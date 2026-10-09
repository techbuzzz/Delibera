namespace Delibera.Server.Infrastructure;

/// <summary>
///    Host-side switch for the Redis-backed debate orchestrator.
/// </summary>
/// <remarks>
///    Deliberately bound to the same <c>Delibera:Redis</c> section that
///    <c>RedisOrchestratorOptions</c> uses, rather than a parallel section such as
///    <c>Delibera:Server:Redis</c>. Two sections describing one Redis deployment is the
///    kind of split that leaves <c>Enabled</c> false while the connection string looks
///    correctly configured — and Redis then silently never runs.
///    <para>
///       Only <see cref="Enabled" />, <see cref="CacheEnabled" /> and <see cref="CacheTtl" />
///       live here; every connection detail stays on <c>RedisOrchestratorOptions</c>, which
///       this type never shadows.
///    </para>
/// </remarks>
public sealed class RedisGateOptions
{
   /// <summary>
   ///    The same section <c>RedisOrchestratorOptions.SectionName</c> resolves to.
   /// </summary>
   public const string SectionName = "Delibera:Redis";

   /// <summary>
   ///    Replaces the in-process <c>LocalDebateOrchestrator</c> with
   ///    <c>RedisDebateOrchestrator</c>. Default <c>false</c>: a host that has never heard
   ///    of Redis must behave exactly as it did before this option existed.
   /// </summary>
   public bool Enabled { get; init; }

   /// <summary>
   ///    Also registers <c>RedisDebateCache</c> as the <c>IDebateCache</c> implementation.
   ///    Requires <see cref="Enabled" />; ignored otherwise.
   /// </summary>
   public bool CacheEnabled { get; init; }

   /// <summary>
   ///    TTL for cached debate results. Null uses the cache's own default.
   /// </summary>
   public TimeSpan? CacheTtl { get; init; }
}