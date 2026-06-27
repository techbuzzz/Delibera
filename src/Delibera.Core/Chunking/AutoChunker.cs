using Delibera.Core.Compression;

namespace Delibera.Core.Chunking;

/// <summary>
///    Strategy for splitting a large document into chunks that fit within a model's context window.
/// </summary>
public enum ChunkingStrategy
{
   /// <summary>
   ///    Splits on semantic boundaries — Markdown headers first, then paragraphs, then sentences.
   ///    Best for structured documents (contracts, reports, articles).
   /// </summary>
   SemanticBoundary = 0,

   /// <summary>
   ///    Splits into fixed-size chunks of approximately <c>availableTokens</c> tokens each.
   ///    Fast but may break mid-sentence. Good for unstructured text.
   /// </summary>
   FixedSize,

   /// <summary>
   ///    Sliding window with 20% overlap between chunks. Each chunk starts halfway through
   ///    the previous one, ensuring no information is lost at boundaries.
   ///    Best for narrative text where context continuity matters.
   /// </summary>
   SlidingWindow
}

/// <summary>
///    A single chunk of a larger document, annotated with positional metadata.
/// </summary>
public sealed record DocumentChunk
{
   /// <summary>Zero-based index of this chunk within the plan.</summary>
   public required int Index { get; init; }

   /// <summary>The chunk text content.</summary>
   public required string Content { get; init; }

   /// <summary>Estimated token count for this chunk.</summary>
   public required int EstimatedTokens { get; init; }

   /// <summary>
   ///    The nearest Markdown heading that precedes this chunk, or the document title.
   ///    Provides context so the model knows which section it is reading.
   /// </summary>
   public required string SectionTitle { get; init; }

   /// <summary>Character offset where this chunk starts in the original document.</summary>
   public required int StartChar { get; init; }

   /// <summary>Character offset where this chunk ends in the original document (exclusive).</summary>
   public required int EndChar { get; init; }
}

/// <summary>
///    The result of planning how to split a document for progressive disclosure across debate rounds.
/// </summary>
public sealed record ChunkingPlan
{
   /// <summary>All chunks in order.</summary>
   public required IReadOnlyList<DocumentChunk> Chunks { get; init; }

   /// <summary>Total number of chunks.</summary>
   public required int TotalChunks { get; init; }

   /// <summary>Average estimated tokens per chunk.</summary>
   public required int EstimatedTokensPerChunk { get; init; }

   /// <summary>The context window size used for planning (tokens).</summary>
   public required int ContextWindowTokens { get; init; }

   /// <summary>Tokens available per round after subtracting overhead.</summary>
   public required int AvailableTokensPerRound { get; init; }

   /// <summary><c>true</c> when the entire document fits in a single round without chunking.</summary>
   public required bool FitsInSingleRound { get; init; }

   /// <summary>Recommended number of debate rounds to cover all chunks.</summary>
   public required int RecommendedRounds { get; init; }

   /// <summary>Estimated token overhead per round (system prompt + user question + response buffer).</summary>
   public required int OverheadTokens { get; init; }
}

/// <summary>
///    Splits large documents into context-window-sized chunks using semantic boundary detection.
///    Used by <see cref="AutoChunkingOrchestrator" /> to enable progressive disclosure in debates.
/// </summary>
/// <remarks>
///    <para>
///       The chunker uses <see cref="TokenCounter.Default" /> for token estimation.
///       For precise counts, configure <see cref="TokenCounter.CharsPerToken" /> to match
///       the model family (4.0 for GPT-style, 3.5 for Llama).
///    </para>
///    <para>
///       <see cref="ChunkingStrategy.SemanticBoundary" /> (default) respects document structure:
///       it splits on Markdown headers first, then paragraph breaks, and only falls back to
///       sentence boundaries when a single section is too large.
///    </para>
/// </remarks>
public static class AutoChunker
{
   // Separators in priority order — from largest structural boundary to smallest.
   private static readonly string[] HeaderSeps = ["\n# ", "\n## ", "\n### ", "\n#### ", "\n##### "];
   private static readonly string[] ParaSeps = ["\n\n", "\r\n\r\n"];
   private static readonly string[] SentSeps = [". ", "! ", "? ", ".\n", "!\n", "?\n", ".\r\n", "!\r\n", "?\r\n"];

   /// <summary>
   ///    Creates a chunking plan for the given document, respecting the model's context window.
   /// </summary>
   /// <param name="document">The full document text.</param>
   /// <param name="contextWindowTokens">
   ///    The model's total context window in tokens. This is the hard limit — the chunker
   ///    ensures no single chunk exceeds <c>contextWindowTokens - overheadTokens</c>.
   /// </param>
   /// <param name="overheadTokens">
   ///    Estimated tokens consumed by the system prompt, user question, debate history,
   ///    and expected response. Subtracted from the context window to determine available space.
   /// </param>
   /// <param name="strategy">Chunking strategy to use.</param>
   /// <param name="safetyMargin">
   ///    Additional safety margin as a fraction of the context window (0.0–0.5).
   ///    Default 0.15 = 15% reserved for token estimation inaccuracy.
   /// </param>
   /// <returns>A <see cref="ChunkingPlan" /> describing how to split and distribute the document.</returns>
   public static ChunkingPlan CreatePlan(
      string document,
      int contextWindowTokens,
      int overheadTokens,
      ChunkingStrategy strategy = ChunkingStrategy.SemanticBoundary,
      double safetyMargin = 0.15)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(document);
      ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contextWindowTokens);
      ArgumentOutOfRangeException.ThrowIfNegative(overheadTokens);

      // Calculate available tokens per round with safety margin.
      var safetyReserve = (int)(contextWindowTokens * Math.Clamp(safetyMargin, 0.0, 0.5));
      var availableTokens = contextWindowTokens - overheadTokens - safetyReserve;
      if (availableTokens <= 0)
         availableTokens = Math.Max(256, contextWindowTokens / 4); // fallback: at least 256 tokens

      var totalTokens = TokenCounter.Default.EstimateTokens(document);
      var fitsInSingle = totalTokens <= availableTokens;

      List<DocumentChunk> chunks;
      if (fitsInSingle)
      {
         chunks =
         [
            new DocumentChunk
            {
               Index = 0,
               Content = document,
               EstimatedTokens = totalTokens,
               SectionTitle = "Full Document",
               StartChar = 0,
               EndChar = document.Length
            }
         ];
      }
      else
      {
         chunks = strategy switch
         {
            ChunkingStrategy.SlidingWindow => ChunkBySlidingWindow(document, availableTokens),
            ChunkingStrategy.FixedSize => ChunkByFixedSize(document, availableTokens),
            _ => ChunkBySemanticBoundaries(document, availableTokens)
         };
      }

      // Recommend rounds: aim for ~3 chunks per round, minimum 1.
      var recommendedRounds = Math.Max(1, (int)Math.Ceiling((double)chunks.Count / 3));

      return new ChunkingPlan
      {
         Chunks = chunks.AsReadOnly(),
         TotalChunks = chunks.Count,
         EstimatedTokensPerChunk = chunks.Count > 0
            ? (int)chunks.Average(c => c.EstimatedTokens)
            : 0,
         ContextWindowTokens = contextWindowTokens,
         AvailableTokensPerRound = availableTokens,
         FitsInSingleRound = fitsInSingle,
         RecommendedRounds = recommendedRounds,
         OverheadTokens = overheadTokens
      };
   }

   // ──────────────────────────────────────────────
   // Strategy: SemanticBoundary
   // ──────────────────────────────────────────────

   private static List<DocumentChunk> ChunkBySemanticBoundaries(string doc, int maxTokens)
   {
      var sections = SplitByHeaders(doc);
      var chunks = new List<DocumentChunk>();
      var index = 0;

      foreach (var (title, content, start, end) in sections)
      {
         var sectionTokens = TokenCounter.Default.EstimateTokens(content);

         if (sectionTokens <= maxTokens)
         {
            chunks.Add(MakeChunk(index++, content, title, start, end));
         }
         else
         {
            // Section too large — split by paragraphs.
            var subChunks = SplitByParagraphs(content, maxTokens);
            foreach (var sc in subChunks)
               chunks.Add(MakeChunk(index++, sc, title, start, end));
         }
      }

      return chunks;
   }

   /// <summary>
   ///    Splits text into sections delimited by Markdown headers.
   ///    Each section includes its header line as the title.
   ///    Uses a single-pass scan over the text — O(n) instead of O(n·k) per iteration.
   /// </summary>
   private static List<(string Title, string Content, int Start, int End)> SplitByHeaders(string doc)
   {
      var sections = new List<(string, string, int, int)>();
      var span = doc.AsSpan();

      // Single-pass: scan for any header pattern at each position.
      var headerPositions = new List<(int Pos, int Level, string Title)>();
      var i = 0;
      while (i < span.Length)
      {
         // Check if current position starts a header line (must be at line start or after \n).
         if (span[i] == '#' && (i == 0 || span[i - 1] == '\n'))
         {
            var hashCount = 1;
            var j = i + 1;
            while (j < span.Length && span[j] == '#') { hashCount++; j++; }
            // Must be followed by a space to be a valid Markdown header.
            if (j < span.Length && span[j] == ' ' && hashCount <= 6)
            {
               j++; // skip the space
               var titleStart = j;
               while (j < span.Length && span[j] != '\n' && span[j] != '\r') j++;
               var title = span[titleStart..j].Trim().ToString();
               headerPositions.Add((i, hashCount, title));
               i = j; // continue after this header line
            }
         }

         // Advance to next newline.
         while (i < span.Length && span[i] != '\n') i++;
         if (i < span.Length) i++; // skip the newline
      }

      if (headerPositions.Count == 0)
      {
         sections.Add(("Document", doc, 0, doc.Length));
         return sections;
      }

      // Build sections between headers.
      for (var idx = 0; idx < headerPositions.Count; idx++)
      {
         var (pos, _, title) = headerPositions[idx];
         var contentStart = doc.IndexOf('\n', pos);
         if (contentStart < 0) contentStart = pos;
         else contentStart++;

         var contentEnd = idx + 1 < headerPositions.Count
            ? headerPositions[idx + 1].Pos
            : doc.Length;

         var content = doc[contentStart..contentEnd].Trim();
         if (content.Length > 0)
            sections.Add((title, content, contentStart, contentEnd));
      }

      // If there's text before the first header, add it as a preamble.
      if (headerPositions[0].Pos > 0)
      {
         var preamble = doc[..headerPositions[0].Pos].Trim();
         if (preamble.Length > 0)
            sections.Insert(0, ("Preamble", preamble, 0, headerPositions[0].Pos));
      }

      return sections;
   }

   private static List<string> SplitByParagraphs(string text, int maxTokens)
   {
      var paragraphs = SplitBySeparators(text, ParaSeps);
      return MergeSmallChunks(paragraphs, maxTokens);
   }

   // ──────────────────────────────────────────────
   // Strategy: FixedSize
   // ──────────────────────────────────────────────

   private static List<DocumentChunk> ChunkByFixedSize(string doc, int maxTokens)
   {
      // Approximate character count for maxTokens.
      var approxChars = (int)(maxTokens * TokenCounter.Default.CharsPerToken);
      var chunks = new List<DocumentChunk>();
      var index = 0;
      var pos = 0;

      while (pos < doc.Length)
      {
         var end = Math.Min(pos + approxChars, doc.Length);

         // Try to break at a sentence boundary near the target.
         if (end < doc.Length)
         {
            var bestBreak = FindBestBreak(doc, pos, end);
            if (bestBreak > pos) end = bestBreak;
         }

         var chunk = doc[pos..end].Trim();
         if (chunk.Length > 0)
         {
            chunks.Add(MakeChunk(index++, chunk, $"Part {index}", pos, end));
         }

         if (end >= doc.Length) break;
         pos = end;
      }

      return chunks;
   }

   // ──────────────────────────────────────────────
   // Strategy: SlidingWindow
   // ──────────────────────────────────────────────

   private static List<DocumentChunk> ChunkBySlidingWindow(string doc, int maxTokens)
   {
      var approxChars = (int)(maxTokens * TokenCounter.Default.CharsPerToken);
      var step = approxChars / 2; // 50% overlap
      var chunks = new List<DocumentChunk>();
      var index = 0;
      var pos = 0;

      while (pos < doc.Length)
      {
         var end = Math.Min(pos + approxChars, doc.Length);

         if (end < doc.Length)
         {
            var bestBreak = FindBestBreak(doc, pos, end);
            if (bestBreak > pos) end = bestBreak;
         }

         var chunk = doc[pos..end].Trim();
         if (chunk.Length > 0)
         {
            chunks.Add(MakeChunk(index++, chunk, $"Window {index}", pos, end));
         }

         if (end >= doc.Length) break;
         pos = Math.Max(pos + step, pos + 1);
      }

      return chunks;
   }

   // ──────────────────────────────────────────────
   // Helpers
   // ──────────────────────────────────────────────

   private static List<string> SplitBySeparators(string text, string[] separators)
   {
      var parts = new List<string>();
      var span = text.AsSpan();

      // Single-pass: find the first matching separator and split on it.
      foreach (var sep in separators)
      {
         if (span.IsEmpty) break;

         var sepSpan = sep.AsSpan();
         var idx = span.IndexOf(sepSpan, StringComparison.Ordinal);
         if (idx >= 0)
         {
            // Split on all occurrences of this separator.
            var pos = 0;
            while (pos < span.Length)
            {
               var nextIdx = span[pos..].IndexOf(sepSpan, StringComparison.Ordinal);
               if (nextIdx < 0)
               {
                  var remaining = span[pos..].Trim();
                  if (remaining.Length > 0)
                     parts.Add(remaining.ToString());
                  break;
               }

               var part = span.Slice(pos, nextIdx).Trim();
               if (part.Length > 0)
                  parts.Add(part.ToString());

               pos += nextIdx + sepSpan.Length;
            }

            return parts;
         }
      }

      // No separator matched — split by sentences as last resort.
      var sentParts = SplitBySentencesSpan(span);
      foreach (var p in sentParts)
      {
         var trimmed = p.Trim();
         if (trimmed.Length > 0)
            parts.Add(trimmed);
      }

      return parts.Count > 0 ? parts : [text.Trim()];
   }

   /// <summary>
   ///    Splits a span by sentence-ending delimiters without intermediate string.Split allocations.
   /// </summary>
   private static List<string> SplitBySentencesSpan(ReadOnlySpan<char> span)
   {
      var parts = new List<string>();
      var pos = 0;

      while (pos < span.Length)
      {
         var nextDelim = int.MaxValue;
         var delimLen = 0;

         foreach (var d in SentSeps)
         {
            var idx = span[pos..].IndexOf(d.AsSpan(), StringComparison.Ordinal);
            if (idx >= 0 && pos + idx < nextDelim)
            {
               nextDelim = pos + idx;
               delimLen = d.Length;
            }
         }

         if (nextDelim == int.MaxValue)
         {
            var remaining = span[pos..].Trim();
            if (remaining.Length > 0)
               parts.Add(remaining.ToString());
            break;
         }

         var sentence = span[pos..(nextDelim + delimLen)].Trim();
         if (sentence.Length > 0)
            parts.Add(sentence.ToString());

         pos = nextDelim + delimLen;
      }

      return parts;
   }

   /// <summary>
   ///    Merges small chunks together so each chunk is as close to maxTokens as possible
   ///    without exceeding it. This avoids having many tiny chunks.
   /// </summary>
   private static List<string> MergeSmallChunks(List<string> parts, int maxTokens)
   {
      var merged = new List<string>();
      var current = new StringBuilder();
      var currentTokens = 0;

      foreach (var part in parts)
      {
         var partTokens = TokenCounter.Default.EstimateTokens(part);

         if (currentTokens + partTokens <= maxTokens)
         {
            if (current.Length > 0) current.Append("\n\n");
            current.Append(part);
            currentTokens += partTokens;
         }
         else
         {
            // Flush current chunk.
            if (current.Length > 0)
            {
               merged.Add(current.ToString());
               current.Clear();
               currentTokens = 0;
            }

            // If a single part exceeds maxTokens, split it further by sentences.
            if (partTokens > maxTokens)
            {
               var subParts = SplitBySeparators(part, SentSeps);
               foreach (var sp in subParts)
               {
                  var spTokens = TokenCounter.Default.EstimateTokens(sp);
                  if (currentTokens + spTokens <= maxTokens)
                  {
                     if (current.Length > 0) current.Append(' ');
                     current.Append(sp);
                     currentTokens += spTokens;
                  }
                  else
                  {
                     if (current.Length > 0)
                     {
                        merged.Add(current.ToString());
                        current.Clear();
                        currentTokens = 0;
                     }

                     // If even a single sentence exceeds maxTokens, add it as-is.
                     current.Append(sp);
                     currentTokens = spTokens;
                     merged.Add(current.ToString());
                     current.Clear();
                     currentTokens = 0;
                  }
               }
            }
            else
            {
               current.Append(part);
               currentTokens = partTokens;
            }
         }
      }

      if (current.Length > 0)
         merged.Add(current.ToString());

      return merged;
   }

   /// <summary>
   ///    Finds the best sentence/paragraph break position between <paramref name="start" /> and <paramref name="end" />.
   ///    Searches backwards from <paramref name="end" /> for a natural boundary.
   /// </summary>
   private static int FindBestBreak(string text, int start, int end)
   {
      var minBreakPos = start + (end - start) / 2; // don't break in the first half

      // Try paragraph breaks first.
      foreach (var sep in ParaSeps)
      {
         var idx = text.LastIndexOf(sep, end, end - start, StringComparison.Ordinal);
         if (idx >= minBreakPos)
            return idx + sep.Length;
      }

      // Then sentence breaks.
      foreach (var sep in SentSeps)
      {
         var idx = text.LastIndexOf(sep, end, end - start, StringComparison.Ordinal);
         if (idx >= minBreakPos)
            return idx + sep.Length;
      }

      // Fallback: break at a space.
      var spaceIdx = text.LastIndexOf(' ', end, end - start);
      if (spaceIdx >= minBreakPos)
         return spaceIdx + 1;

      return end; // no good break found
   }

   private static DocumentChunk MakeChunk(int index, string content, string sectionTitle, int start, int end)
   {
      return new DocumentChunk
      {
         Index = index,
         Content = content,
         EstimatedTokens = TokenCounter.Default.EstimateTokens(content),
         SectionTitle = sectionTitle,
         StartChar = start,
         EndChar = end
      };
   }
}
