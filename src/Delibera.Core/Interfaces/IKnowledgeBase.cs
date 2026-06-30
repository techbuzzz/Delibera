namespace Delibera.Core.Interfaces;

/// <summary>
///    Abstraction for loading and searching contextual knowledge.
///    Implement to connect various knowledge sources (Markdown files, RAG, databases).
/// </summary>
public interface IKnowledgeBase
{
   /// <summary>Knowledge base name.</summary>
   string Name { get; }

   /// <summary>
   ///    Loads knowledge from the specified source (file path or URI).
   ///    Implementations should honor <paramref name="ct"/> cooperatively and
   ///    throw <see cref="OperationCanceledException"/> when cancellation is requested.
   /// </summary>
   /// <param name="source">File path or URI identifying the knowledge source.</param>
   /// <param name="ct">Cancellation token; checked at entry and during the load.</param>
   /// <exception cref="OperationCanceledException">The token has been canceled.</exception>
   Task LoadAsync(string source, CancellationToken ct = default);

   /// <summary>
   ///    Loads knowledge from multiple sources sequentially.
   ///    Implementations should honor <paramref name="ct"/> cooperatively and
   ///    check for cancellation between sources.
   /// </summary>
   /// <param name="sources">The knowledge sources to load.</param>
   /// <param name="ct">Cancellation token; checked at entry and between sources.</param>
   /// <exception cref="OperationCanceledException">The token has been canceled.</exception>
   Task LoadManyAsync(IEnumerable<string> sources, CancellationToken ct = default);

   /// <summary>Returns all loaded knowledge as a single text blob.</summary>
   string GetAllContent();

   /// <summary>Keyword search across loaded content.</summary>
   /// <param name="query">Search query.</param>
   /// <param name="maxResults">Maximum number of results.</param>
   IReadOnlyList<string> Search(string query, int maxResults = 5);

   /// <summary>Returns a list of loaded source identifiers.</summary>
   IReadOnlyList<string> GetLoadedSources();
}