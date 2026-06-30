namespace Delibera.Core.Knowledge;

/// <summary>
///    A single document to ingest into a <see cref="MarkdownKnowledgeBase"/>.
///    Wraps the markdown body together with a stable display name and
///    optional metadata (used to tag the document so the council can
///    distinguish "contract" from "discovery context" inside the KB).
/// </summary>
public sealed record KnowledgeDocument(
   string Name,
   string Content,
   IReadOnlyDictionary<string, string>? Metadata = null);