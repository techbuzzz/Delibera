namespace Delibera.Core.Models;

/// <summary>
/// A council participant — an LLM model bound to a specific provider.
/// </summary>
public sealed class CouncilMember
{
   /// <summary>Unique participant identifier.</summary>
   public string Id { get; }

   /// <summary>Display name (e.g., "llama2 (Ollama)").</summary>
   public string DisplayName { get; }

   /// <summary>Model name (e.g., "llama2").</summary>
   public string ModelName { get; }

   /// <summary>LLM provider that serves this model.</summary>
   public ILLMProvider Provider { get; }

   /// <summary>Role in the debate (Expert, Critic, Chairman, etc.).</summary>
   public string Role { get; set; }

   /// <summary>Optional persona system-prompt that personalises the model's behaviour.</summary>
   public string? PersonaPrompt { get; set; }

   /// <summary>
   /// Creates a new council member.
   /// </summary>
   public CouncilMember(string modelName, ILLMProvider provider, string? role = null, string? personaPrompt = null)
   {
      ModelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
      Provider = provider ?? throw new ArgumentNullException(nameof(provider));
      Role = role ?? "Expert";
      PersonaPrompt = personaPrompt;
      DisplayName = $"{modelName} ({provider.ProviderName})";
      Id = $"{provider.ProviderName}:{modelName}:{Guid.NewGuid():N}".ToLowerInvariant();
   }

   /// <summary>Sends a request to the underlying model.</summary>
   public async Task<string> AskAsync(
       string systemPrompt,
       string userPrompt,
       float temperature = 0.7f,
       CancellationToken ct = default)
   {
      var fullSystemPrompt = string.IsNullOrWhiteSpace(PersonaPrompt)
          ? systemPrompt
          : $"{PersonaPrompt}\n\n{systemPrompt}";

      return await Provider.ChatAsync(ModelName, fullSystemPrompt, userPrompt, temperature, ct);
   }

   /// <inheritdoc/>
   public override string ToString() => DisplayName;
}
