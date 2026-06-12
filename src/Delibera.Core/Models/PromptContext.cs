namespace Delibera.Core.Models;

/// <summary>
///    Immutable context that feeds into a debate session.
/// </summary>
/// <param name="SystemPrompt">System prompt (role / context shared by all models).</param>
/// <param name="UserPrompt">User prompt (the main question or task).</param>
/// <param name="KnowledgeContent">Pre-loaded knowledge content (merged text from files or RAG).</param>
/// <param name="KnowledgeFiles">Paths / identifiers of knowledge sources.</param>
/// <param name="Metadata">Arbitrary key-value metadata.</param>
public sealed record PromptContext(
   string SystemPrompt = "",
   string UserPrompt = "",
   string? KnowledgeContent = null,
   IReadOnlyList<string> KnowledgeFiles = null!,
   IReadOnlyDictionary<string, string> Metadata = null!)
{
   /// <summary>
   ///    Creates an empty <see cref="PromptContext" /> with default collections.
   /// </summary>
   public PromptContext() : this(string.Empty, string.Empty, null, [], new Dictionary<string, string>())
   {
   }

   /// <summary>
   ///    Builds the full user prompt, injecting knowledge context when available.
   /// </summary>
   public string GetFullUserPrompt()
   {
      if (string.IsNullOrWhiteSpace(KnowledgeContent))
         return UserPrompt;

      return $$"""
              ### Context (Knowledge Base):
              {{KnowledgeContent}}

              ### Question:
              {{UserPrompt}}
              """;
   }
}
