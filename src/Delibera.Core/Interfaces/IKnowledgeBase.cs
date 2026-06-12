namespace Delibera.Core.Interfaces;

/// <summary>
///    Abstraction for loading and searching contextual knowledge.
///    Implement to connect various knowledge sources (Markdown files, RAG, databases).
/// </summary>
public interface IKnowledgeBase
{
   /// <summary>Knowledge base name.</summary>
   string Name { get; }

   /// <summary>Loads knowledge from the specified source (file path or URI).</summary>
   Task LoadAsync(string source);

   /// <summary>Loads knowledge from multiple sources.</summary>
   Task LoadManyAsync(IEnumerable<string> sources);

   /// <summary>Returns all loaded knowledge as a single text blob.</summary>
   string GetAllContent();

   /// <summary>Keyword search across loaded content.</summary>
   /// <param name="query">Search query.</param>
   /// <param name="maxResults">Maximum number of results.</param>
   IReadOnlyList<string> Search(string query, int maxResults = 5);

   /// <summary>Returns a list of loaded source identifiers.</summary>
   IReadOnlyList<string> GetLoadedSources();
}