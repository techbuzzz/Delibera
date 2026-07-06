using Delibera.Core.DependencyInjection;
using Delibera.Core.Models;

namespace Delibera.Core.Persistence;

/// <summary>
///    A serialisable snapshot of a debate at a point in time, allowing it to be
///    resumed from that point after a crash or intentional pause. Captured
///    automatically by <see cref="Council.CouncilExecutor"/> after each round when
///    a <see cref="IDebateStore"/> is configured.
/// </summary>
/// <param name="DebateId">Unique debate identifier. Auto-generated if null/empty on save.</param>
/// <param name="CreatedAt">UTC timestamp when the checkpoint was created.</param>
/// <param name="LastCompletedRound">The highest round number that has completed.</param>
/// <param name="CompletedRounds">All rounds completed so far, in order.</param>
/// <param name="Options">
///    The <see cref="CouncilOptions"/> snapshot used for the debate, so the resumed
///    executor can reapply the same configuration (max rounds, temperature, …).
/// </param>
/// <param name="OriginalQuestion">The user prompt that started the debate.</param>
public sealed record DebateCheckpoint(
    string DebateId,
    DateTimeOffset CreatedAt,
    int LastCompletedRound,
    IReadOnlyList<DebateRound> CompletedRounds,
    CouncilOptions Options,
    string OriginalQuestion)
{
    /// <summary>
    ///    Creates an empty checkpoint for a fresh debate, auto-generating a
    ///    ULID-style identifier and stamping <see cref="CreatedAt"/> to UTC now.
    /// </summary>
    /// <param name="options">The council options snapshot.</param>
    /// <param name="originalQuestion">The debate's user prompt.</param>
    /// <returns>A fresh, empty checkpoint.</returns>
    public static DebateCheckpoint CreateEmpty(CouncilOptions options, string originalQuestion)
    {
        return new DebateCheckpoint(
            DebateId: GenerateId(),
            CreatedAt: DateTimeOffset.UtcNow,
            LastCompletedRound: 0,
            CompletedRounds: [],
            Options: options,
            OriginalQuestion: originalQuestion);
    }

    /// <summary>
    ///    Generates a new ULID-style identifier (26 chars, lexicographically sortable
    ///    by creation time, no collisions across processes). Format:
    ///    <c>{timestamp-base32}{random-base32}</c> (10 + 16 chars).
    /// </summary>
    public static string GenerateId()
    {
        var now = DateTimeOffset.UtcNow;
        var ms = now.ToUnixTimeMilliseconds();
        // Encode the timestamp (48 bits) as 10 base32 chars (Crockford alphabet).
        Span<char> ts = stackalloc char[10];
        for (var i = 9; i >= 0; i--)
        {
            ts[i] = Base32Alphabet[(int)(ms & 0x1F)];
            ms >>= 5;
        }
        // 16 random chars from the same alphabet.
        Span<char> rand = stackalloc char[16];
        var rng = System.Security.Cryptography.RandomNumberGenerator.Fill;
        Span<byte> bytes = stackalloc byte[10];
        rng(bytes);
        for (var i = 0; i < 16; i++)
            rand[i] = Base32Alphabet[bytes[i % bytes.Length] & 0x1F];
        return string.Concat(ts, rand);
    }

    private static readonly char[] Base32Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ".ToCharArray();
}

/// <summary>
///    Lightweight metadata for a checkpoint, used by <see cref="IDebateStore.ListAsync"/>
///    to enumerate stored checkpoints without loading the full round data.
/// </summary>
/// <param name="DebateId">Unique debate identifier.</param>
/// <param name="CreatedAt">UTC timestamp when the checkpoint was created.</param>
/// <param name="LastCompletedRound">The highest round number that has completed.</param>
/// <param name="OriginalQuestion">The debate's user prompt (truncated for display).</param>
public sealed record DebateCheckpointMeta(
    string DebateId,
    DateTimeOffset CreatedAt,
    int LastCompletedRound,
    string OriginalQuestion);