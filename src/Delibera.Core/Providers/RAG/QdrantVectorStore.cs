using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Delibera.Core.Providers.RAG;

/// <summary>
/// <see cref="IVectorStore"/> implementation backed by Qdrant vector database.
/// Uses the official Qdrant.Client NuGet gRPC client.
/// </summary>
public sealed class QdrantVectorStore : IVectorStore
{
   private readonly QdrantClient _client;

   /// <inheritdoc/>
   public string StoreName => "Qdrant";

   /// <summary>
   /// Creates a Qdrant vector store.
   /// </summary>
   /// <param name="host">Qdrant server host (e.g., "localhost").</param>
   /// <param name="port">Qdrant gRPC port (default 6334).</param>
   /// <param name="https">Whether to use HTTPS.</param>
   /// <param name="apiKey">Optional API key for Qdrant Cloud.</param>
   public QdrantVectorStore(string host = "localhost", int port = 6334, bool https = false, string? apiKey = null)
   {
      _client = new QdrantClient(host, port, https, apiKey);
   }

   /// <inheritdoc/>
   public async Task EnsureCollectionAsync(string collectionName, int vectorSize, CancellationToken ct = default)
   {
      var exists = await _client.CollectionExistsAsync(collectionName, ct);
      if (!exists)
      {
         await _client.CreateCollectionAsync(
             collectionName,
             new VectorParams { Size = (ulong)vectorSize, Distance = Distance.Cosine },
             cancellationToken: ct);
      }
   }

   /// <inheritdoc/>
   public async Task UpsertAsync(string collectionName, IReadOnlyList<VectorPoint> points, CancellationToken ct = default)
   {
      if (points.Count == 0) return;

      var grpcPoints = new List<PointStruct>(points.Count);

      foreach (var p in points)
      {
         var pointId = Guid.TryParse(p.Id, out var guid) ? guid : Guid.NewGuid();

         var payload = new Dictionary<string, Value>
         {
            ["text"] = p.Text
         };

         if (p.Metadata is not null)
         {
            foreach (var (k, v) in p.Metadata)
               payload[k] = v;
         }

         grpcPoints.Add(new PointStruct
         {
            Id = pointId,
            Vectors = p.Vector,
            Payload = { payload }
         });
      }

      await _client.UpsertAsync(collectionName, grpcPoints, cancellationToken: ct);
   }

   /// <inheritdoc/>
   public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
       string collectionName,
       float[] queryVector,
       int limit = 5,
       float scoreThreshold = 0.0f,
       CancellationToken ct = default)
   {
      var scored = await _client.SearchAsync(
          collectionName,
          queryVector,
          limit: (ulong)limit,
          scoreThreshold: scoreThreshold > 0 ? scoreThreshold : null,
          cancellationToken: ct);

      var results = new List<VectorSearchResult>(scored.Count);

      foreach (var s in scored)
      {
         var text = s.Payload.TryGetValue("text", out var tv) ? tv.StringValue : string.Empty;

         var meta = new Dictionary<string, string>();
         foreach (var (k, v) in s.Payload)
         {
            if (k != "text")
               meta[k] = v.StringValue;
         }

         results.Add(new VectorSearchResult(
             Id: s.Id.Uuid,
             Text: text,
             Score: s.Score,
             Metadata: meta.Count > 0 ? meta : null));
      }

      return results.AsReadOnly();
   }

   /// <inheritdoc/>
   public async Task DeleteCollectionAsync(string collectionName, CancellationToken ct = default)
   {
      await _client.DeleteCollectionAsync(collectionName, cancellationToken: ct);
   }

   /// <inheritdoc/>
   public async Task<long> CountAsync(string collectionName, CancellationToken ct = default)
   {
      var info = await _client.GetCollectionInfoAsync(collectionName, ct);
      return (long)info.PointsCount;
   }

   /// <inheritdoc/>
   public ValueTask DisposeAsync()
   {
      _client.Dispose();
      return ValueTask.CompletedTask;
   }
}
