using System.Text;
using System.Text.Json;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>
/// Reads RFC 8259 JSON into the canonical tree with <see cref="Utf8JsonReader"/>, which never builds a whole document
/// and reports every token's byte offset (research R2). It stops at the first document error.
/// </summary>
internal static class JsonDocumentReader
{
    public static DocumentRead Read(ConfigurationDocumentSource source, ConfigurationLayer layer)
    {
        var bytes = DocumentBytes.Open(source, layer, out var error);
        if (error is not null)
        {
            return new DocumentRead(null, error);
        }

        if (bytes.Content.All(b => b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r'))
        {
            return new DocumentRead(new ObjectNode(bytes.At(0)), null);
        }

        try
        {
            return new DocumentRead(Parse(bytes), null);
        }
        catch (DocumentErrorException stop)
        {
            return new DocumentRead(null, stop.Error);
        }
    }

    private static ObjectNode? Parse(DocumentBytes bytes)
    {
        var reader = new Utf8JsonReader(bytes.Content, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = ConfigurationLimits.MaxDepth + 1,
        });

        var stack = new Stack<CanonicalNode>();
        CanonicalNode? root = null;
        string? key = null;
        ConfigurationSourceLocation? keyLocation = null;
        var nodes = 0;

        void Stop(ErrorCode code, long offset) => throw new DocumentErrorException(bytes.Error(code, offset));

        void Attach(CanonicalNode node, long offset)
        {
            if (++nodes > ConfigurationLimits.MaxNodes)
            {
                Stop(ErrorCodes.ConfigDocumentTooLarge, offset);
            }

            if (stack.Count == 0)
            {
                root = node;
            }
            else if (stack.Peek() is ObjectNode parent)
            {
                parent.TryAdd(new ObjectMember(key!, keyLocation!, node));
            }
            else
            {
                ((ArrayNode)stack.Peek()).Items.Add(node);
            }
        }

        string Decode(ref Utf8JsonReader current)
        {
            var offset = current.TokenStartIndex;
            string text;
            try
            {
                text = current.GetString()!;
            }
            catch (InvalidOperationException)
            {
                throw new DocumentErrorException(bytes.Error(ErrorCodes.ConfigDocumentMalformed, offset));
            }

            if (DocumentBytes.CheckDecoded(text) is { } code)
            {
                Stop(code, offset);
            }

            return text;
        }

        try
        {
            while (reader.Read())
            {
                var offset = reader.TokenStartIndex;
                var location = bytes.At(offset);
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        key = Decode(ref reader);
                        keyLocation = location;
                        if (((ObjectNode)stack.Peek()).Find(key) is not null)
                        {
                            Stop(ErrorCodes.ConfigDocumentDuplicateKey, offset);
                        }

                        break;
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        if (stack.Count + 1 > ConfigurationLimits.MaxDepth)
                        {
                            Stop(ErrorCodes.ConfigDocumentTooLarge, offset);
                        }

                        CanonicalNode container = reader.TokenType == JsonTokenType.StartObject ? new ObjectNode(location) : new ArrayNode(location);
                        Attach(container, offset);
                        stack.Push(container);
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        stack.Pop();
                        break;
                    case JsonTokenType.String:
                        Attach(new ScalarNode(location, Decode(ref reader), ScalarKind.String), offset);
                        break;
                    case JsonTokenType.Number:
                        Attach(new ScalarNode(location, Encoding.UTF8.GetString(reader.ValueSpan), ScalarKind.Number), offset);
                        break;
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                        Attach(new ScalarNode(location, reader.TokenType == JsonTokenType.True ? "true" : "false", ScalarKind.Boolean), offset);
                        break;
                    case JsonTokenType.Null:
                        Attach(new ScalarNode(location, "null", ScalarKind.Null), offset);
                        break;
                    default:
                        Stop(ErrorCodes.ConfigDocumentMalformed, offset);
                        break;
                }
            }
        }
        catch (JsonException malformed)
        {
            throw new DocumentErrorException(new DocumentError(
                ErrorCodes.ConfigDocumentMalformed,
                bytes.AtLine(malformed.LineNumber ?? 0, malformed.BytePositionInLine ?? 0)));
        }

        return root switch
        {
            ObjectNode tree => tree,
            ScalarNode { Kind: ScalarKind.Null } => new ObjectNode(root.Location),
            _ => throw new DocumentErrorException(new DocumentError(ErrorCodes.ConfigDocumentMalformed, root!.Location)),
        };
    }
}
