# C# Development Standards

## Scope
Applies to: `**/*.cs`

---

## Language Version

- Target **C# 14** with **.NET 10** features.
- Enable nullable reference types in all projects (`<Nullable>enable</Nullable>`).
- Use `<ImplicitUsings>enable</ImplicitUsings>` to reduce boilerplate.

---

## Core Philosophy

Generate code that is **maintainable, testable, and architecturally sound**.
Apply SOLID principles as foundational constraints, not optional considerations.
Leverage C# 14 and .NET 10 features to reduce boilerplate while enforcing clarity.

---

## Naming Conventions

- **Classes, interfaces, enums, records:** `PascalCase`
- **Methods, properties:** `PascalCase`
- **Local variables, parameters:** `camelCase`
- **Private fields:** `_camelCase`
- **Constants:** `PascalCase` (not ALL_CAPS)
- **Interfaces:** prefix with `I` — `IUserService`
- **Async methods:** suffix with `Async` — `GetUserAsync()`
- **Generic type parameters:** `T`, `TResult`, `TKey`, `TValue`

---

## Type System & Nullability

- Enable nullable reference types everywhere.
- Use `?` explicitly for nullable types; never assume nullability.
- Prefer `is null` / `is not null` over `== null`.
- Use null-coalescing `??` and null-conditional `?.` operators.
- Use `ArgumentNullException.ThrowIfNull()` for parameter validation.
- Avoid `null!` suppression unless absolutely necessary and document why.

---

## Modern C# Features — Use Actively

### Primary Constructors (C# 12+)
```csharp
public class UserService(IUserRepository repository, ILogger<UserService> logger)
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await repository.GetByIdAsync(id, ct);
}
```

### Records for Immutable Data
```csharp
public record CreateUserRequest(string Email, string Name, string Password);
public record UserResponse(Guid Id, string Email, string Name, DateTime CreatedAt);
```

### Pattern Matching
```csharp
var message = user.Status switch
{
    UserStatus.Active   => "Account is active",
    UserStatus.Suspended => "Account is suspended",
    UserStatus.Deleted  => "Account has been deleted",
    _                   => "Unknown status"
};
```

### Collection Expressions (C# 12+)
```csharp
string[] tags = ["dotnet", "csharp", "api"];
List<int> ids = [1, 2, 3, 4, 5];
```

### Required Members
```csharp
public class InvoiceConfig
{
    public required string TemplatePath { get; init; }
    public required string OutputDirectory { get; init; }
}
```

### Global Usings
Place in `GlobalUsings.cs` per project:
```csharp
global using System;
global using System.Collections.Generic;
global using System.Threading;
global using System.Threading.Tasks;
global using Microsoft.Extensions.Logging;
```

---

## Async/Await

- Use `async`/`await` for all I/O-bound operations.
- Always suffix async methods with `Async`.
- Return `Task<T>` or `Task`; avoid `async void` (except event handlers).
- Use `ValueTask<T>` for hot paths with frequent synchronous completion.
- Always pass and forward `CancellationToken`.
- Use `ConfigureAwait(false)` in library code.
- Never use `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` in async context.
- Use `Task.WhenAll()` for parallel independent operations.

```csharp
public async Task<IReadOnlyList<Invoice>> GetUserInvoicesAsync(
    Guid userId,
    CancellationToken ct = default)
{
    return await _repository.GetByUserIdAsync(userId, ct);
}
```

---

## SOLID Principles

### Single Responsibility
- One class = one reason to change.
- Split large classes into focused services.
- Controllers delegate to services; services delegate to repositories.

### Open/Closed
- Extend behavior via interfaces and composition, not inheritance.
- Use strategy pattern for varying algorithms.

### Liskov Substitution
- Derived types must be substitutable for base types.
- Do not throw `NotImplementedException` in overrides.

### Interface Segregation
- Prefer small, focused interfaces over large general ones.
- Clients should not depend on methods they don't use.

### Dependency Inversion
- Depend on abstractions, not concrete implementations.
- Register all dependencies via DI container.
- Use constructor injection exclusively.

---

## Dependency Injection

```csharp
// Registration
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
builder.Services.AddTransient<IInvoicePdfGenerator, GemBoxPdfGenerator>();

// Usage via primary constructor
public class InvoiceService(
    IInvoiceRepository repository,
    IInvoicePdfGenerator pdfGenerator,
    ILogger<InvoiceService> logger)
{ }
```

---

## Exception Handling

- Use specific exception types, not generic `Exception`.
- Catch exceptions at the boundary (controller/middleware), not deep in services.
- Log exceptions with context before re-throwing or handling.
- Use `ProblemDetails` for API error responses.
- Never swallow exceptions silently.

```csharp
try
{
    var invoice = await _service.CreateAsync(request, ct);
    return Ok(invoice);
}
catch (SubscriptionLimitExceededException ex)
{
    _logger.LogWarning(ex, "Subscription limit exceeded for user {UserId}", userId);
    return StatusCode(402, new ProblemDetails { Title = "Subscription limit exceeded" });
}
```

---

## Comments & Documentation

- Write XML docs for all public classes, interfaces, methods, and properties.
- Keep existing comments — never remove them unless explicitly asked.
- Add inline comments only where intent is not obvious from the code.
- Use `<summary>` with a present-tense verb.
- Use `<param>`, `<returns>`, `<exception>` where applicable.

```csharp
/// <summary>
/// Creates a new invoice from the parsed AI result and saves it to the database.
/// </summary>
/// <param name="request">The invoice creation request containing parsed data.</param>
/// <param name="ct">A cancellation token.</param>
/// <returns>The created invoice response DTO.</returns>
/// <exception cref="SubscriptionLimitExceededException">
/// Thrown when the user has exceeded their monthly invoice limit.
/// </exception>
public async Task<InvoiceResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken ct = default)
```

---

## Testing

- Use **xUnit** as the test framework.
- Use **FluentAssertions** for readable assertions.
- Use **Moq** or **NSubstitute** for mocking.
- Follow **AAA pattern**: Arrange, Act, Assert.
- Test both success and failure scenarios.
- Test null parameter validation.
- Name tests: `MethodName_Scenario_ExpectedResult`.

```csharp
[Fact]
public async Task CreateAsync_WhenLimitExceeded_ThrowsSubscriptionLimitExceededException()
{
    // Arrange
    var userId = Guid.NewGuid();
    _subscriptionServiceMock
        .Setup(x => x.CanCreateInvoiceAsync(userId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);

    // Act
    var act = async () => await _sut.CreateAsync(new CreateInvoiceRequest(userId, ...), default);

    // Assert
    await act.Should().ThrowAsync<SubscriptionLimitExceededException>();
}
```

---

## Performance

- Avoid unnecessary allocations on hot paths.
- Use `Span<T>` and `Memory<T>` for buffer operations.
- Prefer `StringBuilder` over string concatenation in loops.
- Use `IAsyncEnumerable<T>` for streaming large result sets.
- Avoid LINQ in tight loops — prefer `for`/`foreach`.
- Use `ArrayPool<T>` for temporary large arrays.
- Profile before optimizing — measure, don't guess.

---

## Security

- Never hardcode secrets, connection strings, or API keys.
- Use `IConfiguration` / environment variables / Azure Key Vault.
- Validate all inputs at the boundary.
- Use parameterized queries (EF Core handles this automatically).
- Hash passwords with `BCrypt` or `PBKDF2` — never MD5/SHA1.
- Sanitize user input before passing to AI prompts.
