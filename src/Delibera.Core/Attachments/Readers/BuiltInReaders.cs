using System.Text;

namespace Delibera.Core.Attachments.Readers;

/// <summary>
///    Built-in reader for plain-text file formats: <c>.txt .md .json .xml .cs .yml .csv .html</c>.
///    Zero external dependencies — uses in-box <see cref="File.ReadAllTextAsync(string, CancellationToken)"/>.
/// </summary>
public sealed class PlainTextFileReader : IFileContentReader
{
   /// <inheritdoc />
   public IReadOnlyCollection<string> SupportedExtensions { get; } =
   [
      ".txt", ".md", ".markdown", ".json", ".xml", ".cs",
      ".yml", ".yaml", ".csv", ".html", ".htm", ".log", ".tsv"
   ];

   /// <inheritdoc />
   public async Task<FileReadResult> ReadAsync(string filePath, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

      if (!File.Exists(filePath))
         return new FileReadResult(filePath,
            TextContent: $"[Attachment: {Path.GetFileName(filePath)} — file not found]",
            BinaryParts: null, Metadata: null);

      try
      {
         var text = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct).ConfigureAwait(false);
         var meta = new Dictionary<string, string>
         {
            ["sizeBytes"] = new FileInfo(filePath).Length.ToString(),
            ["extension"] = Path.GetExtension(filePath).ToLowerInvariant()
         };
         return new FileReadResult(filePath, text, BinaryParts: null, meta);
      }
      catch (Exception ex)
      {
         return new FileReadResult(filePath,
            TextContent: $"[Attachment: {Path.GetFileName(filePath)} — read error: {ex.Message}]",
            BinaryParts: null, Metadata: null);
      }
   }
}

/// <summary>
///    Built-in reader for image files: <c>.png .jpg .jpeg .webp .gif</c>.
///    Returns the raw bytes as a <see cref="BinaryAttachment"/> with the correct
///    MIME type, ready for vision models via
///    <see cref="Microsoft.Extensions.AI"/> <c>ImageContent</c>. Zero external dependencies.
/// </summary>
public sealed class ImageFileReader : IFileContentReader
{
   private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
   {
      [".png"] = "image/png",
      [".jpg"] = "image/jpeg",
      [".jpeg"] = "image/jpeg",
      [".webp"] = "image/webp",
      [".gif"] = "image/gif",
      [".bmp"] = "image/bmp"
   };

   /// <inheritdoc />
   public IReadOnlyCollection<string> SupportedExtensions { get; } = [.. MimeTypes.Keys];

   /// <inheritdoc />
   public async Task<FileReadResult> ReadAsync(string filePath, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

      if (!File.Exists(filePath))
         return new FileReadResult(filePath,
            TextContent: $"[Attachment: {Path.GetFileName(filePath)} — file not found]",
            BinaryParts: null, Metadata: null);

      try
      {
         var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
         var ext = Path.GetExtension(filePath).ToLowerInvariant();
         var mediaType = MimeTypes.TryGetValue(ext, out var mt)
            ? mt
            : "application/octet-stream";
         var attachment = new BinaryAttachment(Path.GetFileName(filePath), mediaType, bytes);
         var meta = new Dictionary<string, string>
         {
            ["sizeBytes"] = bytes.Length.ToString(),
            ["mediaType"] = mediaType,
            ["extension"] = ext
         };
         return new FileReadResult(filePath, TextContent: null, BinaryParts: [attachment], meta);
      }
      catch (Exception ex)
      {
         return new FileReadResult(filePath,
            TextContent: $"[Attachment: {Path.GetFileName(filePath)} — read error: {ex.Message}]",
            BinaryParts: null, Metadata: null);
      }
   }

   /// <summary>
   ///    Returns the MIME type for a given image file extension, or
   ///    <c>"application/octet-stream"</c> if unknown.
   /// </summary>
   public static string GetMediaType(string extension)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(extension);
      var ext = extension.StartsWith('.')
         ? extension.ToLowerInvariant()
         : "." + extension.ToLowerInvariant();
      return MimeTypes.TryGetValue(ext, out var mt)
         ? mt
         : "application/octet-stream";
   }
}

/// <summary>
///    Built-in fallback reader used when no reader is registered for a file extension.
///    Returns a clearly-worded placeholder string — never throws, never returns binary parts.
/// </summary>
public sealed class FallbackFileReader : IFileContentReader
{
   /// <summary>
   ///    Singleton instance — stateless, safe to share.
   /// </summary>
   public static FallbackFileReader Instance { get; } = new();

   private FallbackFileReader()
   {
   }

   /// <inheritdoc />
   public IReadOnlyCollection<string> SupportedExtensions { get; } = []; // matches nothing — used directly

   /// <inheritdoc />
   public Task<FileReadResult> ReadAsync(string filePath, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
      var ext = Path.GetExtension(filePath);
      var name = Path.GetFileName(filePath);
      var text = $"""
                  [Attachment: {name}]
                  No reader is registered for extension '{ext}'.
                  Register one with: .WithFileReader("{ext}", yourReader)
                  """;
      return Task.FromResult(new FileReadResult(filePath, text, BinaryParts: null, Metadata: null));
   }
}

/// <summary>
///    Adapter that wraps a delegate as an <see cref="IFileContentReader"/>.
///    Used by <see cref="FileContentReaderRegistry.Register(string, Func{string, CancellationToken, Task{FileReadResult}})"/>
///    so callers can register a lambda without writing a class.
/// </summary>
public sealed class DelegateFileContentReader : IFileContentReader
{
   private readonly Func<string, CancellationToken, Task<FileReadResult>> _handler;

   /// <summary>
   ///    Creates a delegate-based reader handling a single extension.
   /// </summary>
   /// <param name="extension">The extension this reader handles (including the leading dot).</param>
   /// <param name="handler">Delegate that reads the file and returns a <see cref="FileReadResult"/>.</param>
   public DelegateFileContentReader(string extension, Func<string, CancellationToken, Task<FileReadResult>> handler)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(extension);
      ArgumentNullException.ThrowIfNull(handler);
      SupportedExtensions = [extension.ToLowerInvariant()];
      _handler = handler;
   }

   /// <inheritdoc />
   public IReadOnlyCollection<string> SupportedExtensions { get; }

   /// <inheritdoc />
   public Task<FileReadResult> ReadAsync(string filePath, CancellationToken ct = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
      return _handler(filePath, ct);
   }
}
