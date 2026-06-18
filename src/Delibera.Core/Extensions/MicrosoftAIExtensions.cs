using System.Runtime.CompilerServices;
using Delibera.Core.Providers.LLM;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Delibera.Core.Extensions;

/// <summary>
///    Bridging helpers between Delibera's provider abstractions (<see cref="ILLMProvider" />,
///    <see cref="IEmbeddingProvider" />) and the Microsoft.Extensions.AI abstractions
///    (<see cref="IChatClient" />, <see cref="IEmbeddingGenerator{TInput,TEmbedding}" />).
/// </summary>
/// <remarks>
///    These extensions let the two worlds interoperate in either direction:
///    <list type="bullet">
///       <item>
///          Adopt any Microsoft.Extensions.AI client as a Delibera provider via <see cref="AsLLMProvider" /> /
///          <see cref="AsEmbeddingProvider" />.
///       </item>
///       <item>
///          Expose a Delibera provider as a Microsoft.Extensions.AI <see cref="IChatClient" /> via
///          <see cref="AsChatClient" />, so it can participate in the standard middleware pipeline.
///       </item>
///       <item>Compose middleware (function invocation, logging) with <see cref="WithMiddleware" />.</item>
///    </list>
/// </remarks>
public static class MicrosoftAIExtensions
{
   /// <summary>
   ///    Adopts a Microsoft.Extensions.AI <see cref="IChatClient" /> as a Delibera <see cref="ILLMProvider" />.
   /// </summary>
   /// <param name="chatClient">The chat client to wrap.</param>
   /// <param name="providerName">Optional friendly provider name (defaults to client metadata).</param>
   /// <param name="ownsClient">Whether disposing the provider also disposes the client.</param>
   public static ILLMProvider AsLLMProvider(this IChatClient chatClient, string? providerName = null, bool ownsClient = true)
   {
      return new ChatClientLLMProvider(chatClient, providerName, ownsClient);
   }

   /// <summary>
   ///    Adopts a Microsoft.Extensions.AI <see cref="IEmbeddingGenerator{TInput,TEmbedding}" /> as a
   ///    Delibera <see cref="IEmbeddingProvider" />.
   /// </summary>
   public static IEmbeddingProvider AsEmbeddingProvider(
      this IEmbeddingGenerator<string, Embedding<float>> generator,
      string? modelName = null,
      int? vectorSize = null,
      bool ownsGenerator = true)
   {
      return new EmbeddingGeneratorProvider(generator, modelName, vectorSize, ownsGenerator);
   }

   /// <summary>
   ///    Exposes a Delibera <see cref="ILLMProvider" /> as a Microsoft.Extensions.AI <see cref="IChatClient" />.
   /// </summary>
   /// <remarks>
   ///    Use this to drop an existing Delibera provider into a Microsoft.Extensions.AI middleware pipeline
   ///    (caching, telemetry, function invocation). If the provider already is a
   ///    <see cref="ChatClientLLMProvider" />, its underlying client is returned directly to avoid a needless layer.
   /// </remarks>
   /// <param name="provider">The Delibera provider to expose.</param>
   /// <param name="defaultModel">Model id used when a request does not specify one.</param>
   public static IChatClient AsChatClient(this ILLMProvider provider, string? defaultModel = null)
   {
      ArgumentNullException.ThrowIfNull(provider);
      if (provider is ChatClientLLMProvider ccp) return ccp.ChatClient;
      return new LLMProviderChatClient(provider, defaultModel);
   }

   /// <summary>
   ///    Composes a standard Microsoft.Extensions.AI middleware pipeline around an <see cref="IChatClient" />.
   /// </summary>
   /// <param name="chatClient">The inner client.</param>
   /// <param name="enableFunctionInvocation">Add automatic function (tool) invocation middleware.</param>
   /// <param name="loggerFactory">When supplied, adds logging middleware.</param>
   /// <returns>The decorated client.</returns>
   public static IChatClient WithMiddleware(
      this IChatClient chatClient,
      bool enableFunctionInvocation = false,
      ILoggerFactory? loggerFactory = null)
   {
      ArgumentNullException.ThrowIfNull(chatClient);

      var builder = chatClient.AsBuilder();
      if (loggerFactory is not null) builder = builder.UseLogging(loggerFactory);
      if (enableFunctionInvocation) builder = builder.UseFunctionInvocation(loggerFactory);
      return builder.Build();
   }

   /// <summary>
   ///    Minimal <see cref="IChatClient" /> adapter over a Delibera <see cref="ILLMProvider" />.
   /// </summary>
   private sealed class LLMProviderChatClient(ILLMProvider provider, string? defaultModel) : IChatClient
   {
      private readonly ChatClientMetadata _metadata = new(provider.ProviderName, defaultModelId: defaultModel);

      public async Task<ChatResponse> GetResponseAsync(
         IEnumerable<ChatMessage> messages,
         ChatOptions? options = null,
         CancellationToken cancellationToken = default)
      {
         var (system, user) = SplitMessages(messages);
         var text = await provider.ChatAsync(
            ResolveModel(options),
            system,
            user,
            options?.Temperature ?? 0.7f,
            cancellationToken);

         return new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { ModelId = ResolveModel(options) };
      }

      public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
         IEnumerable<ChatMessage> messages,
         ChatOptions? options = null,
         [EnumeratorCancellation] CancellationToken cancellationToken = default)
      {
         var (system, user) = SplitMessages(messages);
         await foreach (var chunk in provider.ChatStreamAsync(
                           ResolveModel(options), system, user, options?.Temperature ?? 0.7f, cancellationToken))
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
      }

      public object? GetService(Type serviceType, object? serviceKey = null)
      {
         ArgumentNullException.ThrowIfNull(serviceType);
         if (serviceKey is null && serviceType.IsInstanceOfType(_metadata)) return _metadata;
         if (serviceKey is null && serviceType.IsInstanceOfType(provider)) return provider;
         return null;
      }

      public void Dispose()
      {
         provider.Dispose();
      }

      private string ResolveModel(ChatOptions? options)
      {
         return options?.ModelId is { Length: > 0 } m
            ? m
            : defaultModel ?? string.Empty;
      }

      private static (string System, string User) SplitMessages(IEnumerable<ChatMessage> messages)
      {
         var system = new StringBuilder();
         var user = new StringBuilder();
         foreach (var message in messages)
         {
            var target = message.Role == ChatRole.System
               ? system
               : user;
            if (target.Length > 0) target.Append('\n');
            target.Append(message.Text);
         }

         return (system.ToString(), user.ToString());
      }
   }
}