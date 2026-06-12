using OllamaSharp;
using OllamaSharp.Models;

namespace Delibera.Core.Providers.LLM;

/// <summary>
///    Generates embeddings through an Ollama server using a specified embedding model.
///    Wraps <see cref="OllamaProvider" /> for the HTTP transport layer.
/// </summary>
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
{
   private readonly OllamaApiClient _client;
   private int? _cachedVectorSize;

   /// <summary>
   ///    Creates an Ollama embedding provider.
   /// </summary>
   /// <param name="ollamaProvider">The Ollama LLM provider that supplies the HTTP client.</param>
   /// <param name="embeddingModel">Name of the embedding model (e.g., "nomic-embed-text", "llama2").</param>
   /// <param name="vectorSize">
   ///    Known vector dimensionality. If <c>null</c> the provider will auto-detect it
   ///    on the first <see cref="EmbedAsync" /> call.
   /// </param>
   public OllamaEmbeddingProvider(OllamaProvider ollamaProvider, string embeddingModel, int? vectorSize = null)
   {
      ArgumentNullException.ThrowIfNull(ollamaProvider);
      ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);

      _client = ollamaProvider.Client;
      EmbeddingModelName = embeddingModel;
      _cachedVectorSize = vectorSize;
   }

   /// <summary>
   ///    Convenience constructor that creates its own internal Ollama HTTP client.
   /// </summary>
   public OllamaEmbeddingProvider(string endpoint, string embeddingModel, string apiKey = "", int? vectorSize = null)
      : this(new OllamaProvider(endpoint, apiKey), embeddingModel, vectorSize)
   {
   }

   /// <inheritdoc />
   public string EmbeddingModelName { get; }

   /// <inheritdoc />
   public int VectorSize => _cachedVectorSize ??
                            throw new InvalidOperationException(
                               "Vector size unknown — call EmbedAsync at least once so the provider can probe the model.");

   /// <inheritdoc />
   public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(text);

      var request = new EmbedRequest { Model = EmbeddingModelName, Input = [text] };
      var response = await _client.EmbedAsync(request, ct);

      var embedding = response.Embeddings.FirstOrDefault() ?? throw new InvalidOperationException($"No embedding returned by model '{EmbeddingModelName}'.");

      var vector = embedding.Select(d => d).ToArray();
      _cachedVectorSize ??= vector.Length;
      return vector;
   }

   /// <inheritdoc />
   public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(texts);
      if (texts.Count == 0) return [];

      var request = new EmbedRequest { Model = EmbeddingModelName, Input = texts.ToList() };
      var response = await _client.EmbedAsync(request, ct);

      var result = new List<float[]>(texts.Count);
      foreach (var emb in response.Embeddings)
      {
         var vector = emb.Select(d => d).ToArray();
         _cachedVectorSize ??= vector.Length;
         result.Add(vector);
      }

      return result.AsReadOnly();
   }
}