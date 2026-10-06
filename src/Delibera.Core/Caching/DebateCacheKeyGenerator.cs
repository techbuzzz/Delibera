using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Delibera.Core.Models;

namespace Delibera.Core.Caching;

/// <summary>
///    Generates deterministic cache keys for debate results.
///    The key is derived from the debate-defining inputs: question, members,
///    chairman, strategy, knowledge, and configuration — everything that affects the
///    debate output. Two debates with identical inputs produce the same cache key.
/// </summary>
public static class DebateCacheKeyGenerator
{
   /// <summary>
   ///    Bumped whenever the set of hashed inputs changes, so that keys minted by an
   ///    older version can never be read back as if they described the same debate.
   /// </summary>
   public const int KeyVersion = 2;

   private static readonly JsonSerializerOptions _json = new()
   {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
      WriteIndented = false,
   };

   /// <summary>
   ///    Generates a 16-character hex cache key from debate-defining inputs.
   ///    Uses SHA-256 of the serialized content, truncated to 16 hex chars.
   /// </summary>
   /// <param name="context">The prompt context containing the question and knowledge.</param>
   /// <param name="members">
   ///    Identity of each council member, ordered deterministically by the caller-visible
   ///    name. The caller decides how much identity is significant — the executor includes
   ///    model, provider, role and persona, because any of them changes the output.
   /// </param>
   /// <param name="strategyName">Strategy name (e.g. "Standard", "Critique").</param>
   /// <param name="maxRounds">Maximum number of debate rounds.</param>
   /// <param name="temperature">LLM temperature.</param>
   /// <param name="systemPrompt">System prompt (if overridden).</param>
   /// <param name="chairmanName">
   ///    Identity of the chairman (model, provider and prompt). The chairman produces the
   ///    final verdict, so two debates differing only in their chairman are not the same
   ///    debate and must not share a cache entry.
   /// </param>
   public static string Generate(
      PromptContext context,
      IReadOnlyList<string> members,
      string strategyName,
      int maxRounds,
      float temperature,
      string? systemPrompt = null,
      string? chairmanName = null)
   {
      var bytes = JsonSerializer.SerializeToUtf8Bytes(new
      {
         Version = KeyVersion,
         Question = context.UserPrompt,
         Members = members.Order(StringComparer.Ordinal),
         Chairman = chairmanName,
         Strategy = strategyName,
         MaxRounds = maxRounds,
         Temperature = temperature,
         SystemPrompt = systemPrompt,
         // Hashed rather than embedded: the knowledge base can be megabytes, and the
         // outer hash covers the digest just as well.
         KnowledgeHash = context.KnowledgeContent is null
            ? null
            : ComputeKnowledgeHash(context.KnowledgeContent),
      }, _json);

      // Truncate to the first 8 bytes = 16 hex chars. Formatting only those 8 bytes
      // produces the same string as slicing the full 64-char hex, without building
      // (and immediately discarding) the 64-char intermediate.
      Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
      SHA256.HashData(bytes, hash);
      return Convert.ToHexString(hash[..8]);
   }

   /// <summary>
   ///    Knowledge bases can be megabytes, so the UTF-8 buffer is pooled rather than
   ///    materialized on the heap — the same approach <c>CompressionCache.ComputeKey</c>
   ///    uses. Above <see cref="MaxPooledKnowledgeChars" /> a rent would exceed the
   ///    largest shared bucket and allocate anyway, so that case keeps the direct path.
   /// </summary>
   private const int MaxPooledKnowledgeChars = 1 << 20;

   private static string ComputeKnowledgeHash(string knowledge)
   {
      if (knowledge.Length > MaxPooledKnowledgeChars)
         return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(knowledge)));

      // GetByteCount is exact, so the rented buffer is sized to the payload rather than
      // to the worst-case 3x expansion. It costs a counting pass and no allocation.
      var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(knowledge));
      try
      {
         var byteCount = Encoding.UTF8.GetBytes(knowledge.AsSpan(), buffer);
         return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, byteCount)));
      }
      finally
      {
         ArrayPool<byte>.Shared.Return(buffer);
      }
   }
}
