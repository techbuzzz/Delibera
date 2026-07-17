namespace Delibera.Core.Attachments;

/// <summary>
///    Reads the content of a file and returns a normalised <see cref="FileReadResult"/>.
///    Register implementations per file extension via
///    <see cref="Council.CouncilBuilder.WithFileReader(string, IFileContentReader)"/>.
/// </summary>
/// <remarks>
///    <para>
///       This is the single extensibility point for document parsing in Delibera.Core.
///       The core package ships <see cref="Readers.PlainTextFileReader"/>,
///       <see cref="Readers.ImageFileReader"/>, and <see cref="Readers.FallbackFileReader"/>
///       as built-ins (zero external dependencies). Format-specific readers
///       (PDF, DOCX, …) are provided by separate optional adapter packages
///       such as <c>Delibera.Adapters.PdfPig</c>.
///    </para>
/// </remarks>
public interface IFileContentReader
{
   /// <summary>
   ///    File extensions this reader handles, including the leading dot and
   ///    both lower- and upper-case variants (e.g. <c>[".pdf", ".PDF"]</c>).
   /// </summary>
   IReadOnlyCollection<string> SupportedExtensions { get; }

   /// <summary>
   ///    Reads the file at <paramref name="filePath"/> and returns a normalised
   ///    <see cref="FileReadResult"/>. Implementations must handle missing files
   ///    gracefully (return a result with an error message in
   ///    <see cref="FileReadResult.TextContent"/> rather than throwing).
   /// </summary>
   /// <param name="filePath">Absolute or relative path to the file.</param>
   /// <param name="ct">Cancellation token.</param>
   /// <returns>Normalised read result.</returns>
   Task<FileReadResult> ReadAsync(string filePath, CancellationToken ct = default);
}

/// <summary>
///    Normalised output of an <see cref="IFileContentReader"/>.
///    <see cref="TextContent"/> is used for text-only models and the AutoChunking
///    pipeline; <see cref="BinaryParts"/> is used for vision-capable models via
///    <see cref="Microsoft.Extensions.AI"/> <c>ImageContent</c>. Both can be non-null
///    (e.g. a PDF with embedded images).
/// </summary>
/// <param name="SourcePath">Original file path the result was read from.</param>
/// <param name="TextContent">
///    Extracted text content. <c>null</c> when the reader could not extract text
///    (e.g. an image-only file). Used for text-only models and AutoChunking.
/// </param>
/// <param name="BinaryParts">
///    Raw binary parts (images, screenshots) suitable for vision models. <c>null</c>
///    when the file has no binary content (e.g. plain text).
/// </param>
/// <param name="Metadata">
///    Optional metadata keyed by tag name (e.g. <c>{"pages": "12", "author": "…"}</c>).
///    <c>null</c> when no metadata was extracted.
/// </param>
public sealed record FileReadResult(
   string SourcePath,
   string? TextContent,
   IReadOnlyList<BinaryAttachment>? BinaryParts,
   IReadOnlyDictionary<string, string>? Metadata);

/// <summary>
///    A raw binary part — image bytes ready for vision-model consumption via
///    <see cref="Microsoft.Extensions.AI"/> <c>ImageContent</c>.
/// </summary>
/// <param name="Name">Human-readable name (e.g. "architecture-diagram.png").</param>
/// <param name="MediaType">MIME type (e.g. "image/png", "image/jpeg", "image/webp").</param>
/// <param name="Content">Raw binary content.</param>
public sealed record BinaryAttachment(
   string Name,
   string MediaType,
   byte[] Content);

/// <summary>
///    A file attached to a council debate. The file is read lazily via the
///    <see cref="FileContentReaderRegistry"/> when the debate starts, so the
///    attachment can be configured before the reader is registered.
/// </summary>
/// <param name="FilePath">Absolute or relative path to the file.</param>
/// <param name="Description">
///    Optional human-readable description (e.g. "System Architecture diagram").
///    Shown to text-only models that cannot process binary attachments.
/// </param>
public sealed record FileAttachment(
   string FilePath,
   string? Description = null);
