namespace Delibera.Core.Models;

/// <summary>
///    Describes the capabilities of an LLM model — context window size,
///    maximum output tokens, and supported modalities.
/// </summary>
/// <remarks>
///    <para>
///       Obtained from the provider via <see cref="Interfaces.ILLMProvider.GetModelCapabilitiesAsync" />
///       or inferred from the built-in <see cref="ModelContextWindowRegistry" />.
///    </para>
///    <para>
///       When a provider cannot determine the context window, <see cref="ContextWindowTokens" />
///       remains <c>null</c> and the AutoChunking orchestrator falls back to the registry or
///       a conservative default.
///    </para>
/// </remarks>
public sealed record ModelCapabilities
{
   /// <summary>Model identifier as recognised by the provider (e.g. "llama3.2", "gpt-4o-mini").</summary>
   public required string ModelName { get; init; }

   /// <summary>
   ///    Maximum context window size in tokens. <c>null</c> when unknown.
   ///    This is the total number of tokens the model can process in a single request
   ///    (system prompt + user prompt + response).
   /// </summary>
   public int? ContextWindowTokens { get; init; }

   /// <summary>
   ///    Maximum number of tokens the model can generate in a single response.
   ///    <c>null</c> when unknown.
   /// </summary>
   public int? MaxOutputTokens { get; init; }

   /// <summary>Whether the model supports image / vision inputs.</summary>
   public bool SupportsVision { get; init; }

   /// <summary>Whether the model supports tool / function calling.</summary>
   public bool SupportsTools { get; init; }

   /// <summary>
   ///    Model family name for grouping and heuristics (e.g. "llama", "qwen", "phi").
   ///    May be <c>null</c>.
   /// </summary>
   public string? Family { get; init; }

   /// <summary>
   ///    Creates a placeholder instance for a model whose capabilities are unknown.
   ///    All optional fields are left at their default (<c>null</c> / <c>false</c>).
   /// </summary>
   public static ModelCapabilities Unknown(string modelName) => new() { ModelName = modelName };
}
