using System.Text;
using Delibera.Core.Interfaces;
using Delibera.Server.Middleware;
using Delibera.Server.Sse;

namespace Delibera.Server.Api.Endpoints;

public static class DebateEndpoints
{
   public static IEndpointRouteBuilder MapDebateEndpoints(this IEndpointRouteBuilder routes)
   {
      // No WithOpenApi(): it is deprecated in .NET 10 (ASPDEPR002) and its behaviour is
      // now part of the built-in OpenAPI pipeline. The group metadata below (tags, names,
      // summaries, Produces*) is picked up by AddOpenApi()/MapOpenApi() on its own.
      var group = routes.MapGroup("/debates")
         .WithTags("Debates");

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
            "text/markdown");

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
      var record = await orchestration.RunAsync(request, tenantId, ct);
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
      var record = orchestration.Enqueue(request, tenantId);
      var response = record.ToResponse(ctx);
      return TypedResults.Accepted(response.ResultUrl, response);
   }

   private static Results<Ok<DebateResponse>, NotFound>
      GetDebateAsync(
         string id,
         IDebateOrchestrationService orchestration,
         HttpContext ctx)
   {
      var record = orchestration.Find(id, TenantResolutionMiddleware.Resolve(ctx));
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
      var record = orchestration.Find(id, TenantResolutionMiddleware.Resolve(ctx));
      if (record is null) return TypedResults.NotFound();
      if (record.Status != DebateStatus.Completed)
         return TypedResults.Conflict($"Debate is {record.Status}, not yet completed.");

      return TypedResults.Ok(record.ToResponse(ctx).Verdict ??
                             new VerdictDto
                             {
                                Recommendation = record.Result?.FinalVerdict
                             });
   }

   private static async Task StreamDebateAsync(
      string id,
      IDebateOrchestrationService orchestration,
      IDebateOrchestrator debateOrchestrator,
      HttpContext ctx,
      CancellationToken ct)
   {
      var record = orchestration.Find(id, TenantResolutionMiddleware.Resolve(ctx));
      if (record is null)
      {
         ctx.Response.StatusCode = StatusCodes.Status404NotFound;
         return;
      }

      await SseDebateStreamWriter.WriteAsync(record, debateOrchestrator, ctx, ct);
   }

   private static Results<Ok<DebateRoundDto[]>, NotFound>
      GetDebateRoundsAsync(
         string id,
         IDebateOrchestrationService orchestration,
         HttpContext ctx,
         [FromQuery] int page = 1,
         [FromQuery] int pageSize = 20)
   {
      var record = orchestration.Find(id, TenantResolutionMiddleware.Resolve(ctx));
      if (record is null) return TypedResults.NotFound();

      // Snapshot first: the debate may still be running, so paginating over the live
      // collection could mix a page taken before and after a round was appended.
      var rounds = record.RoundsSnapshot()
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
      [FromQuery] string? status = null,
      [FromQuery] int page = 1,
      [FromQuery] int pageSize = 20)
   {
      var debates = orchestration.List(
         TenantResolutionMiddleware.Resolve(ctx), templateId, status, page, pageSize);
      return TypedResults.Ok(debates.Select(r => r.ToResponse(ctx)).ToArray());
   }

   private static Results<NoContent, NotFound>
      CancelDebateAsync(
         string id,
         IDebateOrchestrationService orchestration,
         HttpContext ctx)
   {
      return orchestration.Cancel(id, TenantResolutionMiddleware.Resolve(ctx))
         ? TypedResults.NoContent()
         : TypedResults.NotFound();
   }

   private static Results<FileContentHttpResult, NotFound>
      ExportMarkdownAsync(
         string id,
         IDebateOrchestrationService orchestration,
         HttpContext ctx,
         CancellationToken ct)
   {
      var record = orchestration.Find(id, TenantResolutionMiddleware.Resolve(ctx));
      if (record?.Result is null) return TypedResults.NotFound();

      var md = record.Result.ToMarkdown();
      var bytes = Encoding.UTF8.GetBytes(md);
      return TypedResults.File(bytes, "text/markdown",
         $"debate_{id}_result.md");
   }
}
