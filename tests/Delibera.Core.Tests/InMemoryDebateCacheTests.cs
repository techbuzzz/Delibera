using Delibera.Core.Caching;
using Delibera.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class InMemoryDebateCacheTests
{
    private static readonly DebateResult _sampleResult = new()
    {
        StrategyName = "Standard",
        Context = new PromptContext(),
        Participants = ["Alice"],
        FinalVerdict = "Yes",
    };

    private InMemoryDebateCache CreateCache(TimeSpan? ttl = null) =>
        new(new MemoryCache(new MemoryCacheOptions()), ttl, NullLogger<InMemoryDebateCache>.Instance);

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        var cache = CreateCache();
        var result = await cache.GetAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_Then_GetAsync_ReturnsResult()
    {
        var cache = CreateCache();
        await cache.SetAsync("key1", _sampleResult);
        var result = await cache.GetAsync("key1");
        result.Should().NotBeNull();
        result!.FinalVerdict.Should().Be("Yes");
    }

    [Fact]
    public async Task InvalidateAsync_RemovesKey()
    {
        var cache = CreateCache();
        await cache.SetAsync("key1", _sampleResult);
        await cache.InvalidateAsync("key1");
        var result = await cache.GetAsync("key1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenKeyPresent()
    {
        var cache = CreateCache();
        await cache.SetAsync("key1", _sampleResult);
        var exists = await cache.ExistsAsync("key1");
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenKeyMissing()
    {
        var cache = CreateCache();
        var exists = await cache.ExistsAsync("nonexistent");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task SetAsync_Overwrites_ExistingKey()
    {
        var cache = CreateCache();
        await cache.SetAsync("key1", _sampleResult);
        var updatedResult = _sampleResult with { FinalVerdict = "No" };
        await cache.SetAsync("key1", updatedResult);

        var result = await cache.GetAsync("key1");
        result!.FinalVerdict.Should().Be("No");
    }

    [Fact]
    public async Task InvalidateAsync_NonExistentKey_DoesNotThrow()
    {
        var cache = CreateCache();
        var act = async () => await cache.InvalidateAsync("nonexistent");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetAsync_WithCustomTtl_StoresResult()
    {
        var cache = CreateCache(TimeSpan.FromMinutes(30));
        await cache.SetAsync("key1", _sampleResult, TimeSpan.FromHours(1));
        var result = await cache.GetAsync("key1");
        result.Should().NotBeNull();
    }
}