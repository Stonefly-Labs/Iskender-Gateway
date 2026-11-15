using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry.Composition;

/// <summary>
/// A configuration document a composition root reads when it validates: a file, text, or a stream factory, with a source
/// name and a declared format. Nothing is opened, called, or read when the document is described (hosting contract,
/// Registration rule 5). Owned by ISK-0007.
/// </summary>
public sealed class CompositionDocument
{
    private const int MaxSourceNameLength = 256;

    private CompositionDocument(string sourceName, ConfigurationDocumentFormat format, string? path, string? text, Func<Stream>? open)
    {
        if (!IsSourceName(sourceName) || !Enum.IsDefined(format))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        SourceName = sourceName;
        Format = format;
        FilePath = path;
        Text = text;
        Open = open;
    }

    /// <summary>The name used in every location: 1 to 256 characters, with no control character, U+2028, or U+2029.</summary>
    public string SourceName { get; }

    /// <summary>The declared format.</summary>
    public ConfigurationDocumentFormat Format { get; }

    /// <summary>The file to read, for a document from <see cref="FromFile"/>.</summary>
    internal string? FilePath { get; }

    /// <summary>The text, for a document from <see cref="FromText"/>.</summary>
    internal string? Text { get; }

    /// <summary>The stream factory, for a document from <see cref="FromStream"/>.</summary>
    internal Func<Stream>? Open { get; }

    /// <summary>A file, read when the configuration is validated. The source name is the file name unless one is given.</summary>
    public static CompositionDocument FromFile(string path, ConfigurationDocumentFormat format, string? sourceName = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        return new(sourceName ?? Path.GetFileName(path), format, path, null, null);
    }

    /// <summary>A document given as text.</summary>
    public static CompositionDocument FromText(string sourceName, ConfigurationDocumentFormat format, string text)
    {
        if (text is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        return new(sourceName, format, null, text, null);
    }

    /// <summary>A stream factory, called once by each container that validates. The stream is disposed after it is read.</summary>
    public static CompositionDocument FromStream(string sourceName, ConfigurationDocumentFormat format, Func<Stream> open)
    {
        if (open is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        return new(sourceName, format, null, null, open);
    }

    private static bool IsSourceName(string? name) =>
        name is { Length: >= 1 and <= MaxSourceNameLength } && !name.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029');
}
