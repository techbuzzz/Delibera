using Delibera.Core.Models;
using FluentAssertions;

namespace Delibera.Core.Tests;

/// <summary>
///    The lookup contract of <see cref="ModelContextWindowRegistry" />.
///    <para>
///    Model names arrive tagged ("llama3.2:7b", "gpt-4o-mini"), so matching is a
///    case-insensitive <em>substring</em> search — which is why the registry indexes a
///    pattern-ordered array rather than relying on dictionary key lookup alone. These tests
///    pin the three properties that contract depends on: case-insensitivity, the
///    most-specific-pattern-wins rule, and the exact-match fast path agreeing with the scan.
///    </para>
///    <para>
///    These read built-in data only and never call <c>Register</c> /
///    <c>RegisterVisionPattern</c>, because those mutate process-global state for the rest
///    of the test run.
///    </para>
/// </summary>
public class ModelContextWindowRegistryTests
{
   // ── Exact-match fast path ──

   [Fact]
   public void GetContextWindow_Returns_Window_For_Exact_Pattern()
   {
      ModelContextWindowRegistry.GetContextWindow("qwen").Should().Be(8_192);
      ModelContextWindowRegistry.GetContextWindow("gpt-4o").Should().Be(131_072);
      ModelContextWindowRegistry.GetContextWindow("o1").Should().Be(200_000);
   }

   // ── Most-specific pattern wins ──
   // "llama3.2:7b" contains both "llama3.2" (131072) and "llama3" (8192). The registry
   // scans longest-pattern-first, so the specific entry must win.

   [Theory]
   [InlineData("llama3.2:7b", 131_072)]
   [InlineData("llama3.2", 131_072)]
   [InlineData("gpt-4o-mini", 131_072)]
   [InlineData("qwen2.5:32b", 32_768)]
   [InlineData("phi3.5-mini-instruct", 131_072)]
   public void GetContextWindow_Prefers_The_Longest_Matching_Pattern(string modelName, int expected)
   {
      ModelContextWindowRegistry.GetContextWindow(modelName).Should().Be(expected,
         "a tagged name must match its most specific registered pattern, not a shorter prefix");
   }

   // ── Case-insensitivity on both phases ──

   [Theory]
   [InlineData("LLAMA3.2:7B")]
   [InlineData("Llama3.2:7b")]
   [InlineData("llama3.2:7B")]
   public void GetContextWindow_Is_Case_Insensitive_On_The_Substring_Scan(string modelName)
   {
      ModelContextWindowRegistry.GetContextWindow(modelName).Should().Be(131_072);
   }

   [Theory]
   [InlineData("QWEN")]
   [InlineData("QwEn")]
   public void GetContextWindow_Is_Case_Insensitive_On_The_Exact_Fast_Path(string modelName)
   {
      ModelContextWindowRegistry.GetContextWindow(modelName).Should().Be(8_192,
         "the frozen fast path must use the same comparer as the scan, not the ordinal default");
   }

   // ── Negative cases ──

   [Fact]
   public void GetContextWindow_Returns_Null_For_Unknown_Model()
   {
      ModelContextWindowRegistry.GetContextWindow("totally-unknown-model").Should().BeNull();
   }

   [Fact]
   public void GetContextWindow_Rejects_Null_Or_Whitespace()
   {
      var actNull = () => ModelContextWindowRegistry.GetContextWindow(null!);
      var actEmpty = () => ModelContextWindowRegistry.GetContextWindow("   ");
      actNull.Should().Throw<ArgumentException>();
      actEmpty.Should().Throw<ArgumentException>();
   }

   // ── Vision patterns ──

   [Theory]
   [InlineData("llava:13b")]
   [InlineData("llava13b-instruct")]
   [InlineData("gpt-4o")]
   [InlineData("LLava:13b")]
   public void SupportsVision_Is_True_For_Substring_Matches(string modelName)
   {
      ModelContextWindowRegistry.SupportsVision(modelName).Should().BeTrue();
   }

   [Theory]
   [InlineData("mixtral-8x7b")]
   [InlineData("llama3.2")]     // only "llama3.2-vision" is registered, not "llama3.2"
   [InlineData("gpt-4")]        // only "gpt-4o" / "gpt-4-vision" are registered
   public void SupportsVision_Is_False_When_No_Pattern_Matches(string modelName)
   {
      ModelContextWindowRegistry.SupportsVision(modelName).Should().BeFalse();
   }

   // ── Freeze() is an eager warm-up, not a behaviour switch ──

   [Fact]
   public void Freeze_Preserves_Lookup_Results()
   {
      ModelContextWindowRegistry.Freeze();

      ModelContextWindowRegistry.GetContextWindow("llama3.2:7b").Should().Be(131_072);
      ModelContextWindowRegistry.GetContextWindow("qwen").Should().Be(8_192);
      ModelContextWindowRegistry.SupportsVision("llava:13b").Should().BeTrue();
   }

   // ── Public snapshots ──

   [Fact]
   public void GetAll_Exposes_Every_Registered_Pattern()
   {
      var all = ModelContextWindowRegistry.GetAll();

      all.Should().ContainKey("llama3.2").WhoseValue.Should().Be(131_072);
      all.Should().ContainKey("gpt-4o").WhoseValue.Should().Be(131_072);
      all.Count.Should().BeGreaterThan(40);
   }

   [Fact]
   public void GetVisionPatterns_Exposes_Every_Registered_Pattern()
   {
      ModelContextWindowRegistry.GetVisionPatterns().Should().Contain("llava");
   }

   // ── Capabilities snapshot combines both lookups ──

   [Fact]
   public void GetCapabilities_Combines_Window_And_Vision_Lookups()
   {
      var capabilities = ModelContextWindowRegistry.GetCapabilities("llava13b-instruct");

      capabilities.ModelName.Should().Be("llava13b-instruct");
      capabilities.SupportsVision.Should().BeTrue();
   }

   [Fact]
   public void GetCapabilities_Reports_Unknown_Window_For_Unregistered_Model()
   {
      var capabilities = ModelContextWindowRegistry.GetCapabilities("some-private-finetune:v3");

      capabilities.IsUnknown.Should().BeTrue();
      capabilities.ContextWindowTokens.Should().BeNull();
   }
}