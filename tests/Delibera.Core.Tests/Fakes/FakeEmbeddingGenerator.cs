using Microsoft.Extensions.AI;

namespace Delibera.Core.Tests.Fakes;

/// <summary>
///    In-memory <see cref="IEmbeddingGenerator{TInput,TEmbedding}" /> producing deterministic vectors.
/// </summary>
public sealed class FakeEmbeddingGenerator(int dimensions = 4, string modelName = "fake-embed") : IEmbeddingGenerator<string, Embedding<float>>
{
   private readonly EmbeddingGeneratorMetadata _metadata = new("Fake", defaultModelId: modelName, defaultModelDimensions: dimensions);

   public bool Disposed { get; private set; }

   public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
      IEnumerable<string> values,
      EmbeddingGenerationOptions? options = null,
      CancellationToken cancellationToken = default)
   {
      var embeddings = new GeneratedEmbeddings<Embedding<float>>();
      foreach (var value in values)
      {
         var vector = new float[dimensions];
         for (var i = 0; i < dimensions; i++)
            vector[i] = (value.Length + i) % 7 / 7f;
         embeddings.Add(new Embedding<float>(vector));
      }

      return Task.FromResult(embeddings);
   }

   public object? GetService(Type serviceType, object? serviceKey = null)
   {
      ArgumentNullException.ThrowIfNull(serviceType);
      return serviceKey is null && serviceType.IsInstanceOfType(_metadata) ? _metadata : null;
   }

   public void Dispose() => Disposed = true;
}
