using Delibera.Core.Caching;
using Delibera.Core.Models;
using FluentAssertions;
using Xunit;

namespace Delibera.Core.Tests;

public sealed class DebateCacheKeyGeneratorTests
{
    private static readonly PromptContext _context = new()
    {
        SystemPrompt = "You are a helpful assistant.",
        UserPrompt = "What is the best programming language?",
        KnowledgeContent = "Some knowledge text.",
    };

    private static readonly IReadOnlyList<string> _members = ["Architect", "Devil's Advocate", "Pragmatist"];

    [Fact]
    public void Generate_ReturnsDeterministicKey()
    {
        var key1 = DebateCacheKeyGenerator.Generate(_context, _members, "Standard", 4, 0.7f, "system prompt");
        var key2 = DebateCacheKeyGenerator.Generate(_context, _members, "Standard", 4, 0.7f, "system prompt");
        key1.Should().Be(key2);
    }

    [Fact]
    public void Generate_Returns16CharHexKey()
    {
        var key = DebateCacheKeyGenerator.Generate(_context, _members, "Standard", 4, 0.7f);
        key.Should().HaveLength(16);
        key.Should().MatchRegex("^[0-9A-F]{16}$");
    }

    [Fact]
    public void Generate_DifferentQuestions_ProduceDifferentKeys()
    {
        var ctx1 = _context with { UserPrompt = "Question A" };
        var ctx2 = _context with { UserPrompt = "Question B" };

        var key1 = DebateCacheKeyGenerator.Generate(ctx1, _members, "Standard", 4, 0.7f);
        var key2 = DebateCacheKeyGenerator.Generate(ctx2, _members, "Standard", 4, 0.7f);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Generate_DifferentStrategies_ProduceDifferentKeys()
    {
        var key1 = DebateCacheKeyGenerator.Generate(_context, _members, "Standard", 4, 0.7f);
        var key2 = DebateCacheKeyGenerator.Generate(_context, _members, "Critique", 4, 0.7f);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Generate_DifferentMembers_ProduceDifferentKeys()
    {
        var members2 = new List<string> { "Architect", "Pragmatist" };

        var key1 = DebateCacheKeyGenerator.Generate(_context, _members, "Standard", 4, 0.7f);
        var key2 = DebateCacheKeyGenerator.Generate(_context, members2, "Standard", 4, 0.7f);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Generate_MemberOrderDoesNotAffectKey()
    {
        var ordered = new List<string> { "Alice", "Bob", "Charlie" };
        var reversed = new List<string> { "Charlie", "Bob", "Alice" };

        var key1 = DebateCacheKeyGenerator.Generate(_context, ordered, "Standard", 4, 0.7f);
        var key2 = DebateCacheKeyGenerator.Generate(_context, reversed, "Standard", 4, 0.7f);

        key1.Should().Be(key2);
    }

    [Fact]
    public void Generate_DifferentMaxRounds_ProduceDifferentKeys()
    {
        var key1 = DebateCacheKeyGenerator.Generate(_context, _members, "Standard", 3, 0.7f);
        var key2 = DebateCacheKeyGenerator.Generate(_context, _members, "Standard", 6, 0.7f);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Generate_DifferentKnowledgeContent_ProduceDifferentKeys()
    {
        var ctx1 = _context with { KnowledgeContent = "Knowledge A" };
        var ctx2 = _context with { KnowledgeContent = "Knowledge B" };

        var key1 = DebateCacheKeyGenerator.Generate(ctx1, _members, "Standard", 4, 0.7f);
        var key2 = DebateCacheKeyGenerator.Generate(ctx2, _members, "Standard", 4, 0.7f);

        key1.Should().NotBe(key2);
    }
}