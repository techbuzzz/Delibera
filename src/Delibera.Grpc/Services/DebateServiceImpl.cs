using Delibera.Core.Interfaces;
using Delibera.Core.Models;
using Delibera.Grpc.V1;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using CoreRoundEvent = Delibera.Core.Interfaces.DebateRoundEvent;

namespace Delibera.Grpc.Services;

/// <summary>
///    gRPC surface for debates.
/// </summary>
/// <remarks>
///    <para>
///    Sits <b>on top of</b> <see cref="IDebateOrchestrator" /> rather than beside it. That is the
///    point of having the abstraction: a debate executed over gRPC takes the same
///    distributed-execution and result-caching path as one executed over REST. A parallel gRPC
///    pipeline would silently ignore <c>WithOrchestrator</c> and <c>WithCache</c>, so the same
///    council would behave differently depending on the transport.
///    </para>
///    <para>
///    The builder arrives per call because the orchestrator owns execution and a
///    <see cref="ICouncilBuilder" /> cannot be resolved from DI the way a provider can.
///    </para>
/// </remarks>
public sealed class DebateServiceImpl : DebateService.DebateServiceBase
{
   private readonly IDebateOrchestrator _orchestrator;
   private readonly Func<ICouncilBuilder> _builderFactory;
   private readonly ILogger<DebateServiceImpl> _logger;

   /// <summary>Creates the service.</summary>
   /// <param name="orchestrator">Execution backend shared with the REST surface.</param>
   /// <param name="builderFactory">Supplies the configured council builder for a call.</param>
   /// <param name="logger">Logger.</param>
   public DebateServiceImpl(
      IDebateOrchestrator orchestrator,
      Func<ICouncilBuilder> builderFactory,
      ILogger<DebateServiceImpl> logger)
   {
      _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
      _builderFactory = builderFactory ?? throw new ArgumentNullException(nameof(builderFactory));
      _logger = logger ?? throw new ArgumentNullException(nameof(logger));
   }

   /// <inheritdoc />
   public override async Task<DebateResponse> CreateDebate(
      CreateDebateRequest request,
      ServerCallContext context)
   {
      ArgumentNullException.ThrowIfNull(request);

      if (string.IsNullOrWhiteSpace(request.Question))
         throw new RpcException(new Status(StatusCode.InvalidArgument, "question is required."));

      var handle = await _orchestrator
         .EnqueueAsync(Guid.NewGuid().ToString("N"), BuildFrom(request), context.CancellationToken)
         .ConfigureAwait(false);

      return DebateMapper.ToResponse(handle);
   }

   /// <inheritdoc />
   public override async Task<DebateResponse> GetDebate(
      GetDebateRequest request,
      ServerCallContext context)
   {
      ArgumentNullException.ThrowIfNull(request);

      var handle = await _orchestrator
         .GetStatusAsync(request.DebateId, context.CancellationToken)
         .ConfigureAwait(false);

      if (handle is null)
      {
         throw new RpcException(new Status(
            StatusCode.NotFound,
            $"No debate with id '{request.DebateId}'."));
      }

      return DebateMapper.ToResponse(handle);
   }

   /// <inheritdoc />
   public override async Task<CancelDebateResponse> CancelDebate(
      CancelDebateRequest request,
      ServerCallContext context)
   {
      ArgumentNullException.ThrowIfNull(request);

      var cancelled = await _orchestrator
         .CancelAsync(request.DebateId, context.CancellationToken)
         .ConfigureAwait(false);

      return new CancelDebateResponse { Cancelled = cancelled };
   }

   /// <inheritdoc />
   /// <remarks>
   ///    <para>
   ///    Exactly one terminal event is written before the stream ends — <c>completed</c> on
   ///    success, <c>error</c> on failure or on an early end. A client cannot otherwise tell a
   ///    finished debate from a dropped connection, which is the same confusion the SSE writer had
   ///    before it gained its terminal event.
   ///    </para>
   ///    <para>
   ///    Iteration is a plain <c>await foreach</c>: one <c>MoveNextAsync</c> in flight at a time,
   ///    awaited to completion before the next. The SSE writer shipped a defect here — it compared
   ///    a winner against a second <c>ValueTask.AsTask()</c> call, which returns a <em>different</em>
   ///    <see cref="Task" /> instance, so every genuine event was misread as a heartbeat and a
   ///    debate streamed zero rounds.
   ///    </para>
   /// </remarks>
   public override async Task StreamDebate(
      GetDebateRequest request,
      IServerStreamWriter<DebateStreamEvent> responseStream,
      ServerCallContext context)
   {
      ArgumentNullException.ThrowIfNull(request);
      ArgumentNullException.ThrowIfNull(responseStream);

      // A client that walks away should stop the debate rather than leave it burning tokens.
      context.CancellationToken.Register(() =>
      {
         // Fire-and-forget: the request context is already tearing down, and the orchestrator's own
         // cancellation path is the one that has to stay intact.
         _ = _orchestrator.CancelAsync(request.DebateId, CancellationToken.None);
      });

      var handle = await _orchestrator
         .GetStatusAsync(request.DebateId, context.CancellationToken)
         .ConfigureAwait(false);

      if (handle is null)
      {
         await WriteErrorAsync(
            responseStream,
            $"No debate with id '{request.DebateId}'.",
            context.CancellationToken).ConfigureAwait(false);
         return;
      }

      await WriteEventAsync(
         responseStream,
         new DebateStreamEvent { Started = DebateMapper.ToResponse(handle) },
         context.CancellationToken).ConfigureAwait(false);

      var terminalWritten = false;

      await foreach (var streamEvent in _orchestrator
         .StreamAsync(request.DebateId, context.CancellationToken)
         .ConfigureAwait(false))
      {
         switch (streamEvent)
         {
            case CoreRoundEvent.RoundCompleted completed:
               await WriteEventAsync(
                  responseStream,
                  new DebateStreamEvent { Round = DebateMapper.ToRound(completed.DebateId, completed.Round) },
                  context.CancellationToken).ConfigureAwait(false);
               break;

            case CoreRoundEvent.DebateCompleted done when !terminalWritten:
               await WriteEventAsync(
                  responseStream,
                  new DebateStreamEvent
                  {
                     Completed = new DebateResponse
                     {
                        DebateId = done.DebateId,
                        Status = DebateStatus.Completed,
                        CreatedAt = DebateMapper.ToTimestamp(done.Result.StartedAt),
                        CompletedAt = done.Result.CompletedAt is { } at
                           ? DebateMapper.ToTimestamp(at)
                           : null
                     }
                  },
                  context.CancellationToken).ConfigureAwait(false);
               terminalWritten = true;
               break;

            case CoreRoundEvent.DebateFailed failed when !terminalWritten:
               await WriteErrorAsync(responseStream, failed.Error, context.CancellationToken).ConfigureAwait(false);
               terminalWritten = true;
               break;
         }
      }

      if (!terminalWritten)
      {
         // The stream ended without a completion or a failure. Saying so is the whole point: a
         // silent end is indistinguishable from a network drop.
         await WriteErrorAsync(
            responseStream,
            "The event stream ended before a terminal event was received.",
            context.CancellationToken).ConfigureAwait(false);
      }
   }

   private ICouncilBuilder BuildFrom(CreateDebateRequest request)
   {
      var builder = _builderFactory().WithUserPrompt(request.Question);

      if (request.Options is { } options)
      {
         if (options.MaxRounds is { } rounds) builder = builder.WithMaxRounds(rounds);
         if (options.Temperature is { } temperature) builder = builder.WithTemperature(temperature);
      }

      return builder;
   }

   private async Task WriteEventAsync(
      IServerStreamWriter<DebateStreamEvent> responseStream,
      DebateStreamEvent streamEvent,
      CancellationToken ct)
   {
      try
      {
         await responseStream.WriteAsync(streamEvent).ConfigureAwait(false);
      }
      catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
      {
         // The client went away. Not worth propagating — the debate was cancelled alongside it.
         _logger.LogDebug("gRPC stream cancelled by the client.");
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
         _logger.LogDebug("gRPC stream cancelled with the request context.");
      }
   }

   private Task WriteErrorAsync(
      IServerStreamWriter<DebateStreamEvent> responseStream,
      string message,
      CancellationToken ct)
      => WriteEventAsync(
         responseStream,
         new DebateStreamEvent
         {
            Error = new DebateStreamError { Message = message, Status = DebateStatus.Failed }
         },
         ct);
}

/// <summary>
///    Translates between the framework's models and the protobuf contract.
/// </summary>
/// <remarks>
///    Judgement calls live here rather than in the service so every transport applies the same
///    rule — notably <see cref="DebateStatusValue" />, which pins the wire ordinals against the
///    enum the REST API serialises as strings.
/// </remarks>
public static class DebateMapper
{
   /// <summary>Maps a handle to its wire response.</summary>
   /// <param name="handle">The handle.</param>
   public static DebateResponse ToResponse(DebateHandle handle)
   {
      ArgumentNullException.ThrowIfNull(handle);

      return new DebateResponse
      {
         DebateId = handle.DebateId,
         Status = DebateStatusValue(handle.Status),
         CreatedAt = ToTimestamp(handle.CreatedAt)
      };
   }

   /// <summary>Maps a completed round to its wire shape.</summary>
   /// <param name="debateId">Owning debate, so a client can multiplex several streams.</param>
   /// <param name="round">The completed round.</param>
   public static RoundDto ToRound(string debateId, DebateRound round)
   {
      ArgumentNullException.ThrowIfNull(round);

      var dto = new RoundDto
      {
         DebateId = debateId,
         RoundNumber = round.RoundNumber,
         RoundName = round.RoundName ?? string.Empty,
         Description = round.Description ?? string.Empty
      };

      foreach (var (member, response) in round.Responses)
         dto.Responses.Add(member, response);

      return dto;
   }

   /// <summary>
   ///   Maps the framework's debate status onto the wire enum.
   /// </summary>
   /// <remarks>
   ///   Explicit rather than a cast, because the proto pins numeric values while the REST API
   ///   serialises the same states as strings. An implicit cast would keep compiling through a
   ///   reordering of one enum while silently disagreeing with the other.
   /// </remarks>
   /// <param name="status">Framework status.</param>
   public static DebateStatus DebateStatusValue(DebateOrchestrationStatus status) => status switch
   {
      DebateOrchestrationStatus.Running => DebateStatus.Running,
      DebateOrchestrationStatus.Completed => DebateStatus.Completed,
      DebateOrchestrationStatus.Cancelled => DebateStatus.Cancelled,
      DebateOrchestrationStatus.Failed => DebateStatus.Failed,
      _ => DebateStatus.Unspecified
   };

   /// <summary>
   ///   Converts a framework timestamp into the protobuf well-known type.
   /// </summary>
   /// <remarks>
   ///   Unspecified for the default instant rather than a 1970 epoch: a missing completion time
   ///   is not the same fact as "completed at the epoch", and protobuf's own default for an unset
   ///   field is exactly that missing value.
   /// </remarks>
   /// <param name="value">The instant to convert.</param>
   public static Google.Protobuf.WellKnownTypes.Timestamp? ToTimestamp(DateTime value)
      => value == default
         ? null
         : Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(
            new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

   /// <summary>Converts an offset-carrying instant, preserving the instant itself.</summary>
   /// <param name="value">The instant to convert.</param>
   public static Google.Protobuf.WellKnownTypes.Timestamp ToTimestamp(DateTimeOffset value)
      => Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(value);
}