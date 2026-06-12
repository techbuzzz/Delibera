namespace Delibera.Core.Models;

/// <summary>
///    Immutable context that feeds into a debate session.
/// </summary>
public sealed record PromptContext
{
   /// <summary>System prompt (role / context shared by all models).</summary>
   public string SystemPrompt { get; init; } = string.Empty;

   /// <summary>User prompt (the main question or task).</summary>
   public string UserPrompt { get; init; } = string.Empty;

   /// <summary>Pre-loaded knowledge content (merged text from files or RAG).</summary>
   public string? KnowledgeContent { get; init; }

   /// <summary>Paths / identifiers of knowledge sources.</summary>
   public IReadOnlyList<string> KnowledgeFiles { get; init; } = [];

   /// <summary>Arbitrary key-value metadata.</summary>
   public IReadOnlyDictionary<string, string> Metadata { get; init; } =
      new Dictionary<string, string>();

   /// <summary>
   ///    Builds the full user prompt, injecting knowledge context when available.
   /// </summary>
   public string GetFullUserPrompt()
   {
      if (string.IsNullOrWhiteSpace(KnowledgeContent))
         return UserPrompt;

      return $"""
              ### Context (Knowledge Base):
              {KnowledgeContent}

              ### Question:
              {UserPrompt}
              """;
   }
}