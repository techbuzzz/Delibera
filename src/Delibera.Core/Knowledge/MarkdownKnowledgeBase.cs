namespace Delibera.Core.Knowledge;

/// <summary>
///    File-based knowledge base that loads Markdown files and provides
///    keyword search across their content.
/// </summary>
public sealed class MarkdownKnowledgeBase : IKnowledgeBase
{
   private readonly Dictionary<string, string> _documents = new(StringComparer.OrdinalIgnoreCase);

   /// <summary>Creates a Markdown knowledge base with an optional name.</summary>
   public MarkdownKnowledgeBase(string name = "Markdown Knowledge Base")
   {
      Name = name;
   }

   /// <summary>Number of loaded documents.</summary>
   public int DocumentCount => _documents.Count;

   /// <summary>Total characters across all documents.</summary>
   public int TotalCharacters => _documents.Values.Sum(d => d.Length);

   /// <inheritdoc />
   public string Name { get; }

   /// <inheritdoc />
   public async Task LoadAsync(string source)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(source);
      var fullPath = Path.GetFullPath(source);
      if (!File.Exists(fullPath))
         throw new FileNotFoundException($"Knowledge file not found: {fullPath}");

      _documents[Path.GetFileName(fullPath)] = await File.ReadAllTextAsync(fullPath);
   }

   /// <inheritdoc />
   public async Task LoadManyAsync(IEnumerable<string> sources)
   {
      ArgumentNullException.ThrowIfNull(sources);
      foreach (var s in sources) await LoadAsync(s);
   }

   /// <inheritdoc />
   public string GetAllContent()
   {
      if (_documents.Count == 0) return string.Empty;
      var sb = new StringBuilder();
      foreach (var (name, content) in _documents)
      {
         sb.AppendLine($"--- [{name}] ---");
         sb.AppendLine(content);
         sb.AppendLine();
      }

      return sb.ToString();
   }

   /// <inheritdoc />
   public IReadOnlyList<string> Search(string query, int maxResults = 5)
   {
      ArgumentNullException.ThrowIfNull(query);
      if (string.IsNullOrWhiteSpace(query)) return [];

      var keywords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
      var scored = new List<(string chunk, int score)>();

      foreach (var (fileName, content) in _documents)
      {
         var paragraphs = content.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
         foreach (var p in paragraphs)
         {
            var lower = p.ToLowerInvariant();
            var score = keywords.Count(k => lower.Contains(k));
            if (score > 0)
               scored.Add(($"[{fileName}] {p.Trim()}", score));
         }
      }

      return scored
         .OrderByDescending(r => r.score)
         .Take(maxResults)
         .Select(r => r.chunk)
         .ToList()
         .AsReadOnly();
   }

   /// <inheritdoc />
   public IReadOnlyList<string> GetLoadedSources() => _documents.Keys.ToList().AsReadOnly();

   /// <summary>Loads all matching files from a directory.</summary>
   public async Task LoadDirectoryAsync(string directoryPath, string pattern = "*.md")
   {
      var full = Path.GetFullPath(directoryPath);
      if (!Directory.Exists(full))
         throw new DirectoryNotFoundException($"Directory not found: {full}");

      await LoadManyAsync(Directory.GetFiles(full, pattern, SearchOption.AllDirectories));
   }
}
