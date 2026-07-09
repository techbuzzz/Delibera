using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Delibera.Core.Models;

namespace Delibera.Core.Caching;

/// <summary>
///    Generates deterministic cache keys for debate results.
///    The key is derived from the debate-defining inputs: question, members,
///    strategy, knowledge, and configuration — everything that affects the
///    debate output. Two debates with identical inputs produce the same cache key.
/// </summary>
public static class DebateCacheKeyGenerator
{
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
    /// <param name="members">Display names of council members (ordered deterministically).</param>
    /// <param name="strategyName">Strategy name (e.g. "Standard", "Critique").</param>
    /// <param name="maxRounds">Maximum number of debate rounds.</param>
    /// <param name="temperature">LLM temperature.</param>
    /// <param name="systemPrompt">System prompt (if overridden).</param>
    public static string Generate(
        PromptContext context,
        IReadOnlyList<string> members,
        string strategyName,
        int maxRounds,
        float temperature,
        string? systemPrompt = null)
    {
        var content = JsonSerializer.Serialize(new
        {
            Question = context.UserPrompt,
            Members = members.Order(StringComparer.Ordinal),
            Strategy = strategyName,
            MaxRounds = maxRounds,
            Temperature = temperature,
            SystemPrompt = systemPrompt,
            KnowledgeHash = context.KnowledgeContent is null
                ? null
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context.KnowledgeContent))),
        }, _json);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash)[..16];
    }
}