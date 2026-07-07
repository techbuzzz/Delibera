using Delibera.Server.Api.Contracts;
using Delibera.Server.Api.Mapping;
using Delibera.Server.Middleware;
using Delibera.Server.Services;
using Delibera.Server.Sse;

namespace Delibera.Server.Api.Endpoints;

public static class DebateEndpoints
{
    public static IEndpointRouteBuilder MapDebateEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/debates")
            .WithTags("Debates")
            .WithOpenApi();

        // POST /api/v1/debates  — sync execution (waits for completion)
        group.MapPost("/", CreateDebateAsync)
            .WithName("CreateDebate")
            .WithSummary("Run a council debate synchronously and return the verdict.")
            .Produces<DebateResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/v1/debates/async  — fire-and-forget, returns 202 immediately
        group.MapPost("/async", CreateDebateAsyncBackground)
            .WithName("CreateDebateAsync")
            .WithSummary("Enqueue a debate; poll GET /debates/{id} for status.")
            .Produces<DebateResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();

        // GET /api/v1/debates/{id}
        group.MapGet("/{id}", GetDebateAsync)
            .WithName("GetDebate")
            .WithSummary("Get debate status and verdict.")
            .Produces<DebateResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/v1/debates/{id}/result
        group.MapGet("/{id}/result", GetDebateResultAsync)
            .WithName("GetDebateResult")
            .WithSummary("Get only the structured verdict of a completed debate.")
            .Produces<VerdictDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // GET /api/v1/debates/{id}/stream  — SSE
        group.MapGet("/{id}/stream", StreamDebateAsync)
            .WithName("StreamDebate")
            .WithSummary("Server-Sent Events stream of DebateRound payloads.")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");

        // GET /api/v1/debates/{id}/rounds  — paginated
        group.MapGet("/{id}/rounds", GetDebateRoundsAsync)
            .WithName("GetDebateRounds")
            .WithSummary("Return completed rounds (paginated).")
            .Produces<DebateRoundDto[]>();

        // GET /api/v1/debates  — list
        group.MapGet("/", ListDebatesAsync)
            .WithName("ListDebates")
            .WithSummary("List debates with optional filtering.");

        // DELETE /api/v1/debates/{id}
        group.MapDelete("/{id}", CancelDebateAsync)
            .WithName("CancelDebate")
            .WithSummary("Cancel a running or pending debate.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/v1/debates/{id}/export/markdown
        group.MapGet("/{id}/export/markdown", ExportMarkdownAsync)
            .WithName("ExportMarkdown")
            .WithSummary("Download the full debate transcript as Markdown.")
            .Produces<FileContentHttpResult>(StatusCodes.Status200OK,
                contentType: "text/markdown");

        return routes;
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private static async Task<Results<Created<DebateResponse>,
                                      ValidationProblem,
                                      ProblemHttpResult>>
    CreateDebateAsync(
        CreateDebateRequest request,
        IDebateOrchestrationService orchestration,
        HttpContext ctx,
        CancellationToken ct)
    {
        var tenantId = TenantResolutionMiddleware.Resolve(ctx);
        var record   = await orchestration.RunAsync(request, tenantId, ct);
        var response = record.ToResponse(ctx);
        return TypedResults.Created(response.ResultUrl, response);
    }

    private static Results<Accepted<DebateResponse>, ValidationProblem>
    CreateDebateAsyncBackground(
        CreateDebateRequest request,
        IDebateOrchestrationService orchestration,
        HttpContext ctx)
    {
        var tenantId = TenantResolutionMiddleware.Resolve(ctx);
        var record   = orchestration.Enqueue(request, tenantId);
        var response = record.ToResponse(ctx);
        return TypedResults.Accepted(response.ResultUrl, response);
    }

    private static Results<Ok<DebateResponse>, NotFound>
    GetDebateAsync(
        string id,
        IDebateOrchestrationService orchestration,
        HttpContext ctx)
    {
        var record = orchestration.Find(id);
        return record is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(record.ToResponse(ctx));
    }

    private static Results<Ok<VerdictDto>, NotFound, Conflict<string>>
    GetDebateResultAsync(
        string id,
        IDebateOrchestrationService orchestration,
        HttpContext ctx)
    {
        var record = orchestration.Find(id);
        if (record is null)  return TypedResults.NotFound();
        if (record.Status != DebateStatus.Completed)
            return TypedResults.Conflict($"Debate is {record.Status}, not yet completed.");

        return TypedResults.Ok(record.ToResponse(ctx).Verdict ?? new VerdictDto
        {
            Recommendation = record.Result?.FinalVerdict
        });
    }

    private static async Task StreamDebateAsync(
        string id,
        IDebateOrchestrationService orchestration,
        HttpContext ctx,
        CancellationToken ct)
    {
        var record = orchestration.Find(id);
        if (record is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await SseDebateStreamWriter.WriteAsync(record, ctx, ct);
    }

    private static Results<Ok<DebateRoundDto[]>, NotFound>
    GetDebateRoundsAsync(
        string id,
        IDebateOrchestrationService orchestration,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var record = orchestration.Find(id);
        if (record is null) return TypedResults.NotFound();

        var rounds = record.Rounds
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => r.ToDto())
            .ToArray();

        return TypedResults.Ok(rounds);
    }

    private static Ok<DebateResponse[]> ListDebatesAsync(
        IDebateOrchestrationService orchestration,
        HttpContext ctx,
        [FromQuery] string? templateId = null,
        [FromQuery] string? status     = null,
        [FromQuery] int     page       = 1,
        [FromQuery] int     pageSize   = 20)
    {
        var debates = orchestration.List(templateId, status, page, pageSize);
        return TypedResults.Ok(debates.Select(r => r.ToResponse(ctx)).ToArray());
    }

    private static Results<NoContent, NotFound>
    CancelDebateAsync(
        string id,
        IDebateOrchestrationService orchestration)
    {
        return orchestration.Cancel(id)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<FileContentHttpResult, NotFound>>
    ExportMarkdownAsync(
        string id,
        IDebateOrchestrationService orchestration,
        CancellationToken ct)
    {
        var record = orchestration.Find(id);
        if (record?.Result is null) return TypedResults.NotFound();

        var md = await record.Result.ToMarkdownAsync(ct);
        var bytes = System.Text.Encoding.UTF8.GetBytes(md);
        return TypedResults.File(bytes, "text/markdown",
            fileDownloadName: $"debate_{id}_result.md");
    }
}
