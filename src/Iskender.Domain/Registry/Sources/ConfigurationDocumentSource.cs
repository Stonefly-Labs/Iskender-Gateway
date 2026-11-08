using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>
/// A named configuration document in a declared format. The stream and file forms read at most the size limit plus one
/// byte, so an oversized source is never read past the byte that proves it too large (research R4).
/// </summary>
public sealed class ConfigurationDocumentSource
{
    private const int MaxNameLength = 256;

    private ConfigurationDocumentSource(string name, ConfigurationDocumentFormat format, byte[] content)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength || name.Any(char.IsControl) || !Enum.IsDefined(format))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Name = name;
        Format = format;
        Content = content;
    }

    /// <summary>The name the host gave the source, used in every location: 1 to 256 characters, with no control characters.</summary>
    public string Name { get; }

    /// <summary>The declared format.</summary>
    public ConfigurationDocumentFormat Format { get; }

    /// <summary>At most the size limit plus one byte of the document.</summary>
    internal byte[] Content { get; }

    public static ConfigurationDocumentSource FromBytes(string name, ConfigurationDocumentFormat format, ReadOnlySpan<byte> content) =>
        new(name, format, content[..Math.Min(content.Length, ConfigurationLimits.MaxDocumentBytes + 1)].ToArray());

    /// <summary>A document given as text, encoded as UTF-8 with no byte order mark.</summary>
    public static ConfigurationDocumentSource FromString(string name, ConfigurationDocumentFormat format, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return FromBytes(name, format, System.Text.Encoding.UTF8.GetBytes(content));
    }

    /// <summary>Reads at most the size limit plus one byte from <paramref name="content"/>. The stream is not disposed.</summary>
    public static ConfigurationDocumentSource FromStream(string name, ConfigurationDocumentFormat format, Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(name, format, ReadBounded(content));
    }

    /// <summary>Reads at most the size limit plus one byte of a file. The source name is the file name unless one is given.</summary>
    public static ConfigurationDocumentSource FromFile(string path, ConfigurationDocumentFormat format, string? name = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);
        return new(name ?? Path.GetFileName(path), format, ReadBounded(stream));
    }

    private static byte[] ReadBounded(Stream stream)
    {
        var buffer = new byte[ConfigurationLimits.MaxDocumentBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
        {
            total += read;
        }

        return buffer[..total];
    }
}
