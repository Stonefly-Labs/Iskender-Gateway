using System.Text;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Iskender.Domain.Registry;

/// <summary>
/// Reads the YAML subset into the canonical tree from <see cref="Parser"/> events alone, never the deserializer or the
/// representation model (research R1). Every banned construct is rejected at its first event, before anything is
/// expanded, and reading stops at the first document error.
/// </summary>
internal static class YamlDocumentReader
{
    private const string MergeKey = "<<";

    public static DocumentRead Read(ConfigurationDocumentSource source, ConfigurationLayer layer)
    {
        var bytes = DocumentBytes.Open(source, layer, out var error);
        if (error is not null)
        {
            return new DocumentRead(null, error);
        }

        var text = Encoding.UTF8.GetString(bytes.Content);
        var offsets = new ByteOffsets(text);
        var separators = Separators.For(text);
        try
        {
            return new DocumentRead(Parse(bytes, separators.Hide(text), offsets, separators), null);
        }
        catch (DocumentErrorException stop)
        {
            return new DocumentRead(null, stop.Error);
        }
        catch (YamlException malformed)
        {
            // The parser's message can quote the input, so only its mark is kept.
            return new DocumentRead(null, bytes.Error(ErrorCodes.ConfigDocumentMalformed, offsets.Of(malformed.Start)));
        }
    }

    private static ObjectNode Parse(DocumentBytes bytes, string text, ByteOffsets offsets, Separators separators)
    {
        var parser = new Parser(new StringReader(text));
        var stack = new Stack<CanonicalNode>();
        CanonicalNode? root = null;
        (string Text, ConfigurationSourceLocation Location)? key = null;
        var documents = 0;
        var nodes = 0;

        void Stop(ErrorCode code, Mark mark) => throw new DocumentErrorException(bytes.Error(code, offsets.Of(mark)));

        void Attach(CanonicalNode node, Mark mark)
        {
            if (++nodes > ConfigurationLimits.MaxNodes)
            {
                Stop(ErrorCodes.ConfigDocumentTooLarge, mark);
            }

            if (stack.Count == 0)
            {
                root = node;
            }
            else if (stack.Peek() is ObjectNode parent)
            {
                parent.TryAdd(new ObjectMember(key!.Value.Text, key.Value.Location, node));
                key = null;
            }
            else
            {
                ((ArrayNode)stack.Peek()).Items.Add(node);
            }
        }

        while (parser.MoveNext())
        {
            var current = parser.Current!;
            var mark = current.Start;
            if (current is NodeEvent node && (!node.Anchor.IsEmpty || !node.Tag.IsEmpty))
            {
                Stop(ErrorCodes.ConfigDocumentUnsupportedConstruct, mark);
            }

            switch (current)
            {
                case DocumentStart start:
                    if (++documents > 1 || start.Version is not null || start.Tags?.Any(tag => !IsDefault(tag)) == true)
                    {
                        Stop(ErrorCodes.ConfigDocumentUnsupportedConstruct, mark);
                    }

                    break;
                case AnchorAlias:
                    Stop(ErrorCodes.ConfigDocumentUnsupportedConstruct, mark);
                    break;
                case Scalar scalar when stack.Count > 0 && stack.Peek() is ObjectNode parent && key is null:
                    if (scalar.Style == ScalarStyle.Plain && scalar.Value == MergeKey)
                    {
                        Stop(ErrorCodes.ConfigDocumentUnsupportedConstruct, mark);
                    }

                    var name = separators.Restore(scalar.Value);
                    Check(name, mark);
                    if (parent.Find(name) is not null)
                    {
                        Stop(ErrorCodes.ConfigDocumentDuplicateKey, mark);
                    }

                    key = (name, bytes.At(offsets.Of(mark)));
                    break;
                case Scalar scalar:
                    var value = separators.Restore(scalar.Value);
                    Check(value, mark);
                    Attach(new ScalarNode(bytes.At(offsets.Of(mark)), value, KindOf(scalar.Style, value)), mark);
                    break;
                case MappingStart or SequenceStart:
                    if (stack.Count > 0 && stack.Peek() is ObjectNode && key is null)
                    {
                        // A key that is itself a mapping or a sequence.
                        Stop(ErrorCodes.ConfigDocumentMalformed, mark);
                    }

                    if (stack.Count + 1 > ConfigurationLimits.MaxDepth)
                    {
                        Stop(ErrorCodes.ConfigDocumentTooLarge, mark);
                    }

                    var location = bytes.At(offsets.Of(mark));
                    CanonicalNode container = current is MappingStart ? new ObjectNode(location) : new ArrayNode(location);
                    Attach(container, mark);
                    stack.Push(container);
                    break;
                case MappingEnd or SequenceEnd:
                    stack.Pop();
                    break;
            }
        }

        return root switch
        {
            null => new ObjectNode(bytes.At(0)),
            ObjectNode tree => tree,
            ScalarNode { Kind: ScalarKind.Null } => new ObjectNode(root.Location),
            _ => throw new DocumentErrorException(new DocumentError(ErrorCodes.ConfigDocumentMalformed, root.Location)),
        };

        void Check(string value, Mark at)
        {
            if (DocumentBytes.CheckDecoded(value) is { } code)
            {
                Stop(code, at);
            }
        }
    }

    /// <summary>The parser lists YAML's two default tag handles on every document, so only another directive came from a <c>%TAG</c> line.</summary>
    private static bool IsDefault(YamlDotNet.Core.Tokens.TagDirective tag) =>
        (tag.Handle, tag.Prefix) is ("!", "!") or ("!!", "tag:yaml.org,2002:");

    /// <summary>Quoted and block scalars are strings; an unquoted scalar is plain, or null when it is <c>~</c>, <c>null</c>, or empty (research R3).</summary>
    private static ScalarKind KindOf(ScalarStyle style, string value) => style switch
    {
        ScalarStyle.Plain when value is "" or "~" or "null" => ScalarKind.Null,
        ScalarStyle.Plain => ScalarKind.Plain,
        _ => ScalarKind.String,
    };

    /// <summary>
    /// The parser takes U+2028 and U+2029 as line breaks, as YAML 1.1 does, but JSON and the line table in
    /// <see cref="DocumentBytes"/> do not. So each is handed to the parser as a private-use character, which the parser
    /// treats as ordinary text, and restored in every key and value, so the YAML and JSON forms of the same content agree
    /// (FR-010, FR-011). Both are one UTF-16 code unit, so marks do not move. The text may hold the stand-ins itself, as
    /// characters or escapes, so it is also scanned with a second pair: the two parses differ only where a separator stood,
    /// and every character that agrees is kept as written. No text is ever parsed with its separators as line breaks.
    /// </summary>
    private sealed class Separators
    {
        private const char LineSeparator = '\u2028';
        private const char ParagraphSeparator = '\u2029';
        private const char Line = '\uE000';
        private const char Paragraph = '\uE001';
        private const char OtherLine = '\uE002';
        private const char OtherParagraph = '\uE003';

        // The scalars of the second parse, in event order. Null when the text holds no separator.
        private readonly Queue<string>? _others;

        private Separators(Queue<string>? others)
        {
            _others = others;
        }

        public static Separators For(string text)
        {
            if (!text.Contains(LineSeparator, StringComparison.Ordinal) && !text.Contains(ParagraphSeparator, StringComparison.Ordinal))
            {
                return new(null);
            }

            // Only events are read, so nothing is expanded. A parse error stops this scan where the first parse stops too.
            var others = new Queue<string>();
            var parser = new Parser(new StringReader(Hide(text, OtherLine, OtherParagraph)));
            try
            {
                while (parser.MoveNext())
                {
                    if (parser.Current is Scalar scalar)
                    {
                        others.Enqueue(scalar.Value);
                    }
                }
            }
            catch (YamlException)
            {
            }

            return new(others);
        }

        public string Hide(string text) => _others is null ? text : Hide(text, Line, Paragraph);

        /// <summary>Restores the separators in the next scalar. Call once for every scalar event, in order.</summary>
        public string Restore(string value)
        {
            if (_others is null || !_others.TryDequeue(out var other) || other.Length != value.Length)
            {
                return value;
            }

            return string.Create(value.Length, (value, other), static (span, pair) =>
            {
                for (var index = 0; index < span.Length; index++)
                {
                    var c = pair.value[index];
                    span[index] = c == pair.other[index] ? c : c == Line ? LineSeparator : ParagraphSeparator;
                }
            });
        }

        private static string Hide(string text, char line, char paragraph) =>
            text.Replace(LineSeparator, line).Replace(ParagraphSeparator, paragraph);
    }

    /// <summary>
    /// Turns a parser mark, which counts UTF-16 code units, into a UTF-8 byte offset. Marks arrive almost always in
    /// order, so a forward cursor keeps this linear without a table.
    /// </summary>
    private sealed class ByteOffsets(string text)
    {
        private int _chars;
        private long _bytes;

        public long Of(Mark mark)
        {
            var target = (int)Math.Clamp(mark.Index, 0, text.Length);
            if (target < _chars)
            {
                (_chars, _bytes) = (0, 0);
            }

            _bytes += Encoding.UTF8.GetByteCount(text.AsSpan(_chars, target - _chars));
            _chars = target;
            return _bytes;
        }
    }
}
