namespace Delibera.Core.Attachments;

/// <summary>
///    DI entry that pairs a file extension with a factory for an
///    <see cref="IFileContentReader"/>. Registered by
///    <c>ServiceCollectionExtensions.AddFileReader</c> so a DI-resolved
///    <see cref="Council.CouncilBuilder"/> can pick up custom readers automatically.
/// </summary>
public sealed record FileReaderDIEntry(
    string Extension,
    Func<IServiceProvider, IFileContentReader> ReaderFactory);