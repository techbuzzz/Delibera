using Microsoft.Extensions.AI;

namespace Delibera.Core.Providers.LLM;

/// <summary>
///    A universal <see cref="IEmbeddingProvider" /> implemented on top of the Microsoft.Extensions.AI
///    <see cref="IEmbeddingGenerator{TInput,TEmbedding}" /> abstraction.
/// </summary>
/// <remarks>
///    Wraps any <c>IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c> — Ollama (OllamaSharp),
///    OpenAI / Azure OpenAI, local OpenAI-compatible servers, etc. — so RAG indexing and querying
///    work with the standard .NET AI embedding contract instead of a provider-specific SDK call.
/// </remarks>
public sealed class EmbeddingGeneratorProvider : IEmbeddingProvider, IDisposable
{
   private readonly bool _ownsGenerator;
   private int? _cachedVectorSize;
   private bool _disposed;

   /// <summary>
   ///    Wraps an existing <see cref="IEmbeddingGenerator{TInput,TEmbedding}" />.
   /// </summary>
   /// <param name="generator">The Microsoft.Extensions.AI embedding generator to wrap.</param>
   /// <param name="modelName">
   ///    Friendly model name. When <c>null</c> it is read from the generator metadata
   ///    (<see cref="EmbeddingGeneratorMetadata.DefaultModelId" />), falling back to "embedding".
   /// </param>
   /// <param name="vectorSize">
   ///    Known vector dimensionality. When <c>null</c> it is auto-detected from metadata or on the
   ///    first <see cref="EmbedAsync" /> call.
   /// </param>
   /// <param name="ownsGenerator">
   ///    When <c>true</c> (default) the wrapped generator is disposed together with this provider.
   /// </param>
   public EmbeddingGeneratorProvider(
      IEmbeddingGenerator<string, Embedding<float>> generator,
      string? modelName = null,
      int? vectorSize = null,
      bool ownsGenerator = true)
   {
      Generator = generator ?? throw new ArgumentNullException(nameof(generator));
      _ownsGenerator = ownsGenerator;

      var metadata = generator.GetService(typeof(EmbeddingGeneratorMetadata)) as EmbeddingGeneratorMetadata;
      EmbeddingModelName = modelName ??
                           (string.IsNullOrWhiteSpace(metadata?.DefaultModelId)
                              ? "embedding"
                              : metadata!.DefaultModelId!);
      _cachedVectorSize = vectorSize ?? metadata?.DefaultModelDimensions;
   }

   /// <summary>Exposes the wrapped generator for advanced scenarios and middleware composition.</summary>
   public IEmbeddingGenerator<string, Embedding<float>> Generator { get; }

   /// <inheritdoc />
   public void Dispose()
   {
      if (_disposed) return;
      _disposed = true;
      if (_ownsGenerator) Generator.Dispose();
      GC.SuppressFinalize(this);
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

      var result = await Generator.GenerateAsync([text], cancellationToken: ct);
      var embedding = result.FirstOrDefault() ?? throw new InvalidOperationException($"No embedding returned by model '{EmbeddingModelName}'.");

      var vector = embedding.Vector.ToArray();
      _cachedVectorSize ??= vector.Length;
      return vector;
   }

   /// <inheritdoc />
   public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(texts);
      if (texts.Count == 0) return [];

      var result = await Generator.GenerateAsync(texts, cancellationToken: ct);

      var vectors = new List<float[]>(texts.Count);
      foreach (var embedding in result)
      {
         var vector = embedding.Vector.ToArray();
         _cachedVectorSize ??= vector.Length;
         vectors.Add(vector);
      }

      return vectors.AsReadOnly();
   }
}