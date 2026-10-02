using System.Text;
using Delibera.Core.Interfaces;

namespace Delibera.Core.Providers.RAG;

/// <summary>
///    Renders search hits as prompt-ready context text.
/// </summary>
/// <remarks>
///    Both <see cref="BaseRagProvider.GetContextAsync" /> and the Knowledge Keeper need
///    the same rendering. Sharing it matters for more than tidiness: a caller that already
///    holds the search results can format them directly instead of asking the provider to
///    search a second time, which used to double the embedding and vector-store traffic of
///    every round.
/// </remarks>
internal static class RagContextFormatter
{
   /// <summary>
   ///    Formats hits as numbered, scored sources. Returns an empty string for no hits.
   /// </summary>
   internal static string Format(IReadOnlyList<VectorSearchResult> results)
   {
      if (results.Count == 0)
         return string.Empty;

      var sb = new StringBuilder();
      for (var i = 0; i < results.Count; i++)
      {
         sb.AppendLine($"[Source {i + 1} — score: {results[i].Score:F3}]");
         sb.AppendLine(results[i].Text);
         sb.AppendLine();
      }

      return sb.ToString().TrimEnd();
   }
}
