namespace Delibera.Core.Models;

/// <summary>
///    Static registry of known context-window sizes for popular LLM models.
///    Used as a fallback when a provider cannot report capabilities dynamically.
/// </summary>
/// <remarks>
///    <para>
///       The registry is consulted by <see cref="Chunking.AutoChunkingOrchestrator" />
///       when <see cref="Interfaces.ILLMProvider.GetModelCapabilitiesAsync" /> returns
///       a <see cref="ModelCapabilities" /> where <see cref="ModelCapabilities.IsUnknown" />
///       is <c>true</c> or <see cref="ModelCapabilities.ContextWindowTokens" /> is <c>null</c>.
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
       ["stable-code"] = 16_384
    };

   // ── Vision-capable model name patterns (case-insensitive substring match) ──
   // When a model name contains any of these substrings, it is treated as vision-capable.
   private static readonly HashSet<string> KnownVisionPatterns = new(StringComparer.OrdinalIgnoreCase)
   {
      "llava", "gemma3", "llama3.2-vision", "minicpm-v",
      "gpt-4o", "gpt-4-vision", "claude-3", "qwen-vl", "internvl",
      "qwen2-vl", "qwen2.5-vl", "cogvlm", "yi-vl", "deepseek-vl",
      "pixtral", "llama4"
   };

   /// <summary>
   ///    Returns <c>true</c> when the model name matches a known vision-capable pattern.
   ///    Uses case-insensitive substring matching — "llava:13b" matches "llava".
   /// </summary>
   /// <param name="modelName">Model name as reported by the provider.</param>
   /// <returns><c>true</c> if the model supports vision inputs; <c>false</c> otherwise.</returns>
   public static bool SupportsVision(string modelName)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
      foreach (var pattern in KnownVisionPatterns)
         if (modelName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            return true;
      return false;
   }

   /// <summary>
   ///    Returns the <see cref="ModelCapabilities"/> for a model by name — combining
   ///    context-window lookup with vision-capability detection.
   /// </summary>
   /// <param name="modelName">Model name as reported by the provider.</param>
   /// <returns>
   ///    A <see cref="ModelCapabilities"/> snapshot with <see cref="ModelCapabilities.ContextWindowTokens"/>
   ///    from the registry (or <c>null</c> if unknown) and <see cref="ModelCapabilities.SupportsVision"/>
   ///    from <see cref="SupportsVision(string)"/>.
   /// </returns>
   public static ModelCapabilities GetCapabilities(string modelName)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
      return new ModelCapabilities
      {
         ModelName = modelName,
         ContextWindowTokens = GetContextWindow(modelName),
         SupportsVision = SupportsVision(modelName)
      };
   }

   /// <summary>
   ///    Registers a custom vision-capable model name pattern.
   ///    Overwrites any existing entry for the same pattern.
   /// </summary>
   /// <param name="modelNamePattern">Substring pattern to match against model names.</param>
   public static void RegisterVisionPattern(string modelNamePattern)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(modelNamePattern);
      KnownVisionPatterns.Add(modelNamePattern);
   }

   /// <summary>
   ///    Returns a read-only snapshot of all registered vision-capable model patterns.
   /// </summary>
   public static IReadOnlyCollection<string> GetVisionPatterns() => KnownVisionPatterns;

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
   public static IReadOnlyDictionary<string, int> GetAll()
   {
      return KnownWindows.AsReadOnly();
   }
}
