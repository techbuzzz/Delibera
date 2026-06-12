# .NET Architecture & DDD Guidelines

## Scope
Applies to: `**/*.cs`, `**/*.csproj`, `**/Program.cs`, `**/*.razor`

---

## Mandatory Thinking Process

**Before any implementation:**
1. Identify which layer the change belongs to (Domain / Application / Infrastructure / API).
2. Verify the change does not violate layer boundaries.
3. Check if an existing abstraction can be reused or extended.
4. Consider testability — can this be unit tested without infrastructure?
5. Consider backward compatibility — does this break existing contracts?

---

## Architecture: Clean Architecture + DDD

```
┌─────────────────────────────────────────────┐
│                   API Layer                  │  Controllers, Middleware, Program.cs
├─────────────────────────────────────────────┤
│              Application Layer               │  Commands, Queries, Handlers, DTOs, Interfaces
├─────────────────────────────────────────────┤
│               Domain Layer                   │  Entities, Value Objects, Aggregates, Domain Events
├─────────────────────────────────────────────┤
│            Infrastructure Layer              │  EF Core, Repositories, External Services, Email, PDF
└─────────────────────────────────────────────┘
```

### Dependency Rule
Dependencies point **inward only**:
- API → Application → Domain
- Infrastructure → Application → Domain
- Domain has **zero** external dependencies

---

## Domain Layer

### Entities
- Have identity (`Id` of type `Guid`).
- Encapsulate business rules and invariants.
- Expose behavior via methods, not just properties.
- Never expose setters for business-critical state — use methods.

```csharp
public class Invoice
{
    public Guid Id { get; private set; }
    public InvoiceStatus Status { get; private set; }
    private readonly List<InvoiceItem> _items = [];

    public IReadOnlyList<InvoiceItem> Items => _items.AsReadOnly();

    public void AddItem(string description, decimal quantity, decimal unitPrice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _items.Add(new InvoiceItem(description, quantity, unitPrice));
    }

    public void MarkAsSent()
    {
        if (Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Only draft invoices can be sent.");
        Status = InvoiceStatus.Sent;
    }
}
```

### Value Objects
- No identity — equality by value.
- Immutable.
- Use records.

```csharp
public record Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0, currency);
    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot add different currencies.");
        return new Money(Amount + other.Amount, Currency);
    }
}
```

### Aggregates
- One aggregate root per transaction boundary.
- External code accesses aggregate internals only through the root.
- Keep aggregates small — split if they grow too large.

### Domain Events
- Raise domain events for significant state changes.
- Use `INotification` (MediatR) or a custom `IDomainEvent` interface.
- Dispatch events after the transaction commits.

---

## Application Layer

### CQRS with MediatR

```csharp
// Command
public record CreateInvoiceCommand(Guid UserId, string RawText) : IRequest<InvoiceResponse>;

// Handler
public class CreateInvoiceCommandHandler(
    IInvoiceRepository repository,
    IAiParsingService aiParser,
    ILogger<CreateInvoiceCommandHandler> logger)
    : IRequestHandler<CreateInvoiceCommand, InvoiceResponse>
{
    public async Task<InvoiceResponse> Handle(CreateInvoiceCommand request, CancellationToken ct)
    {
        var parsed = await aiParser.ParseAsync(request.RawText, ct);
        var invoice = Invoice.Create(request.UserId, parsed);
        await repository.AddAsync(invoice, ct);
        logger.LogInformation("Invoice {InvoiceId} created for user {UserId}", invoice.Id, request.UserId);
        return InvoiceResponse.FromDomain(invoice);
    }
}
```

### Pipeline Behaviors
Use MediatR pipeline behaviors for cross-cutting concerns:
- `ValidationBehavior<TRequest, TResponse>` — FluentValidation
- `LoggingBehavior<TRequest, TResponse>` — structured logging
- `TransactionBehavior<TRequest, TResponse>` — DB transactions for commands

### Application Services
- Orchestrate domain objects and infrastructure.
- Do not contain business logic — that belongs in the domain.
- Return DTOs, not domain entities.

---

## Infrastructure Layer

### Repository Pattern
- Define interfaces in Application layer.
- Implement in Infrastructure layer.
- Use EF Core as the ORM.
- Keep repositories focused — one per aggregate root.

```csharp
// Interface in Application
public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Invoice>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Invoice invoice, CancellationToken ct = default);
    Task UpdateAsync(Invoice invoice, CancellationToken ct = default);
}

// Implementation in Infrastructure
public class InvoiceRepository(AppDbContext context) : IInvoiceRepository
{
    public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.Invoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
}
```

### EF Core Conventions
- Use Fluent API in `OnModelCreating` — not data annotations on entities.
- PascalCase table names.
- `HasDefaultValueSql("NOW()")` for timestamps.
- `HasMaxLength` + `IsRequired` for strings.
- Configure owned entities and value objects explicitly.
- Use `AsNoTracking()` for read-only queries.

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Invoice>(e =>
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Status).HasConversion<string>();
        e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
        e.OwnsMany(x => x.Items, items =>
        {
            items.WithOwner().HasForeignKey("InvoiceId");
            items.Property(i => i.Description).HasMaxLength(500).IsRequired();
        });
    });
}
```

---

## API Layer

### Controllers
- Thin controllers — delegate everything to MediatR.
- Use `[ApiController]` and `[Route]` attributes.
- Return `IActionResult` or `ActionResult<T>`.
- Use `[ProducesResponseType]` for Swagger documentation.
- Extract `UserId` from JWT claims, never from request body.

```csharp
[ApiController]
[Route("api/invoices")]
[Authorize]
public class InvoicesController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(InvoiceResponse), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 402)]
    public async Task<IActionResult> Create(
        [FromBody] CreateInvoiceRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId(); // extension method on ClaimsPrincipal
        var command = new CreateInvoiceCommand(userId, request.RawText);
        var result = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }
}
```

### Minimal API (alternative)
Use for simple endpoints without complex authorization:
```csharp
app.MapPost("/api/invoices", async (
    CreateInvoiceRequest request,
    IMediator mediator,
    ClaimsPrincipal user,
    CancellationToken ct) =>
{
    var command = new CreateInvoiceCommand(user.GetUserId(), request.RawText);
    var result = await mediator.Send(command, ct);
    return Results.Created($"/api/invoices/{result.Id}", result);
}).RequireAuthorization();
```

---

## Validation

- Use **FluentValidation** for command/request validation.
- Register validators via `AddValidatorsFromAssembly`.
- Validate in pipeline behavior, not in handlers.

```csharp
public class CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RawText).NotEmpty().MaximumLength(2000);
    }
}
```

---

## Logging

- Use `ILogger<T>` everywhere.
- Use structured logging with named parameters.
- Log at appropriate levels: `Debug`, `Information`, `Warning`, `Error`.
- Include correlation IDs for request tracing.
- Never log sensitive data (passwords, tokens, payment details).

```csharp
_logger.LogInformation(
    "Invoice {InvoiceId} created for user {UserId} with {ItemCount} items",
    invoice.Id, userId, invoice.Items.Count);
```

---

## Configuration

- Use strongly-typed configuration classes.
- Bind via `IOptions<T>` pattern.
- Validate configuration at startup with `ValidateDataAnnotations()` and `ValidateOnStart()`.

```csharp
public class AzureOpenAiOptions
{
    public const string SectionName = "AzureOpenAI";

    [Required] public string Endpoint { get; init; } = string.Empty;
    [Required] public string ApiKey { get; init; } = string.Empty;
    [Required] public string DeploymentName { get; init; } = string.Empty;
}

// Registration
builder.Services
    .AddOptions<AzureOpenAiOptions>()
    .BindConfiguration(AzureOpenAiOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

---

## Key Rules

- Domain layer has **zero** infrastructure dependencies.
- Business logic lives in the **domain**, not in services or handlers.
- Handlers orchestrate — they do not contain business rules.
- Never return EF entities from Application layer — always map to DTOs.
- One aggregate root per transaction.
- Keep aggregates small and focused.
- Prefer composition over inheritance.
- Avoid static state and static methods in domain logic.
