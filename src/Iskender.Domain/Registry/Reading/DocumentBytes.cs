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

    // Utf8JsonReader counts only line feeds as line breaks, so its line numbers are mapped back through these.
    private readonly int[] _lineFeedStarts;

    private DocumentBytes(string source, ConfigurationLayer layer, byte[] content)
    {
        Source = source;
        Layer = layer;
        Content = content;
        var starts = new List<int> { 0 };
        var lineFeedStarts = new List<int> { 0 };
        for (var index = 0; index < content.Length; index++)
        {
            // A line feed, a carriage return, and the pair together each end one line, so every location is right (FR-013, FR-019).
            if (content[index] == (byte)'\n')
            {
                starts.Add(index + 1);
                lineFeedStarts.Add(index + 1);
            }
            else if (content[index] == (byte)'\r' && (index + 1 == content.Length || content[index + 1] != (byte)'\n'))
            {
                starts.Add(index + 1);
            }
        }

        _lineStarts = [.. starts];
        _lineFeedStarts = [.. lineFeedStarts];
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

    /// <summary>The location of a 0-based line and a byte position within it, as <c>Utf8JsonReader</c> reports them, counting line feeds only.</summary>
    public ConfigurationSourceLocation AtLine(long line, long bytePositionInLine) =>
        At(line < _lineFeedStarts.Length ? _lineFeedStarts[line] + bytePositionInLine : Content.Length);

    public DocumentError Error(ErrorCode code, long offset) => new(code, At(offset));

    /// <summary>
    /// Checks a key or string after escapes are decoded: a control character other than tab, line feed, or carriage
    /// return, or a lone surrogate, is malformed, and more than 65,536 UTF-8 bytes is too large. A lone surrogate has no
    /// UTF-8 form, so it would otherwise be validated and hashed as U+FFFD, and two texts would share a revision (FR-028).
    /// </summary>
    public static ErrorCode? CheckDecoded(string text) =>
        FieldRules.HasForbiddenControl(text) || FieldRules.HasLoneSurrogate(text) ? ErrorCodes.ConfigDocumentMalformed
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
