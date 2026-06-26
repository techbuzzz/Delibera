using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Delibera.Core.Tests.Fakes;

/// <summary>
///    In-memory <see cref="IChatClient" /> used to test the Microsoft.Extensions.AI integration
///    without any network access.
/// </summary>
public sealed class FakeChatClient(string reply = "fake-reply", string providerName = "Fake", string? defaultModel = "fake-model", int delayMs = 0)
   : IChatClient
{
   private readonly ChatClientMetadata _metadata = new(providerName, defaultModelId: defaultModel);

   public bool Disposed { get; private set; }

   /// <summary>Records the options of the most recent request for assertions.</summary>
   public ChatOptions? LastOptions { get; private set; }

   /// <summary>Records the messages of the most recent request for assertions.</summary>
   public IList<ChatMessage>? LastMessages { get; private set; }

   public async Task<ChatResponse> GetResponseAsync(
      IEnumerable<ChatMessage> messages,
      ChatOptions? options = null,
      CancellationToken cancellationToken = default)
   {
      LastMessages = messages.ToList();
      LastOptions = options;
      if (delayMs > 0)
         await Task.Delay(delayMs, cancellationToken);
      return new ChatResponse(new ChatMessage(ChatRole.Assistant, reply));
   }

   public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
      IEnumerable<ChatMessage> messages,
      ChatOptions? options = null,
      [EnumeratorCancellation] CancellationToken cancellationToken = default)
   {
      LastMessages = messages.ToList();
      LastOptions = options;
      if (delayMs > 0)
         await Task.Delay(delayMs, cancellationToken);
      // Emit one update per word to simulate token streaming.
      foreach (var word in reply.Split(' '))
      {
         await Task.Yield();
         yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
      }
   }

   public object? GetService(Type serviceType, object? serviceKey = null)
   {
      ArgumentNullException.ThrowIfNull(serviceType);
      return serviceKey is null && serviceType.IsInstanceOfType(_metadata) ? _metadata : null;
   }

   public void Dispose() => Disposed = true;
}
