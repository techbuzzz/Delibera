namespace Delibera.Core.Interfaces;

/// <summary>
///    Abstraction for connecting to various LLM services.
///    Implement this interface to add new providers (Ollama, OpenAI, Yandex GPT, etc.)
/// </summary>
public interface ILLMProvider : IDisposable
{
   /// <summary>Unique provider name (e.g., "Ollama", "OpenAI").</summary>
   string ProviderName { get; }

   /// <summary>Checks whether the provider endpoint is reachable.</summary>
   Task<bool> IsAvailableAsync(CancellationToken ct = default);

   /// <summary>Returns a list of models available on this provider.</summary>
   Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);

   /// <summary>
   ///    Sends a chat-completion request to the specified model.
   /// </summary>
   /// <param name="model">Model name (e.g., "llama2", "qwen2.5").</param>
   /// <param name="systemPrompt">System prompt that defines the model's role / context.</param>
   /// <param name="userPrompt">User prompt with the question or task.</param>
   /// <param name="temperature">Generation temperature (0.0 = deterministic, 1.0+ = creative).</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Text response from the model.</returns>
   Task<string> ChatAsync(
      string model,
      string systemPrompt,
      string userPrompt,
      float temperature = 0.7f,
      CancellationToken ct = default);
}