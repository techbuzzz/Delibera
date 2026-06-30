using Delibera.Core.Knowledge;
using FluentAssertions;

namespace Delibera.Core.Tests;

public class MarkdownKnowledgeBaseTests
{
   // ── Case 1: LoadTextAsync(string, string) + Search ────────────────────────
   [Fact]
   public async Task LoadTextAsync_String_Then_Search_Returns_Source()
   {
      var kb = new MarkdownKnowledgeBase();
      await kb.LoadTextAsync("hello world from the council", "src");

      var results = kb.Search("hello", 1);
      results.Should().NotBeEmpty();
      results[0].Should().Contain("[src]");
      results[0].Should().Contain("hello");
   }

   // ── Case 2: LoadTextAsync(KnowledgeDocument) preserves metadata ──────────
   [Fact]
   public async Task LoadTextAsync_Document_Stores_Metadata()
   {
      var kb = new MarkdownKnowledgeBase();
      var doc = new KnowledgeDocument(
         "src",
         "hello world from the contract",
         new Dictionary<string, string> { ["kind"] = "contract" });

      await kb.LoadTextAsync(doc);

      kb.DocumentMetadata.Should().ContainKey("src");
      kb.DocumentMetadata["src"]!["kind"].Should().Be("contract");

      var results = kb.Search("hello", 1);
      results.Should().NotBeEmpty();
      results[0].Should().Contain("[src]");
   }

   // ── Case 3: LoadTextsAsync bulk ingest ───────────────────────────────────
   [Fact]
   public async Task LoadTextsAsync_Indexes_All_Documents()
   {
      var kb = new MarkdownKnowledgeBase();
      var docs = new[]
      {
         new KnowledgeDocument("doc1", "alpha bravo charlie"),
         new KnowledgeDocument("doc2", "delta echo foxtrot"),
         new KnowledgeDocument("doc3", "golf hotel india"),
      };

      await kb.LoadTextsAsync(docs);

      kb.DocumentCount.Should().Be(3);
      kb.GetLoadedSources().Should().BeEquivalentTo(new[] { "doc1", "doc2", "doc3" });
   }

   // ── Case 4: file-path LoadAsync regression ───────────────────────────────
   [Fact]
   public async Task LoadAsync_From_File_Still_Works()
   {
      var tempDir = Path.Combine(Path.GetTempPath(), "delibera_kb_tests_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);
      var path = Path.Combine(tempDir, "regression.md");
      await File.WriteAllTextAsync(path, "# Title\n\nhello markdown body\n\nsecond paragraph here");

      try
      {
         var kb = new MarkdownKnowledgeBase();
         await kb.LoadAsync(path);

         kb.DocumentCount.Should().Be(1);
         kb.GetLoadedSources()[0].Should().Be("regression.md");

         var results = kb.Search("hello", 5);
         results.Should().NotBeEmpty();
         results[0].Should().Contain("[regression.md]");
         results[0].Should().Contain("hello markdown body");
      }
      finally
      {
         if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
      }
   }

   // ── Case 5a: argument validation on string overload ─────────────────────
   [Theory]
   [InlineData("", "src")]
   [InlineData("   ", "src")]
   public async Task LoadTextAsync_String_Empty_Content_Throws_ArgumentException(string content, string sourceName)
   {
      var kb = new MarkdownKnowledgeBase();
      var act = () => kb.LoadTextAsync(content, sourceName);
      await act.Should().ThrowAsync<ArgumentException>();
   }

   [Theory]
   [InlineData("hello", "")]
   [InlineData("hello", "   ")]
   public async Task LoadTextAsync_String_Empty_SourceName_Throws_ArgumentException(string content, string sourceName)
   {
      var kb = new MarkdownKnowledgeBase();
      var act = () => kb.LoadTextAsync(content, sourceName);
      await act.Should().ThrowAsync<ArgumentException>();
   }

   [Fact]
   public async Task LoadTextAsync_String_Null_Content_Throws_ArgumentNullException()
   {
      var kb = new MarkdownKnowledgeBase();
      var act = () => kb.LoadTextAsync(null!, "src");
      await act.Should().ThrowAsync<ArgumentNullException>();
   }

   [Fact]
   public async Task LoadTextAsync_String_Null_SourceName_Throws_ArgumentNullException()
   {
      var kb = new MarkdownKnowledgeBase();
      var act = () => kb.LoadTextAsync("hello", null!);
      await act.Should().ThrowAsync<ArgumentNullException>();
   }

   // ── Case 5b: argument validation on document overload ───────────────────
   [Fact]
   public async Task LoadTextAsync_Document_Null_Throws_ArgumentNullException()
   {
      var kb = new MarkdownKnowledgeBase();
      var act = () => kb.LoadTextAsync(null!);
      await act.Should().ThrowAsync<ArgumentNullException>();
   }

   [Fact]
   public async Task LoadTextAsync_Document_Empty_Name_Throws_ArgumentException()
   {
      var kb = new MarkdownKnowledgeBase();
      var doc = new KnowledgeDocument("", "hello");
      var act = () => kb.LoadTextAsync(doc);
      await act.Should().ThrowAsync<ArgumentException>();
   }

   [Fact]
   public async Task LoadTextAsync_Document_Empty_Content_Throws_ArgumentException()
   {
      var kb = new MarkdownKnowledgeBase();
      var doc = new KnowledgeDocument("src", "   ");
      var act = () => kb.LoadTextAsync(doc);
      await act.Should().ThrowAsync<ArgumentException>();
   }

   // ── Case 5c: argument validation on LoadTextsAsync ──────────────────────
   [Fact]
   public async Task LoadTextsAsync_Null_Throws_ArgumentNullException()
   {
      var kb = new MarkdownKnowledgeBase();
      var act = () => kb.LoadTextsAsync(null!);
      await act.Should().ThrowAsync<ArgumentNullException>();
   }

   [Fact]
   public async Task LoadTextsAsync_Empty_Collection_Is_NoOp()
   {
      var kb = new MarkdownKnowledgeBase();
      await kb.LoadTextsAsync([]);
      kb.DocumentCount.Should().Be(0);
   }

   // ── Case 6: CancellationToken is honored ─────────────────────────────────
   [Fact]
   public async Task LoadTextsAsync_Honors_Cancellation_Between_Documents()
   {
      var kb = new MarkdownKnowledgeBase();
      using var cts = new CancellationTokenSource();
      var docs = new List<KnowledgeDocument>
      {
         new("doc1", "alpha"),
         new("doc2", "beta"),
         new("doc3", "gamma"),
      };

      // Cancel before the second document lands.
      int loaded = 0;
      var enumerable = docs.Select(d =>
      {
         if (loaded == 1) cts.Cancel();
         loaded++;
         return d;
      });

      var act = () => kb.LoadTextsAsync(enumerable, cts.Token);
      await act.Should().ThrowAsync<OperationCanceledException>();
   }

   [Fact]
   public async Task LoadTextAsync_String_Honors_Pre_Canceled_Token()
   {
      var kb = new MarkdownKnowledgeBase();
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      var act = () => kb.LoadTextAsync("hello", "src", cts.Token);
      await act.Should().ThrowAsync<OperationCanceledException>();
   }

   [Fact]
   public async Task LoadTextAsync_Document_Honors_Pre_Canceled_Token()
   {
      var kb = new MarkdownKnowledgeBase();
      using var cts = new CancellationTokenSource();
      cts.Cancel();
      var doc = new KnowledgeDocument("src", "hello");

      var act = () => kb.LoadTextAsync(doc, cts.Token);
      await act.Should().ThrowAsync<OperationCanceledException>();
   }

   // ── Case 6b: file-path API honors CT ─────────────────────────────────────
   [Fact]
   public async Task LoadAsync_File_Honors_Pre_Canceled_Token()
   {
      var kb = new MarkdownKnowledgeBase();
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      var act = () => kb.LoadAsync("any-file.md", cts.Token);
      await act.Should().ThrowAsync<OperationCanceledException>();
   }

   [Fact]
   public async Task LoadManyAsync_Honors_Cancellation_Between_Files()
   {
      var tempDir = Path.Combine(Path.GetTempPath(), "delibera_kb_cancel_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);
      try
      {
         var paths = Enumerable.Range(0, 5)
            .Select(i => Path.Combine(tempDir, $"f{i}.md"))
            .ToArray();
         foreach (var p in paths)
            await File.WriteAllTextAsync(p, $"# Doc {Path.GetFileName(p)}");

         var kb = new MarkdownKnowledgeBase();
         using var cts = new CancellationTokenSource();

         // Cancel after the second file lands.
         int loaded = 0;
         var enumerable = paths.Select(p =>
         {
            if (loaded == 1) cts.Cancel();
            loaded++;
            return p;
         });

         var act = () => kb.LoadManyAsync(enumerable, cts.Token);
         await act.Should().ThrowAsync<OperationCanceledException>();
         kb.DocumentCount.Should().BeLessThan(5);
      }
      finally
      {
         if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
      }
   }

   [Fact]
   public async Task LoadDirectoryAsync_Forwards_Cancellation()
   {
      var tempDir = Path.Combine(Path.GetTempPath(), "delibera_kb_dir_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);
      try
      {
         await File.WriteAllTextAsync(Path.Combine(tempDir, "a.md"), "alpha");
         await File.WriteAllTextAsync(Path.Combine(tempDir, "b.md"), "beta");

         var kb = new MarkdownKnowledgeBase();
         using var cts = new CancellationTokenSource();
         cts.Cancel();

         var act = () => kb.LoadDirectoryAsync(tempDir, "*.md", cts.Token);
         await act.Should().ThrowAsync<OperationCanceledException>();
      }
      finally
      {
         if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
      }
   }

   // ── Case 7: idempotency — re-loading same source name overwrites cleanly ─
   [Fact]
   public async Task LoadTextAsync_Same_Source_Name_Overwrites_Previous_Content()
   {
      var kb = new MarkdownKnowledgeBase();
      await kb.LoadTextAsync("first content", "src");
      await kb.LoadTextAsync("second content", "src");

      kb.DocumentCount.Should().Be(1);
      var results = kb.Search("second", 5);
      results.Should().NotBeEmpty();
      results[0].Should().Contain("second content");
   }
}