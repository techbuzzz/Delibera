namespace Delibera.Core.Models;

/// <summary>
///    A council participant — an LLM model bound to a specific provider.
/// </summary>
public sealed class CouncilMember(string modelName, ILLMProvider provider, string? role = null, string? personaPrompt = null)
{
   /// <summary>Unique participant identifier.</summary>
   public string Id { get; } = $"{provider.ProviderName}:{modelName}:{Guid.NewGuid():N}".ToLowerInvariant();

   /// <summary>Display name (e.g., "llama2 (Ollama)").</summary>
   public string DisplayName { get; } = $"{modelName} ({provider.ProviderName})";

   /// <summary>Model name (e.g., "llama2").</summary>
   public string ModelName { get; } = modelName ?? throw new ArgumentNullException(nameof(modelName));

   /// <summary>LLM provider that serves this model.</summary>
   public ILLMProvider Provider { get; } = provider ?? throw new ArgumentNullException(nameof(provider));

   private string _role = role ?? "Expert";

   /// <summary>Role in the debate (Expert, Critic, Chairman, etc.).</summary>
   public string Role
   {
      get => field ?? "Expert";
      set => field = value ?? "Expert";
   }

   private string? _personaPrompt = personaPrompt;

   /// <summary>Optional persona system-prompt that personalises the model's behaviour.</summary>
   public string? PersonaPrompt
   {
      get => field;
      set => field = value;
   }

   /// <summary>Sends a request to the underlying model.</summary>
   public Task<string> AskAsync(
      string systemPrompt,
      string userPrompt,
      float temperature = 0.7f,
      CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(systemPrompt);
      ArgumentNullException.ThrowIfNull(userPrompt);

      var fullSystemPrompt = string.IsNullOrWhiteSpace(_personaPrompt)
         ? systemPrompt
         : $"{_personaPrompt}\n\n{systemPrompt}";

      return Provider.ChatAsync(ModelName, fullSystemPrompt, userPrompt, temperature, ct);
   }

   /// <inheritdoc />
   public override string ToString() => DisplayName;
}
