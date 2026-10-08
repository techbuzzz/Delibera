using System.Runtime.CompilerServices;
using Delibera.Core.Providers.LLM;
using Delibera.Core.Tools;

#pragma warning disable IDE1006 // 'LLM' acronym kept all-caps by convention; renaming is a breaking API change

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
   public static ILLMProvider AsLLMProvider(this IChatClient chatClient, string? providerName = null,
      bool ownsClient = true)
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
   ///    Minimal <see cref="IChatClient" /> adapter over a Delibera <see cref="ILLMProvider" />,
   ///    including a text bridge for tool traffic.
   /// </summary>
   /// <remarks>
   ///    <para>
   ///       A provider that returns plain strings cannot emit a
   ///       <see cref="FunctionCallContent" />, and <c>FunctionInvokingChatClient</c> only reacts to
   ///       one. So this adapter translates in both directions:
   ///    </para>
   ///    <list type="bullet">
   ///       <item>
   ///          <b>Outbound.</b> The tools on <c>ChatOptions.Tools</c> are rendered into the system
   ///          prompt as the <c>[[TOOL: name {json}]]</c> protocol, and
   ///          <see cref="FunctionCallContent" /> / <see cref="FunctionResultContent" /> already in
   ///          the conversation are flattened into readable text so the model can see what it
   ///          previously asked for and what came back.
   ///       </item>
   ///       <item>
   ///          <b>Inbound.</b> A marker in the response is parsed back into a real
   ///          <see cref="FunctionCallContent" /> and stripped from the visible text.
   ///       </item>
   ///    </list>
   ///    <para>
   ///       Without this the middleware was silently inert: it saw no tool request, so it never
   ///       invoked anything, and the caller got an ordinary text answer with no error to explain why.
   ///    </para>
   /// </remarks>
   private sealed class LLMProviderChatClient(ILLMProvider provider, string? defaultModel) : IChatClient
   {
      private readonly ChatClientMetadata _metadata = new(provider.ProviderName, defaultModelId: defaultModel);

      public async Task<ChatResponse> GetResponseAsync(
         IEnumerable<ChatMessage> messages,
         ChatOptions? options = null,
         CancellationToken cancellationToken = default)
      {
         var tools = MaterializeTools(options);
         var (system, user) = FlattenMessages(messages, tools);

         var text = await provider.ChatAsync(
            ResolveModel(options),
            system,
            user,
            options?.Temperature ?? 0.7f,
            cancellationToken).ConfigureAwait(false);

         var contents = new List<AIContent>();
         var markerStream = new ToolMarkerStream();

         var visible = markerStream.Append(text ?? string.Empty, out var request);
         if (visible.Length > 0)
            contents.Add(new TextContent(visible));

         if (request is not null)
            contents.Add(ToFunctionCall(request));

         return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents))
         {
            ModelId = ResolveModel(options)
         };
      }

      public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
         IEnumerable<ChatMessage> messages,
         ChatOptions? options = null,
         [EnumeratorCancellation] CancellationToken cancellationToken = default)
      {
         var tools = MaterializeTools(options);
         var (system, user) = FlattenMessages(messages, tools);

         // One marker stream for the whole call: a marker may straddle any chunk boundary, so the
         // parser has to hold the tail across chunks rather than resetting per chunk.
         var markerStream = new ToolMarkerStream();

         await foreach (var chunk in provider.ChatStreamAsync(
                              ResolveModel(options), system, user, options?.Temperature ?? 0.7f, cancellationToken)
                           .ConfigureAwait(false))
         {
            var visible = markerStream.Append(chunk ?? string.Empty, out var request);

            if (visible.Length > 0)
               yield return new ChatResponseUpdate(ChatRole.Assistant, visible);

            if (request is not null) yield return new ChatResponseUpdate(ChatRole.Assistant, [ToFunctionCall(request)]);
         }

         // A response that ends mid-marker still has to deliver its text rather than swallow it.
         var tail = markerStream.Flush();
         if (tail.Length > 0)
            yield return new ChatResponseUpdate(ChatRole.Assistant, tail);
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

      private static FunctionCallContent ToFunctionCall(ToolCallParser.Request request)
      {
         // A call id is what pairs a request with its result. It is generated here because a
         // text-protocol provider has no wire-level id to reuse.
         var arguments = ToolCallParser.TryParseArguments(request.ArgumentsJson);
         return new FunctionCallContent(Guid.NewGuid().ToString("N"), request.ToolName, arguments);
      }

      private static IReadOnlyList<AITool> MaterializeTools(ChatOptions? options)
      {
         if (options?.Tools is not { Count: > 0 } configured) return [];

         var tools = new List<AITool>(configured.Count);
         foreach (var tool in configured) tools.Add(tool);

         return tools;
      }

      private string ResolveModel(ChatOptions? options)
      {
         return options?.ModelId is { Length: > 0 } m
            ? m
            : defaultModel ?? string.Empty;
      }

      /// <summary>
      ///    Flattens the conversation into the (system, user) pair the string-only provider accepts,
      ///    carrying tool traffic across instead of dropping it.
      /// </summary>
      /// <remarks>
      ///    Function-call and function-result content have no <c>Text</c>, so reading
      ///    <c>message.Text</c> alone silently discarded every tool exchange. They are rendered
      ///    explicitly here.
      /// </remarks>
      private static (string System, string User) FlattenMessages(
         IEnumerable<ChatMessage> messages,
         IReadOnlyList<AITool> tools)
      {
         var system = new StringBuilder();
         var user = new StringBuilder();

         if (tools.Count > 0)
            system.Append(ToolCallParser.BuildBriefing(tools));

         foreach (var message in messages)
         {
            var target = message.Role == ChatRole.System ? system : user;

            foreach (var content in message.Contents)
               switch (content)
               {
                  case FunctionCallContent call:
                     AppendLine(target,
                        $"[tool call] {call.Name}({ToolCallParser.SerializeArguments(call.Arguments)})");
                     break;

                  case FunctionResultContent result:
                     AppendLine(target, $"[tool result] {result.Result}");
                     break;

                  case TextContent text:
                     if (!string.IsNullOrEmpty(text.Text))
                        AppendLine(target, text.Text);
                     break;
               }
         }

         return (system.ToString(), user.ToString());
      }

      private static void AppendLine(StringBuilder target, string value)
      {
         if (target.Length > 0) target.Append('\n');
         target.Append(value);
      }
   }
}
