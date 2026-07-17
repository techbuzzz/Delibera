namespace Delibera.Core.Interfaces;

/// <summary>
///    Controls how the <see cref="Council.CouncilExecutor" /> interacts with
///    <see cref="IDebateCache" />.
/// </summary>
public enum CacheBehavior
{
   /// <summary>
   ///    Cache disabled entirely. Default when no <see cref="IDebateCache" /> is registered.
   /// </summary>
   Disabled,

   /// <summary>
   ///    Return cached result if hit; execute and cache if miss.
   /// </summary>
   ReadWrite,

   /// <summary>
   ///    Return cached result if hit; do NOT cache new results.
   /// </summary>
   ReadOnly,

   /// <summary>
   ///    Always execute; always overwrite cache with new result.
   /// </summary>
   WriteThrough,

   /// <summary>
   ///    Bypass cache for this run; do not read or write.
   /// </summary>
   Bypass
}
