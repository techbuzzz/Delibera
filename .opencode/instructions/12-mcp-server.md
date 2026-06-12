# C# MCP Server Development

## Scope
Applies to: `**/*.cs`, `**/*.csproj`

---

## NuGet Packages

- Use **ModelContextProtocol** (prerelease) for most projects:
  ```
  dotnet add package ModelContextProtocol --prerelease
  ```
- Use **ModelContextProtocol.AspNetCore** for HTTP-based MCP servers.
- Use **ModelContextProtocol.Core** for minimal dependencies (client-only or low-level server APIs).

---

## Core Rules

- Always configure logging to stderr: `LogToStandardErrorThreshold = LogLevel.Trace` — avoids interfering with stdio transport.
- Use `[McpServerToolType]` attribute on classes containing MCP tools.
- Use `[McpServerTool]` attribute on methods to expose them as tools.
- Use `[Description]` from `System.ComponentModel` to document tools and parameters.
- Support DI in tool methods — inject `McpServer`, `HttpClient`, or other services as parameters.
- Use `McpServer.AsSamplingChatClient()` to make sampling requests back to the client from within tools.
- Expose prompts using `[McpServerPromptType]` on classes and `[McpServerPrompt]` on methods.
- For stdio transport, use `WithStdioServerTransport()` when building the server.
- Use `WithToolsFromAssembly()` to auto-discover and register all tools from the current assembly.
- Tool methods can be synchronous or async (`Task` or `Task<T>`).
- Always include comprehensive descriptions for tools and parameters.
- Use `CancellationToken` parameters in async tools.
- Return simple types (`string`, `int`) or complex objects serializable to JSON.
- For fine-grained control, use `McpServerOptions` with custom `ListToolsHandler` and `CallToolHandler`.
- Use `McpProtocolException` for protocol-level errors with appropriate `McpErrorCode` values.
- Test MCP servers using `McpClient` from the same SDK or any compliant MCP client.
- Structure projects with `Microsoft.Extensions.Hosting` for proper DI and lifecycle management.

---

## Best Practices

- Keep tool methods focused and single-purpose.
- Use meaningful tool names that clearly indicate their function.
- Provide detailed descriptions explaining what the tool does, what parameters it expects, and what it returns.
- Validate input parameters and throw `McpProtocolException` with `McpErrorCode.InvalidParams` for invalid inputs.
- Use structured logging to help with debugging without polluting stdout.
- Organize related tools into logical classes with `[McpServerToolType]`.
- Consider security implications when exposing tools that access external resources.
- Use the built-in DI container to manage service lifetimes and dependencies.
- Implement proper error handling and return meaningful error messages.
- Test tools individually before integrating with LLMs.

---

## Common Patterns

### Basic Server Setup

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

### Simple Tool

```csharp
[McpServerToolType]
public class InvoiceTools(IInvoiceService invoiceService)
{
    [McpServerTool]
    [Description("Creates a new invoice from natural language text. Returns the created invoice ID and number.")]
    public async Task<string> CreateInvoiceFromText(
        [Description("Natural language description of the invoice, e.g. 'Redesign for LLC Romashka, 80k, due Dec 25'")] 
        string text,
        [Description("The user ID for whom to create the invoice")] 
        Guid userId,
        CancellationToken ct = default)
    {
        var result = await invoiceService.CreateFromTextAsync(userId, text, ct);
        return $"Invoice created: ID={result.Id}, Number={result.Number}";
    }
}
```

### Tool with Dependency Injection

```csharp
[McpServerToolType]
public class DatabaseTools(AppDbContext context, ILogger<DatabaseTools> logger)
{
    [McpServerTool]
    [Description("Gets invoice statistics for a user.")]
    public async Task<object> GetInvoiceStats(
        [Description("The user ID")] Guid userId,
        CancellationToken ct = default)
    {
        var stats = await context.Invoices
            .Where(i => i.UserId == userId)
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync(ct);

        logger.LogInformation("Stats retrieved for user {UserId}", userId);
        return stats;
    }
}
```

### HTTP-based MCP Server (ASP.NET Core)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.MapMcp("/mcp");
await app.RunAsync();
```

### Error Handling

```csharp
[McpServerTool]
[Description("Retrieves an invoice by ID.")]
public async Task<InvoiceResponse> GetInvoice(
    [Description("The invoice ID")] Guid invoiceId,
    CancellationToken ct = default)
{
    var invoice = await _repository.GetByIdAsync(invoiceId, ct);
    if (invoice is null)
        throw new McpProtocolException(McpErrorCode.InvalidParams, $"Invoice {invoiceId} not found.");
    return InvoiceResponse.FromDomain(invoice);
}
```
