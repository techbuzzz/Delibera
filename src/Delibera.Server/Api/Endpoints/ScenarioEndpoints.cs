using Delibera.Core.Interfaces;
using Delibera.Server.Api.Contracts;
using Delibera.Server.Api.Mapping;
using Delibera.Server.Middleware;
using Delibera.Server.Scenarios;
using Delibera.Server.Services;
using Delibera.Server.Sse;

namespace Delibera.Server.Api.Endpoints;

public static class ScenarioEndpoints
{
   public static IEndpointRouteBuilder MapScenarioEndpoints(this IEndpointRouteBuilder routes)
   {
      var group = routes.MapGroup("/scenarios")
         .WithTags("Scenarios")
         .WithOpenApi();

      // POST /api/v1/scenarios  — sync
      group.MapPost("/", RunScenarioAsync)
         .WithName("RunScenario")
         .WithSummary("Run an ad-hoc council scenario from JSON; waits for completion.")
         .Produces<DebateResponse>(StatusCodes.Status201Created)
         .ProducesValidationProblem()
         .ProducesProblem(StatusCodes.Status400BadRequest);

      // POST /api/v1/scenarios/async  — fire-and-forget
      group.MapPost("/async", EnqueueScenarioAsync)
         .WithName("EnqueueScenario")
         .WithSummary("Enqueue an ad-hoc scenario; poll GET /debates/{id} for status.")
         .Produces<DebateResponse>(StatusCodes.Status202Accepted)
         .ProducesValidationProblem();

      // POST /api/v1/scenarios/validate  — dry-run
      group.MapPost("/validate", ValidateScenarioAsync)
         .WithName("ValidateScenario")
         .WithSummary("Validate a scenario JSON without executing it. Returns the parsed council config.")
         .Produces<ScenarioValidationResult>(StatusCodes.Status200OK)
         .ProducesProblem(StatusCodes.Status400BadRequest);

      // GET /api/v1/scenarios/{id}/stream  — SSE
      group.MapGet("/{id}/stream", StreamScenarioAsync)
         .WithName("StreamScenario")
         .WithSummary("SSE stream of DebateRound payloads for a running scenario.")
         .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");

      return routes;
   }

   // ── Handlers ──────────────────────────────────────────────────────────────

   private static async Task<Results<Created<DebateResponse>,
         ValidationProblem,
         ProblemHttpResult>>
      RunScenarioAsync(
         ScenarioRequest scenario,
         IDebateOrchestrationService orchestration,
         HttpContext ctx,
         CancellationToken ct)
   {
      try
      {
         var tenantId = TenantResolutionMiddleware.Resolve(ctx);
         var record = await orchestration.RunScenarioAsync(scenario, tenantId, ct);
         var response = record.ToResponse(ctx);
         return TypedResults.Created(response.ResultUrl, response);
      }
      catch (ArgumentException ex)
      {
         return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
      }
   }

   private static Results<Accepted<DebateResponse>, ValidationProblem>
      EnqueueScenarioAsync(
         ScenarioRequest scenario,
         IDebateOrchestrationService orchestration,
         HttpContext ctx)
   {
      var tenantId = TenantResolutionMiddleware.Resolve(ctx);
      var record = orchestration.EnqueueScenario(scenario, tenantId);
      var response = record.ToResponse(ctx);
      return TypedResults.Accepted(response.ResultUrl, response);
   }

   private static Results<Ok<ScenarioValidationResult>, ProblemHttpResult>
      ValidateScenarioAsync(
         ScenarioRequest scenario,
         IConfiguration configuration)
   {
      try
      {
         // Attempt to build — throws on misconfiguration
         _ = ScenarioBuilder.Build(scenario, configuration);
         var result = new ScenarioValidationResult
         {
            Valid = true,
            MemberCount = scenario.Members.Length,
            Strategy = scenario.Strategy,
            MaxRounds = scenario.MaxRounds,
            HasChairman = scenario.Chairman is not null,
            VotingStrategy = scenario.VotingStrategy,
            Roles = scenario.Members.Select(m => m.Role).ToArray(),
         };
         return TypedResults.Ok(result);
      }
      catch (Exception ex)
      {
         return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
      }
   }

   private static async Task StreamScenarioAsync(
      string id,
      IDebateOrchestrationService orchestration,
      IDebateOrchestrator debateOrchestrator,
      HttpContext ctx,
      CancellationToken ct)
   {
      var record = orchestration.Find(id);
      if (record is null)
      {
         ctx.Response.StatusCode = StatusCodes.Status404NotFound;
         return;
      }

      await SseDebateStreamWriter.WriteAsync(record, debateOrchestrator, ctx, ct);
   }
}

/// <summary>Returned by POST /api/v1/scenarios/validate.</summary>
public sealed record ScenarioValidationResult
{
   public required bool Valid { get; init; }
   public required int MemberCount { get; init; }
   public required string Strategy { get; init; }
   public required int MaxRounds { get; init; }
   public required bool HasChairman { get; init; }
   public string? VotingStrategy { get; init; }
   public required string[] Roles { get; init; }
}
