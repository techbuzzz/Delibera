namespace Delibera.Core.Models;

/// <summary>
///    Static registry of known context-window sizes for popular LLM models.
///    Used as a fallback when a provider cannot report capabilities dynamically.
/// </summary>
/// <remarks>
///    <para>
///       The registry is consulted by <see cref="Chunking.AutoChunkingOrchestrator" />
///       when <see cref="Interfaces.ILLMProvider.GetModelCapabilitiesAsync" /> returns
///       <c>null</c> or a <see cref="ModelCapabilities" /> with an unknown context window.
///    </para>
///    <para>
///       Call <see cref="Register" /> to add custom models at startup. The lookup is
///       case-insensitive and uses substring matching — "llama3.2:latest" matches the
///       "llama3.2" entry.
///    </para>
/// </remarks>
public static class ModelContextWindowRegistry
{
   private static readonly Dictionary<string, int> KnownWindows = new(StringComparer.OrdinalIgnoreCase)
   {
      // ── Llama family ──
      ["llama3.2"] = 131_072,
      ["llama3.1"] = 131_072,
      ["llama3"] = 8_192,
      ["llama2"] = 4_096,

      // ── Qwen family ──
      ["qwen2.5"] = 32_768,
      ["qwen2"] = 32_768,
      ["qwen"] = 8_192,

      // ── DeepSeek family ──
      ["deepseek-r1"] = 131_072,
      ["deepseek-v3"] = 131_072,
      ["deepseek-coder"] = 16_384,

      // ── Phi family ──
      ["phi4"] = 16_384,
      ["phi3.5"] = 131_072,
      ["phi3"] = 4_096,
      ["phi-3"] = 4_096,

      // ── Mistral / Mixtral ──
      ["mistral"] = 32_768,
      ["mixtral"] = 32_768,
      ["codestral"] = 32_768,
      ["ministral"] = 131_072,

      // ── Gemma ──
      ["gemma3"] = 32_768,
      ["gemma2"] = 8_192,
      ["gemma"] = 8_192,

      // ── Command R ──
      ["command-r"] = 131_072,
      ["command-r-plus"] = 131_072,

      // ── OpenAI ──
      ["gpt-4o"] = 131_072,
      ["gpt-4o-mini"] = 131_072,
      ["gpt-4-turbo"] = 131_072,
      ["gpt-4"] = 8_192,
      ["gpt-3.5-turbo"] = 16_384,
      ["o1"] = 200_000,
      ["o1-mini"] = 131_072,
      ["o3-mini"] = 200_000,

      // ── Anthropic ──
      ["claude-3.5"] = 200_000,
      ["claude-3"] = 200_000,
      ["claude"] = 200_000,

      // ── YandexGPT ──
      ["yandexgpt-5"] = 32_768,
      ["yandexgpt-32k"] = 32_768,
      ["yandexgpt"] = 8_000,

      // ── Other ──
      ["nomic"] = 8_192,
      ["mxbai"] = 32_768,
      ["tinyllama"] = 2_048,
      ["stable-code"] = 16_384,
   };

   /// <summary>
   ///    Looks up the context window size for a model by name.
   ///    Uses case-insensitive substring matching — "llama3.2:7b" matches "llama3.2".
   /// </summary>
   /// <param name="modelName">Model name as reported by the provider.</param>
   /// <returns>Context window in tokens, or <c>null</c> if unknown.</returns>
   public static int? GetContextWindow(string modelName)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

      foreach (var (pattern, window) in KnownWindows)
         if (modelName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            return window;

      return null;
   }

   /// <summary>
   ///    Registers a custom model pattern with its context window size.
   ///    Overwrites any existing entry for the same pattern.
   /// </summary>
   /// <param name="modelNamePattern">
   ///    Substring pattern to match against model names (case-insensitive).
   ///    E.g. "my-fine-tuned-llama" will match "my-fine-tuned-llama:v2".
   /// </param>
   /// <param name="contextWindowTokens">Context window size in tokens.</param>
   public static void Register(string modelNamePattern, int contextWindowTokens)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(modelNamePattern);
      ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contextWindowTokens);

      KnownWindows[modelNamePattern] = contextWindowTokens;
   }

   /// <summary>
   ///    Returns a read-only snapshot of all registered model patterns and their context windows.
   /// </summary>
   public static IReadOnlyDictionary<string, int> GetAll() => KnownWindows.AsReadOnly();
}
