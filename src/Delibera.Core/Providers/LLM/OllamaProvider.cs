using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace Delibera.Core.Providers.LLM;

/// <summary>
///    Ollama LLM provider backed by OllamaSharp.
///    Works with both Ollama Cloud (API key) and a local Ollama server.
/// </summary>
public sealed class OllamaProvider : ILLMProvider
{
   private bool _disposed;

   /// <summary>
   ///    Creates an Ollama provider.
   /// </summary>
   /// <param name="endpoint">Ollama endpoint URL (e.g., "https://api.ollama.com" or "http://localhost:11434").</param>
   /// <param name="apiKey">API key for Ollama Cloud (empty for local server).</param>
   public OllamaProvider(string endpoint, string apiKey = "")
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

      var uri = new Uri(endpoint.TrimEnd('/'));

      if (!string.IsNullOrWhiteSpace(apiKey))
      {
         var httpClient = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromMinutes(5) };
         httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
         Client = new OllamaApiClient(httpClient);
      }
      else
      {
         Client = new OllamaApiClient(uri);
      }
   }

   /// <summary>Provides access to the underlying OllamaSharp client (used by <see cref="OllamaEmbeddingProvider" />).</summary>
   internal OllamaApiClient Client { get; }

   /// <inheritdoc />
   public string ProviderName => "Ollama";

   /// <inheritdoc />
   public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
   {
      try
      {
         await Client.ListLocalModelsAsync(ct);
         return true;
      }
      catch
      {
         return false;
      }
   }

   /// <inheritdoc />
   public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
   {
      try
      {
         var models = await Client.ListLocalModelsAsync(ct);
         return models.Select(m => m.Name).ToList().AsReadOnly();
      }
      catch (Exception ex)
      {
         throw new InvalidOperationException($"Failed to list Ollama models: {ex.Message}", ex);
      }
   }

   /// <inheritdoc />
   public async Task<string> ChatAsync(
      string model,
      string systemPrompt,
      string userPrompt,
      float temperature = 0.7f,
      CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(model);
      ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

      var messages = new List<Message>();
      if (!string.IsNullOrWhiteSpace(systemPrompt))
         messages.Add(new Message(ChatRole.System, systemPrompt));
      messages.Add(new Message(ChatRole.User, userPrompt));

      var request = new ChatRequest
      {
         Model = model,
         Messages = messages,
         Options = new RequestOptions { Temperature = temperature },
         Stream = false
      };

      try
      {
         var sb = new StringBuilder();
         await foreach (var chunk in Client.ChatAsync(request, ct))
            if (chunk?.Message?.Content is not null)
               sb.Append(chunk.Message.Content);

         var response = sb.ToString().Trim();
         if (string.IsNullOrWhiteSpace(response))
            throw new InvalidOperationException($"Empty response from model '{model}'.");
         return response;
      }
      catch (HttpRequestException ex)
      {
         throw new InvalidOperationException($"HTTP error talking to Ollama (model: {model}): {ex.Message}", ex);
      }
      catch (TaskCanceledException) when (ct.IsCancellationRequested)
      {
         throw;
      }
      catch (TaskCanceledException ex)
      {
         throw new TimeoutException($"Request to Ollama model '{model}' timed out.", ex);
      }
   }

   /// <inheritdoc />
   public void Dispose()
   {
      if (_disposed) return;
      _disposed = true;
      GC.SuppressFinalize(this);
   }
}