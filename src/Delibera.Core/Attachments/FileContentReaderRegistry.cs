using Delibera.Core.Attachments.Readers;

namespace Delibera.Core.Attachments;

/// <summary>
///    Per-extension registry of <see cref="IFileContentReader"/> instances.
///    Owned by <see cref="Council.CouncilBuilder"/> and consulted by
///    <see cref="Council.CouncilExecutor"/> when attachments are processed.
/// </summary>
/// <remarks>
///    <para>
///       Pre-registers three built-in readers covering the common cases with zero
///       external dependencies:
///    </para>
///    <list type="bullet">
///       <item><see cref="PlainTextFileReader"/> — <c>.txt .md .json .xml .cs .yml .csv .html</c></item>
///       <item><see cref="ImageFileReader"/> — <c>.png .jpg .jpeg .webp .gif</c></item>
///       <item><see cref="FallbackFileReader"/> — any unregistered extension (graceful placeholder)</item>
///    </list>
///    <para>
///       Call <see cref="Register(string, IFileContentReader)"/> to add a custom reader
///       for a specific extension (e.g. <c>.pdf</c>). The registry always returns a
///       reader — <see cref="FallbackFileReader"/> for unknown extensions — so callers
///       never need to null-check.
///    </para>
/// </remarks>
public sealed class FileContentReaderRegistry
{
   private readonly Dictionary<string, IFileContentReader> _readers = new(StringComparer.OrdinalIgnoreCase);

   /// <summary>
   ///    Creates a registry pre-populated with the built-in readers
   ///    (<see cref="PlainTextFileReader"/>, <see cref="ImageFileReader"/>).
   /// </summary>
   public FileContentReaderRegistry()
   {
      Register(new PlainTextFileReader());
      Register(new ImageFileReader());
   }

   /// <summary>
   ///    Registers a reader for all of its <see cref="IFileContentReader.SupportedExtensions"/>.
   ///    Overwrites any existing reader for the same extension.
   /// </summary>
   /// <param name="reader">Reader instance whose <see cref="IFileContentReader.SupportedExtensions"/>
   ///    determine which extensions it handles.</param>
   public void Register(IFileContentReader reader)
   {
      ArgumentNullException.ThrowIfNull(reader);
      foreach (var ext in reader.SupportedExtensions)
         _readers[ext] = reader;
   }

   /// <summary>
   ///    Registers a reader for a specific extension. Overwrites any existing reader.
   /// </summary>
   /// <param name="extension">
   ///    File extension including the leading dot (e.g. <c>".pdf"</c>). Case-insensitive.
   /// </param>
   /// <param name="reader">Reader instance.</param>
   public void Register(string extension, IFileContentReader reader)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(extension);
      ArgumentNullException.ThrowIfNull(reader);
      _readers[NormalizeExtension(extension)] = reader;
   }

   /// <summary>
   ///    Registers a delegate-based reader for a specific extension. The delegate is
   ///    wrapped in a <see cref="DelegateFileContentReader"/> adapter.
   /// </summary>
   /// <param name="extension">File extension including the leading dot (e.g. <c>".pdf"</c>).</param>
   /// <param name="handler">
   ///    Delegate that reads the file and returns a <see cref="FileReadResult"/>.
   /// </param>
   public void Register(string extension, Func<string, CancellationToken, Task<FileReadResult>> handler)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(extension);
      ArgumentNullException.ThrowIfNull(handler);
      _readers[NormalizeExtension(extension)] = new DelegateFileContentReader(extension, handler);
   }

   /// <summary>
   ///    Returns the reader for the file at <paramref name="filePath"/>.
   ///    Always returns a reader — <see cref="FallbackFileReader"/> when the extension
   ///    is not registered. Never throws.
   /// </summary>
   /// <param name="filePath">File path — the extension is extracted from it.</param>
   /// <returns>The reader for the file's extension, or <see cref="FallbackFileReader"/>.</returns>
   public IFileContentReader GetReader(string filePath)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
      var ext = Path.GetExtension(filePath);
      if (string.IsNullOrEmpty(ext))
         return FallbackFileReader.Instance;
      return _readers.TryGetValue(ext, out var reader)
         ? reader
         : FallbackFileReader.Instance;
   }

   /// <summary>
   ///    Returns <c>true</c> when a reader is registered for the given extension.
   /// </summary>
   /// <param name="extension">File extension including the leading dot.</param>
   /// <returns><c>true</c> if registered; <c>false</c> otherwise.</returns>
   public bool IsRegistered(string extension)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(extension);
      return _readers.ContainsKey(NormalizeExtension(extension));
   }

   private static string NormalizeExtension(string extension)
   {
      var ext = extension.Trim();
      if (!ext.StartsWith(".", StringComparison.Ordinal))
         ext = "." + ext;
      return ext.ToLowerInvariant();
   }
}
