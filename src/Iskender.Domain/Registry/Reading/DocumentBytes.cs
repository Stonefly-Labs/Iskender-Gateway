using System.Buffers;
using System.Text;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>
/// A bounded document buffer with its line table, and the checks both readers run before parsing: size, byte order
/// mark, UTF-8, and control characters (research R4).
/// </summary>
internal sealed class DocumentBytes
{
    private readonly int[] _lineStarts;

    private DocumentBytes(string source, ConfigurationLayer layer, byte[] content)
    {
        Source = source;
        Layer = layer;
        Content = content;
        var starts = new List<int> { 0 };
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] == (byte)'\n')
            {
                starts.Add(index + 1);
            }
        }

        _lineStarts = [.. starts];
    }

    public string Source { get; }

    public ConfigurationLayer Layer { get; }

    public byte[] Content { get; }

    /// <summary>Wraps a source's bytes and runs the checks, returning the first document error, or null.</summary>
    public static DocumentBytes Open(ConfigurationDocumentSource source, ConfigurationLayer layer, out DocumentError? error)
    {
        var bytes = new DocumentBytes(source.Name, layer, source.Content);
        error = bytes.Check();
        return bytes;
    }

    /// <summary>The location of a byte offset: 1-based line, and 1-based column in Unicode scalar values.</summary>
    public ConfigurationSourceLocation At(long offset)
    {
        offset = Math.Clamp(offset, 0, Content.Length);
        var line = Array.BinarySearch(_lineStarts, (int)offset);
        if (line < 0)
        {
            line = ~line - 1;
        }

        var column = 1;
        for (var index = _lineStarts[line]; index < offset; index++)
        {
            if ((Content[index] & 0xC0) != 0x80)
            {
                column++;
            }
        }

        return new ConfigurationSourceLocation(Source, line + 1, column, Layer);
    }

    /// <summary>The location of a 0-based line and a byte position within it, as <c>Utf8JsonReader</c> reports them.</summary>
    public ConfigurationSourceLocation AtLine(long line, long bytePositionInLine) =>
        At(line < _lineStarts.Length ? _lineStarts[line] + bytePositionInLine : Content.Length);

    public DocumentError Error(ErrorCode code, long offset) => new(code, At(offset));

    /// <summary>
    /// Checks a key or string after escapes are decoded: a control character other than tab, line feed, or carriage
    /// return is malformed, and more than 65,536 UTF-8 bytes is too large.
    /// </summary>
    public static ErrorCode? CheckDecoded(string text) =>
        FieldRules.HasForbiddenControl(text) ? ErrorCodes.ConfigDocumentMalformed
            : FieldRules.TooLong(text) ? ErrorCodes.ConfigDocumentTooLarge
            : null;

    private DocumentError? Check()
    {
        if (Content.Length > ConfigurationLimits.MaxDocumentBytes)
        {
            return Error(ErrorCodes.ConfigDocumentTooLarge, ConfigurationLimits.MaxDocumentBytes);
        }

        if (Content.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Error(ErrorCodes.ConfigDocumentMalformed, 0);
        }

        var span = Content.AsSpan();
        var offset = 0;
        while (offset < span.Length)
        {
            var status = Rune.DecodeFromUtf8(span[offset..], out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                return Error(ErrorCodes.ConfigDocumentMalformed, offset);
            }

            if (Rune.IsControl(rune) && rune.Value is not ('\t' or '\n' or '\r'))
            {
                return Error(ErrorCodes.ConfigDocumentMalformed, offset);
            }

            offset += consumed;
        }

        return null;
    }
}
