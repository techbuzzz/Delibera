using Delibera.Core.Caching;
using Delibera.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Delibera.Core.Tests;

public sealed class FileDebateCacheTests : IDisposable
{
    private readonly string _cacheDir;
    private readonly FileDebateCache _cache;

    private static readonly DebateResult _sampleResult = new()
    {
        StrategyName = "Standard",
        Context = new PromptContext(),
        Participants = ["Alice"],
        FinalVerdict = "Yes",
    };

    public FileDebateCacheTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), $"delibera-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_cacheDir);
        _cache = new FileDebateCache(_cacheDir, TimeSpan.FromHours(1), NullLogger<FileDebateCache>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_cacheDir, true); } catch { }
    }

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        var result = await _cache.GetAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_Then_GetAsync_ReturnsResult()
    {
        await _cache.SetAsync("key1", _sampleResult);
        var result = await _cache.GetAsync("key1");
        result.Should().NotBeNull();
        result!.FinalVerdict.Should().Be("Yes");
        result.StrategyName.Should().Be("Standard");
    }

    [Fact]
    public async Task InvalidateAsync_RemovesKey()
    {
        await _cache.SetAsync("key1", _sampleResult);
        await _cache.InvalidateAsync("key1");
        var result = await _cache.GetAsync("key1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenKeyPresent()
    {
        await _cache.SetAsync("key1", _sampleResult);
        var exists = await _cache.ExistsAsync("key1");
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenKeyMissing()
    {
        var exists = await _cache.ExistsAsync("nonexistent");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task SetAsync_Overwrites_ExistingKey()
    {
        await _cache.SetAsync("key1", _sampleResult);
        var updatedResult = _sampleResult with { FinalVerdict = "No" };
        await _cache.SetAsync("key1", updatedResult);

        var result = await _cache.GetAsync("key1");
        result!.FinalVerdict.Should().Be("No");
    }

    [Fact]
    public async Task InvalidateAsync_NonExistentKey_DoesNotThrow()
    {
        var act = async () => await _cache.InvalidateAsync("nonexistent");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetAsync_CreatesCacheFile()
    {
        await _cache.SetAsync("filekey1", _sampleResult);
        var filePath = Path.Combine(_cacheDir, "filekey1.cache.json");
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_ExpiredFile_ReturnsNull()
    {
        // Create cache with very short TTL
        var shortLivedCache = new FileDebateCache(_cacheDir, TimeSpan.FromMilliseconds(1), NullLogger<FileDebateCache>.Instance);
        await shortLivedCache.SetAsync("expired", _sampleResult);

        // Wait for expiration
        await Task.Delay(50);

        var result = await shortLivedCache.GetAsync("expired");
        result.Should().BeNull();
    }
}