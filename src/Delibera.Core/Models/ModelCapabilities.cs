namespace Delibera.Core.Models;

/// <summary>
///    Capabilities of a council member, used by F-06 Multi-Modal attachment routing.
///    Auto-detected from the model name via <see cref="ModelContextWindowRegistry"/>
///    or set explicitly via the <c>AddMember</c> overload that accepts
///    <see cref="MemberCapabilities"/>.
/// </summary>
[Flags]
public enum MemberCapabilities
{
   /// <summary>The member can process text prompts. Always set for all LLMs.</summary>
   Text = 1,

   /// <summary>The member can process image / vision inputs (e.g. llava, gpt-4o).</summary>
   Vision = 2
}

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
///    <para>
///       Providers that cannot introspect model metadata at all return
///       <see cref="Unknown" />, which callers check with
///       <see cref="IsUnknown" />.
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
   ///    <c>true</c> when this instance was created via <see cref="Unknown" />,
   ///    meaning the provider could not determine any capabilities for this model.
   ///    Callers should fall back to <see cref="ModelContextWindowRegistry" /> or
   ///    conservative defaults.
   /// </summary>
   public bool IsUnknown => ContextWindowTokens is null && MaxOutputTokens is null && !SupportsVision && !SupportsTools && Family is null;

   /// <summary>
   ///    Creates a placeholder instance for a model whose capabilities are unknown.
   ///    All optional fields are left at their default (<c>null</c> / <c>false</c>).
   ///    Callers can check for this via <see cref="IsUnknown" />.
   /// </summary>
   public static ModelCapabilities Unknown(string modelName)
   {
      return new ModelCapabilities { ModelName = modelName };
   }
}
