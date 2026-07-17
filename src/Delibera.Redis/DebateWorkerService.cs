using System.Text.Json;
using Delibera.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Delibera.Redis;

/// <summary>
///    Background service that consumes debate jobs from a Redis Stream
///    and executes them using <see cref="ICouncilExecutor" />.
///    <para>
///       Each worker instance reads from the <c>delibera:jobs</c> stream as a
///       consumer in the <c>workers</c> group. When a job message is received, the
///       worker deserializes the <see cref="ICouncilBuilder" /> configuration,
///       builds the executor, runs the debate, and publishes round events to the
///       <c>delibera:events</c> stream.
///    </para>
///    <para>
///       In the current iteration, <see cref="RedisDebateOrchestrator" /> runs the
///       debate locally and publishes events inline. This worker exists for
///       future distributed turn-level execution where a separate process picks up
///       individual member turns. It is registered but not active by default.
///    </para>
/// </summary>
public sealed class DebateWorkerService : BackgroundService
{
    private readonly RedisOrchestratorOptions _options;
    private readonly IDebateOrchestrator _orchestrator;
    private readonly ILogger<DebateWorkerService> _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;

    public DebateWorkerService(
        IOptions<RedisOrchestratorOptions> options,
        IDebateOrchestrator orchestrator,
        IConnectionMultiplexer redis,
        ILogger<DebateWorkerService> logger)
    {
        _options      = options.Value;
        _orchestrator = orchestrator;
        _redis        = redis;
        _logger       = logger;
        _db           = redis.GetDatabase();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DebateWorkerService started. Consumer: {Consumer}@{Group}, Stream: {Stream}",
            _options.WorkerConsumerName, _options.WorkerConsumerGroup, _options.JobStreamKey);

        await EnsureConsumerGroupAsync().ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await _db.StreamReadGroupAsync(
                    _options.JobStreamKey,
                    _options.WorkerConsumerGroup,
                    _options.WorkerConsumerName,
                    StreamPosition.NewMessages,
                    _options.BatchSize).ConfigureAwait(false);

                if (messages.Length == 0)
                    continue;

                foreach (var message in messages)
                {
                    await ProcessMessageAsync(message, stoppingToken).ConfigureAwait(false);

                    // Acknowledge the message so it's removed from the pending list.
                    await _db.StreamAcknowledgeAsync(
                        _options.JobStreamKey,
                        _options.WorkerConsumerGroup,
                        message.Id).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DebateWorkerService encountered an error. Retrying in 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("DebateWorkerService stopped.");
    }

    private async Task ProcessMessageAsync(StreamEntry message, CancellationToken ct)
    {
        var debateId = (string?)null;
        try
        {
            debateId = message.Values.FirstOrDefault(v => v.Name == "debateId").Value;
            if (string.IsNullOrEmpty(debateId))
            {
                _logger.LogWarning("Received job message {MessageId} without debateId. Skipping.", message.Id);
                return;
            }

            var jobType = message.Values.FirstOrDefault(v => v.Name == "jobType").Value;
            _logger.LogInformation("Processing job {DebateId} of type {JobType}.", debateId, jobType);

            // Future: distributed turn-level execution will be handled here.
            // For now, the RedisDebateOrchestrator handles execution locally,
            // so this worker serves as a placeholder for the distributed execution path.
            _logger.LogDebug("Job {DebateId}: no-op in current iteration (orchestrator runs locally).", debateId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process job {DebateId}.", debateId);
        }
    }

    private async Task EnsureConsumerGroupAsync()
    {
        try
        {
            await _db.StreamCreateConsumerGroupAsync(
                _options.JobStreamKey,
                _options.WorkerConsumerGroup,
                "0-0").ConfigureAwait(false);
        }
        catch (RedisException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // Consumer group already exists — that's fine.
        }
    }
}