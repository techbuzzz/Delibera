using Delibera.Core.Interfaces;
using Microsoft.Extensions.AI;

namespace Delibera.Core.Providers.LLM;

/// <summary>
///    A universal <see cref="ILLMProvider" /> implemented on top of the Microsoft.Extensions.AI
///    <see cref="IChatClient" /> abstraction.
/// </summary>
/// <remarks>
///    <para>
///       This provider lets Delibera talk to <em>any</em> chat backend that ships an
///       <see cref="IChatClient" /> implementation — Ollama (via OllamaSharp), OpenAI / Azure OpenAI
///       (via <c>Microsoft.Extensions.AI.OpenAI</c>), Anthropic, local OpenAI-compatible servers
///       (LM Studio, LocalAI, vLLM), and so on — without writing a bespoke provider for each one.
///    </para>
///    <para>
///       Because it wraps an <see cref="IChatClient" />, it also benefits from the
///       Microsoft.Extensions.AI middleware pipeline (function invocation, caching, telemetry,
///       logging). Build a decorated client with <see cref="Extensions.MicrosoftAIExtensions" /> helpers and
///       hand it to this provider.
///    </para>
/// </remarks>
public sealed class ChatClientLLMProvider : ILLMProvider
{
   private readonly IChatClient _chatClient;
   private readonly bool _ownsClient;
   private bool _disposed;

   /// <summary>
   ///    Wraps an existing <see cref="IChatClient" /> as an <see cref="ILLMProvider" />.
   /// </summary>
   /// <param name="chatClient">The Microsoft.Extensions.AI chat client to wrap.</param>
   /// <param name="providerName">
   ///    Friendly provider name. When <c>null</c> the name is taken from the client metadata
   ///    (<see cref="ChatClientMetadata.ProviderName" />), falling back to "ChatClient".
   /// </param>
   /// <param name="ownsClient">
   ///    When <c>true</c> (default) the wrapped client is disposed together with this provider.
   ///    Set to <c>false</c> when the client lifetime is owned elsewhere (e.g., a DI container).
   /// </param>
   public ChatClientLLMProvider(IChatClient chatClient, string? providerName = null, bool ownsClient = true)
   {
      _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
      _ownsClient = ownsClient;

      var metadata = chatClient.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
      ProviderName = providerName
                     ?? (string.IsNullOrWhiteSpace(metadata?.ProviderName) ? "ChatClient" : metadata!.ProviderName!);
      DefaultModelId = metadata?.DefaultModelId;
   }

   /// <summary>The default model id reported by the underlying client metadata (may be <c>null</c>).</summary>
   public string? DefaultModelId { get; }

   /// <summary>Exposes the wrapped <see cref="IChatClient" /> for advanced scenarios and middleware composition.</summary>
   public IChatClient ChatClient => _chatClient;

   /// <inheritdoc />
   public string ProviderName { get; }

   /// <inheritdoc />
   public Task<bool> IsAvailableAsync(CancellationToken ct = default)
   {
      // A generic IChatClient has no universal health-check. We treat a live client as available;
      // concrete transports surface failures on the first ChatAsync call.
      return Task.FromResult(!_disposed);
   }

   /// <inheritdoc />
   /// <remarks>
   ///    The Microsoft.Extensions.AI abstraction does not define a model-enumeration contract,
   ///    so this returns the default model id when known, otherwise an empty list.
   /// </remarks>
   public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
   {
      IReadOnlyList<string> models = string.IsNullOrWhiteSpace(DefaultModelId)
         ? []
         : [DefaultModelId!];
      return Task.FromResult(models);
   }

   /// <inheritdoc />
   public async Task<string> ChatAsync(
      string model,
      string systemPrompt,
      string userPrompt,
      float temperature = 0.7f,
      CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

      var messages = BuildMessages(systemPrompt, userPrompt);
      var options = BuildOptions(model, temperature);

      try
      {
         var response = await _chatClient.GetResponseAsync(messages, options, ct);
         var text = response.Text?.Trim() ?? string.Empty;
         if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Empty response from model '{model}' ({ProviderName}).");
         return text;
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
         throw;
      }
      catch (Exception ex) when (ex is not InvalidOperationException)
      {
         throw new InvalidOperationException(
            $"Error talking to {ProviderName} (model: {model}): {ex.Message}", ex);
      }
   }

   /// <inheritdoc />
   public async IAsyncEnumerable<string> ChatStreamAsync(
      string model,
      string systemPrompt,
      string userPrompt,
      float temperature = 0.7f,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

      var messages = BuildMessages(systemPrompt, userPrompt);
      var options = BuildOptions(model, temperature);

      await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, ct))
      {
         var text = update.Text;
         if (!string.IsNullOrEmpty(text))
            yield return text;
      }
   }

   private static List<ChatMessage> BuildMessages(string systemPrompt, string userPrompt)
   {
      var messages = new List<ChatMessage>(2);
      if (!string.IsNullOrWhiteSpace(systemPrompt))
         messages.Add(new ChatMessage(ChatRole.System, systemPrompt));
      messages.Add(new ChatMessage(ChatRole.User, userPrompt));
      return messages;
   }

   private ChatOptions BuildOptions(string model, float temperature) => new()
   {
      // Prefer the explicitly requested model; fall back to the client's default.
      ModelId = string.IsNullOrWhiteSpace(model) ? DefaultModelId : model,
      Temperature = temperature
   };

   /// <inheritdoc />
   public void Dispose()
   {
      if (_disposed) return;
      _disposed = true;
      if (_ownsClient) _chatClient.Dispose();
      GC.SuppressFinalize(this);
   }
}
