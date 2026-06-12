using Npgsql;
using Pgvector;

namespace Delibera.Core.Providers.RAG;

/// <summary>
/// <see cref="IVectorStore"/> implementation backed by PostgreSQL with the pgvector extension.
/// Uses <c>Npgsql</c> + <c>Pgvector</c> NuGet packages for native vector operations.
/// </summary>
/// <remarks>
/// <para>Requires PostgreSQL 15+ with the <c>vector</c> extension installed:</para>
/// <code>CREATE EXTENSION IF NOT EXISTS vector;</code>
/// <para>Each collection maps to a dedicated table with schema:
/// <c>(id UUID PRIMARY KEY, text TEXT, metadata JSONB, embedding vector(N))</c>.
/// Similarity search uses cosine distance (<c>&lt;=&gt;</c> operator).</para>
/// </remarks>
public sealed class PgVectorStore : IVectorStore
{
   private readonly NpgsqlDataSource _dataSource;
   private readonly bool _ownsDataSource;

   /// <inheritdoc/>
   public string StoreName => "PgVector";

   /// <summary>
   /// Creates a PgVector store from a connection string.
   /// </summary>
   /// <param name="connectionString">PostgreSQL connection string (e.g., <c>"Host=localhost;Database=council;Username=postgres;Password=secret"</c>).</param>
   public PgVectorStore(string connectionString)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

      var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
      dataSourceBuilder.UseVector();
      _dataSource = dataSourceBuilder.Build();
      _ownsDataSource = true;
   }

   /// <summary>
   /// Creates a PgVector store from a pre-configured <see cref="NpgsqlDataSource"/>.
   /// The data source must already have the <c>UseVector()</c> extension method called.
   /// </summary>
   /// <param name="dataSource">Pre-built Npgsql data source with vector support.</param>
   public PgVectorStore(NpgsqlDataSource dataSource)
   {
      _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
      _ownsDataSource = false;
   }

   /// <inheritdoc/>
   public async Task EnsureCollectionAsync(string collectionName, int vectorSize, CancellationToken ct = default)
   {
      var tableName = SanitizeTableName(collectionName);

      await using var conn = await _dataSource.OpenConnectionAsync(ct);

      // Ensure pgvector extension exists
      await using (var extCmd = conn.CreateCommand())
      {
         extCmd.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
         await extCmd.ExecuteNonQueryAsync(ct);
      }

      // Create the table if it doesn't exist
      await using var cmd = conn.CreateCommand();
      cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {tableName} (
                id UUID PRIMARY KEY,
                text TEXT NOT NULL,
                metadata JSONB,
                embedding vector({vectorSize})
            )
            """;
      await cmd.ExecuteNonQueryAsync(ct);

      // Create an IVFFlat index for fast cosine-distance search (if not exists)
      await using var idxCmd = conn.CreateCommand();
      idxCmd.CommandText = $"""
            CREATE INDEX IF NOT EXISTS idx_{tableName}_embedding
            ON {tableName}
            USING ivfflat (embedding vector_cosine_ops)
            WITH (lists = 100)
            """;
      try
      {
         await idxCmd.ExecuteNonQueryAsync(ct);
      }
      catch (PostgresException)
      {
         // IVFFlat needs at least some rows for lists parameter;
         // ignore errors on empty tables — index can be created later.
      }
   }

   /// <inheritdoc/>
   public async Task UpsertAsync(string collectionName, IReadOnlyList<VectorPoint> points, CancellationToken ct = default)
   {
      if (points.Count == 0) return;

      var tableName = SanitizeTableName(collectionName);
      await using var conn = await _dataSource.OpenConnectionAsync(ct);
      await using var batch = new NpgsqlBatch(conn);

      foreach (var p in points)
      {
         var id = Guid.TryParse(p.Id, out var guid) ? guid : Guid.NewGuid();
         var metadataJson = p.Metadata is { Count: > 0 }
             ? System.Text.Json.JsonSerializer.Serialize(p.Metadata)
             : null;

         var cmd = new NpgsqlBatchCommand($"""
                INSERT INTO {tableName} (id, text, metadata, embedding)
                VALUES ($1, $2, $3::jsonb, $4)
                ON CONFLICT (id) DO UPDATE SET
                    text = EXCLUDED.text,
                    metadata = EXCLUDED.metadata,
                    embedding = EXCLUDED.embedding
                """);
         cmd.Parameters.AddWithValue(id);
         cmd.Parameters.AddWithValue(p.Text);
         cmd.Parameters.AddWithValue(metadataJson is not null ? (object)metadataJson : DBNull.Value);
         cmd.Parameters.AddWithValue(new Vector(p.Vector));

         batch.BatchCommands.Add(cmd);
      }

      await batch.ExecuteNonQueryAsync(ct);
   }

   /// <inheritdoc/>
   public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
       string collectionName,
       float[] queryVector,
       int limit = 5,
       float scoreThreshold = 0.0f,
       CancellationToken ct = default)
   {
      var tableName = SanitizeTableName(collectionName);
      await using var conn = await _dataSource.OpenConnectionAsync(ct);
      await using var cmd = conn.CreateCommand();

      // Cosine distance: <=> returns distance [0..2], convert to similarity [0..1]
      // similarity = 1 - distance
      cmd.CommandText = $"""
            SELECT id, text, metadata::text, 1 - (embedding <=> $1) AS score
            FROM {tableName}
            WHERE 1 - (embedding <=> $1) >= $3
            ORDER BY embedding <=> $1
            LIMIT $2
            """;
      cmd.Parameters.AddWithValue(new Vector(queryVector));
      cmd.Parameters.AddWithValue(limit);
      cmd.Parameters.AddWithValue((double)scoreThreshold);

      await using var reader = await cmd.ExecuteReaderAsync(ct);
      var results = new List<VectorSearchResult>();

      while (await reader.ReadAsync(ct))
      {
         var id = reader.GetGuid(0).ToString();
         var text = reader.GetString(1);
         var metaJson = reader.IsDBNull(2) ? null : reader.GetString(2);
         var score = (float)reader.GetDouble(3);

         Dictionary<string, string>? metadata = null;
         if (metaJson is not null)
         {
            try
            {
               metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(metaJson);
            }
            catch { /* skip malformed metadata */ }
         }

         results.Add(new VectorSearchResult(id, text, score, metadata));
      }

      return results.AsReadOnly();
   }

   /// <inheritdoc/>
   public async Task DeleteCollectionAsync(string collectionName, CancellationToken ct = default)
   {
      var tableName = SanitizeTableName(collectionName);
      await using var conn = await _dataSource.OpenConnectionAsync(ct);
      await using var cmd = conn.CreateCommand();
      cmd.CommandText = $"DROP TABLE IF EXISTS {tableName}";
      await cmd.ExecuteNonQueryAsync(ct);
   }

   /// <inheritdoc/>
   public async Task<long> CountAsync(string collectionName, CancellationToken ct = default)
   {
      var tableName = SanitizeTableName(collectionName);
      await using var conn = await _dataSource.OpenConnectionAsync(ct);
      await using var cmd = conn.CreateCommand();
      cmd.CommandText = $"SELECT COUNT(*) FROM {tableName}";

      var result = await cmd.ExecuteScalarAsync(ct);
      return result is long l ? l : Convert.ToInt64(result);
   }

   /// <inheritdoc/>
   public async ValueTask DisposeAsync()
   {
      if (_ownsDataSource)
         await _dataSource.DisposeAsync();
   }

   // ──────────────────────────────────────────────
   // Helpers
   // ──────────────────────────────────────────────

   /// <summary>
   /// Sanitises a collection name for use as a PostgreSQL table name.
   /// Replaces non-alphanumeric characters with underscores and adds a prefix.
   /// </summary>
   private static string SanitizeTableName(string collectionName)
   {
      var safe = System.Text.RegularExpressions.Regex.Replace(
          collectionName.ToLowerInvariant(), @"[^a-z0-9_]", "_");
      return $"vc_{safe}";
   }
}
