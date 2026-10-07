using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delibera.Core.Cost;

/// <summary>
///    A sliding-window rate limiter shared by one debate.
/// </summary>
/// <remarks>
///    <para>
///    Buckets are kept per <see cref="RateLimitScope" /> so two members hitting the same model
///    share one budget while different models do not throttle each other.
///    </para>
///    <para>
///    The wait is genuinely asynchronous — a member waiting for capacity suspends rather than
///    pinning a thread-pool thread for the length of the window. That matters here because the
///    caller fans out members in parallel: a blocking wait would turn "wait for the rate limit"
///    into "occupy a thread until the window resets".
///    </para>
/// </remarks>
public sealed class TokenBucketRateLimiter : IRateLimiter
{
   private readonly int _permitLimit;
   private readonly TimeSpan _window;
   private readonly RateLimitBehavior _behavior;
   private readonly RateLimitScope _scope;
   private readonly ConcurrentDictionary<string, Queue<long>> _buckets = new(StringComparer.Ordinal);
   private readonly SemaphoreSlim _signal = new(0, int.MaxValue);

   /// <summary>
   ///    Creates a limiter from a policy.
   /// </summary>
   /// <param name="policy">Permit count, window, behaviour and scope.</param>
   public TokenBucketRateLimiter(RateLimitPolicy policy)
   {
      ArgumentNullException.ThrowIfNull(policy);
      ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.PermitLimit);
      if (policy.Window <= TimeSpan.Zero)
         throw new ArgumentOutOfRangeException(nameof(policy), policy.Window, "Window must be positive.");

      _permitLimit = policy.PermitLimit;
      _window = policy.Window;
      _behavior = policy.Behavior;
      _scope = policy.Scope;
   }

   /// <inheritdoc />
   public async ValueTask AcquireAsync(
      string provider,
      string model,
      string memberName,
      CancellationToken ct = default)
   {
      var key = BuildKey(provider, model, memberName);
      var windowTicks = _window.Ticks;

      while (true)
      {
         ct.ThrowIfCancellationRequested();

         long now = DateTime.UtcNow.Ticks;
         long waitTicks = 0;
         bool granted;

         lock (_buckets)
         {
            if (!_buckets.TryGetValue(key, out var stamps))
            {
               stamps = new Queue<long>();
               _buckets[key] = stamps;
            }

            // Drop stamps that fell out of the window. The queue stays ordered because stamps
            // are only ever appended with an increasing "now".
            while (stamps.Count > 0 && now - stamps.Peek() >= windowTicks)
               stamps.Dequeue();

            if (stamps.Count < _permitLimit)
            {
               stamps.Enqueue(now);
               granted = true;
            }
            else
            {
               granted = false;
               var oldest = stamps.Peek();
               waitTicks = windowTicks - (now - oldest);
               if (waitTicks < 0) waitTicks = 0;
            }
         }

         if (granted)
            return;

         switch (_behavior)
         {
            case RateLimitBehavior.Throw:
               throw new RateLimitExceededException(
                  $"Rate limit exhausted for '{key}': {_permitLimit} calls per {_window.TotalSeconds:0.##}s.");

            case RateLimitBehavior.Drop:
               throw new RateLimitExceededException(
                  $"Rate limit exhausted for '{key}' and the policy is Drop; the call was skipped.");

            default:
               // Wait, then re-check: another member may have released a slot in the meantime,
               // and the window may have rolled forward on its own.
               await WaitForSignalAsync(waitTicks, ct).ConfigureAwait(false);
               break;
         }
      }
   }

   /// <summary>
   ///    Waits until either a slot is signalled or the computed delay elapses. The signal lets
   ///    a member that abandons its wait (cancellation) wake the others immediately instead of
   ///    leaving them parked for the remainder of the window.
   /// </summary>
   private async ValueTask WaitForSignalAsync(long waitTicks, CancellationToken ct)
   {
      var delay = TimeSpan.FromTicks(Math.Min(waitTicks, TimeSpan.TicksPerSecond));
      if (delay <= TimeSpan.Zero)
         return;

      var delayTask = Task.Delay(delay, ct);
      var signalTask = _signal.WaitAsync(ct);
      var completed = await Task.WhenAny(delayTask, signalTask).ConfigureAwait(false);
      await completed.ConfigureAwait(false);
      ct.ThrowIfCancellationRequested();
   }

   private string BuildKey(string provider, string model, string memberName) => _scope switch
   {
      RateLimitScope.Global => "global",
      RateLimitScope.PerProvider => $"provider:{provider}",
      RateLimitScope.PerMember => $"member:{memberName}",
      _ => $"model:{provider}:{model}"
   };
}

/// <summary>
///    A cost ceiling expressed as a hard limit on total spend.
/// </summary>
public sealed class BudgetCostGate : ICostGate
{
   private readonly decimal _limit;
   private readonly CostLimitBehavior _behavior;
   private int _warned;

   /// <summary>
   ///    Creates a gate with the given ceiling.
   /// </summary>
   /// <param name="limit">Maximum total spend. Must be positive.</param>
   /// <param name="behavior">What to do once the ceiling would be crossed.</param>
   public BudgetCostGate(decimal limit, CostLimitBehavior behavior = CostLimitBehavior.Abort)
   {
      if (limit <= 0m)
         throw new ArgumentOutOfRangeException(nameof(limit), limit, "Cost limit must be positive.");

      _limit = limit;
      _behavior = behavior;
   }

   /// <summary>The configured ceiling.</summary>
   public decimal Limit => _limit;

   /// <inheritdoc />
   public ValueTask<CostGateDecision> CheckAsync(CostEstimate current, CancellationToken ct = default)
   {
      ct.ThrowIfCancellationRequested();

      if (current.TotalCost < _limit)
         return ValueTask.FromResult(CostGateDecision.Allow(current.TotalCost));

      var reason =
         $"Cost ceiling reached: {current.TotalCost:F4} spent against a limit of {_limit:F4}.";

      return ValueTask.FromResult(_behavior switch
      {
         // Warn exactly once, however many members the debate has left to call.
         CostLimitBehavior.WarnAndContinue when Interlocked.Exchange(ref _warned, 1) == 0
            => CostGateDecision.Allow(current.TotalCost),

         CostLimitBehavior.Ignore or CostLimitBehavior.WarnAndContinue
            => CostGateDecision.Allow(current.TotalCost),

         _ => CostGateDecision.Deny(current.TotalCost, _limit, reason)
      });
   }
}

/// <summary>
///    Price list backed by an in-memory table, optionally seeded from JSON.
/// </summary>
/// <remarks>
///    Lookup tries an exact (ordinal-ignore-case) match first, then the longest matching
///    substring — the same explicit rule the context-window registry uses. Without that rule,
///    which pattern matched would depend on enumeration order, so a vendored model name like
///    <c>llama3.2:7b</c> could price at one rate or another depending on insertion sequence.
/// </remarks>
public sealed class ModelPricingRegistry : IModelPricingRegistry
{
   private readonly Dictionary<string, ModelPricing> _exact = new(StringComparer.OrdinalIgnoreCase);
   private readonly List<(string Pattern, ModelPricing Pricing)> _patterns = [];

   /// <summary>Fallback price used when no pattern matches; the result is flagged as an estimate.</summary>
   public ModelPricing Fallback { get; set; } = new("unknown", 0.001m, 0.003m);

   /// <summary>Creates an empty registry.</summary>
   public ModelPricingRegistry()
   {
   }

   /// <summary>
   ///    Creates a registry seeded from a JSON array of
   ///    <c>{ "model": "...", "inputPerMillion": 0, "outputPerMillion": 0 }</c> objects.
   /// </summary>
   /// <param name="json">Price table JSON.</param>
   /// <exception cref="JsonException">The payload is not a valid price table.</exception>
   public ModelPricingRegistry(string json)
   {
      ArgumentNullException.ThrowIfNull(json);

      var entries = JsonSerializer.Deserialize<PriceEntry[]>(
         json,
         JsonSerializerOptions.Default);

      if (entries is null) return;

      foreach (var entry in entries)
      {
         if (string.IsNullOrWhiteSpace(entry.Model)) continue;
         Register(new ModelPricing(entry.Model, entry.InputPerMillion, entry.OutputPerMillion));
      }
   }

   /// <inheritdoc />
   public bool TryGetPricing(string modelName, out ModelPricing pricing)
   {
      if (!string.IsNullOrWhiteSpace(modelName) && _exact.TryGetValue(modelName, out var exact))
      {
         pricing = exact;
         return true;
      }

      var bestLength = -1;
      var best = Fallback;
      foreach (var (pattern, candidate) in _patterns)
      {
         if (modelName is null || !modelName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            continue;

         if (pattern.Length <= bestLength) continue;

         bestLength = pattern.Length;
         best = candidate;
      }

      pricing = best;
      return bestLength >= 0;
   }

   /// <inheritdoc />
   public void Register(ModelPricing pricing)
   {
      ArgumentNullException.ThrowIfNull(pricing);

      var key = pricing.ModelName.Trim();
      if (_exact.TryGetValue(key, out var existing))
         _patterns.RemoveAll(p => ReferenceEquals(p.Pricing, existing));

      var entry = new ModelPricing(key, pricing.InputPerMillionTokens, pricing.OutputPerMillionTokens);
      _exact[key] = entry;
      _patterns.Add((key, entry));
   }

   private sealed record PriceEntry
   {
      [JsonPropertyName("model")] public string? Model { get; init; }
      [JsonPropertyName("inputPerMillion")] public decimal InputPerMillion { get; init; }
      [JsonPropertyName("outputPerMillion")] public decimal OutputPerMillion { get; init; }
   }
}