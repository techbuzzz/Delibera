using Polly;

namespace Delibera.Core.Resilience;

/// <summary>
///    Static helpers for running <see cref="HttpClient" /> calls through a Polly v8
///    <see cref="ResiliencePipeline{TResult}" />.
/// </summary>
/// <remarks>
///    Polly v8 works at the callback level rather than the transport level: the
///    pipeline is asked to <c>ExecuteAsync</c> a delegate that issues a single
///    HTTP request. The Delibera providers use this helper to opt-in to the
///    named pipeline registered through <see cref="IDeliberaResiliencePipelineProvider" />.
/// </remarks>
public static class ResilientHttpClientExtensions
{
   /// <summary>
   ///    Sends a request through <paramref name="pipeline" /> with retry semantics.
   ///    Cancellation is wired through both the
   ///    <see cref="CancellationToken" /> argument and the Polly v8
   ///    <see cref="ResilienceContext" />.
   /// </summary>
   /// <param name="http">The HttpClient that owns the underlying transport.</param>
   /// <param name="pipeline">
   ///    The Polly v8 pipeline. Pass
   ///    <see cref="ResiliencePipeline{TResult}.Empty" /> to disable resilience.
   /// </param>
   /// <param name="request">The request message to send.</param>
   /// <param name="completionOption">How the response content should be read.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>The HTTP response message from the last (successful or final) attempt.</returns>
   public static async Task<HttpResponseMessage> SendAsync(
      this HttpClient http,
      ResiliencePipeline<HttpResponseMessage> pipeline,
      HttpRequestMessage request,
      HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
      CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(http);
      ArgumentNullException.ThrowIfNull(pipeline);
      ArgumentNullException.ThrowIfNull(request);

      var context = ResilienceContextPool.Shared.Get(ct);
      try
      {
         Func<ResilienceContext, HttpState, ValueTask<HttpResponseMessage>> callback = async (ctx, state) =>
         {
            var token = ctx.CancellationToken;
            // Clone the request for each attempt — HttpClient.SendAsync
            // disposes the request on the first attempt, leaving retries
            // unable to re-send.
            var clone = await CloneAsync(state.Request, token).ConfigureAwait(false);
            return await state.Http.SendAsync(clone, state.CompletionOption, token).ConfigureAwait(false);
         };

         return await pipeline.ExecuteAsync(callback, context, new HttpState(http, request, completionOption)).ConfigureAwait(false);
      }
      finally
      {
         ResilienceContextPool.Shared.Return(context);
      }
   }

   private static async ValueTask<HttpRequestMessage> CloneAsync(HttpRequestMessage source, CancellationToken ct)
   {
      var clone = new HttpRequestMessage(source.Method, source.RequestUri);
      if (source.Content is not null)
      {
         var buffer = await source.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
         clone.Content = new ByteArrayContent(buffer);
         foreach (var header in source.Content.Headers)
            clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
      }
      foreach (var header in source.Headers)
         clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
      foreach (var prop in source.Options)
         clone.Options.Set(new HttpRequestOptionsKey<object?>(prop.Key), prop.Value);
      return clone;
   }

   private sealed record HttpState(HttpClient Http, HttpRequestMessage Request, HttpCompletionOption CompletionOption);
}