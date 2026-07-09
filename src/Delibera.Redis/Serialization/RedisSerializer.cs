using System.Text.Json;
using System.Text.Json.Serialization;
using Delibera.Core.Models;

namespace Delibera.Redis.Serialization;

/// <summary>
///    JSON-serializable representation of <see cref="DebateRound" /> for Redis transport.
///    Excludes non-serializable fields like <see cref="DebateRound.StrategyUsed" />.
/// </summary>
public sealed record RedisRoundEvent
{
    public required string  DebateId    { get; init; }
    public required string  EventType   { get; init; }
    public int              RoundNumber { get; init; }
    public string?          RoundName   { get; init; }
    public string?          Description { get; init; }
    public Dictionary<string, string>? Responses { get; init; }
    public string?          RoundPrompt { get; init; }
    public DateTime        StartedAt   { get; init; }
    public DateTime?       CompletedAt { get; init; }
}

/// <summary>
///    JSON-serializable representation of a completed debate result for Redis transport.
/// </summary>
public sealed record RedisDebateResult
{
    public required string                                DebateId        { get; init; }
    public required string                                StrategyName    { get; init; }
    public string?                                        FinalVerdict    { get; init; }
    public string?                                        ChairmanName    { get; init; }
    public string?                                        OpeningStatement { get; init; }
    public DateTime                                      StartedAt       { get; init; }
    public DateTime?                                     CompletedAt     { get; init; }
    public List<RedisRoundEvent>?                        Rounds          { get; init; }
    public Dictionary<string, double>?                  VotingTally     { get; init; }
    public string?                                       VotingMethod    { get; init; }
    public string?                                       WinningOption   { get; init; }
    public double?                                       WinningScore    { get; init; }
    public long?                                         TotalTokens     { get; init; }
    public long?                                         TokensSaved     { get; init; }
    public string?                                       ErrorMessage   { get; init; }
}

internal static class RedisSerializer
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        Converters                 = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, _json);
    public static T?   Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, _json);

    public static RedisRoundEvent ToRedisEvent(this DebateRound round, string debateId) => new()
    {
        DebateId    = debateId,
        EventType   = "round-completed",
        RoundNumber = round.RoundNumber,
        RoundName   = round.RoundName,
        Description = round.Description,
        Responses   = new Dictionary<string, string>(round.Responses),
        RoundPrompt = round.RoundPrompt,
        StartedAt   = round.StartedAt,
        CompletedAt = round.CompletedAt,
    };

    public static RedisDebateResult ToRedisResult(this DebateResult result) => new()
    {
        DebateId         = result.DebateId,
        StrategyName     = result.StrategyName,
        FinalVerdict     = result.FinalVerdict,
        ChairmanName     = result.ChairmanName,
        OpeningStatement = result.OpeningStatement,
        StartedAt        = result.StartedAt,
        CompletedAt      = result.CompletedAt,
        Rounds           = result.Rounds.Select(r => r.ToRedisEvent(result.DebateId)).ToList(),
        VotingTally      = result.VotingTally?.Scores.ToDictionary(kv => kv.Key, kv => kv.Value),
        VotingMethod     = result.VotingTally?.Method,
        WinningOption    = result.VotingTally?.WinningOption,
        WinningScore     = result.VotingTally?.Score,
        TotalTokens      = result.TokenStats?.GrandTotal,
        TokensSaved      = result.TokenStats?.TokensSaved,
    };
}