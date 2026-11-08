using Iskender.Core.Contracts.Configuration;

namespace Iskender.Domain.Registry;

/// <summary>How a scalar was written, which the validator uses to type it by its schema field (research R3).</summary>
internal enum ScalarKind
{
    /// <summary>A JSON string, or a quoted YAML scalar.</summary>
    String,

    /// <summary>A JSON number token.</summary>
    Number,

    /// <summary>A JSON <c>true</c> or <c>false</c> token.</summary>
    Boolean,

    /// <summary>JSON <c>null</c>, or an unquoted YAML <c>null</c>, <c>~</c>, or empty value: absent.</summary>
    Null,

    /// <summary>An unquoted YAML scalar, or an environment variable's text, typed only by its field.</summary>
    Plain,
}

/// <summary>A node of the one tree both readers and the environment reader produce (FR-013, research R3).</summary>
internal abstract class CanonicalNode(ConfigurationSourceLocation location)
{
    public ConfigurationSourceLocation Location { get; } = location;
}

/// <summary>A key, where it was written, and its value.</summary>
internal sealed record ObjectMember(string Key, ConfigurationSourceLocation KeyLocation, CanonicalNode Value);

/// <summary>An object: members in the order read, with unique keys.</summary>
internal sealed class ObjectNode(ConfigurationSourceLocation location) : CanonicalNode(location)
{
    private readonly List<ObjectMember> _members = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public IReadOnlyList<ObjectMember> Members => _members;

    /// <summary>Adds a member, or returns false when the key is already present.</summary>
    public bool TryAdd(ObjectMember member)
    {
        if (!_index.TryAdd(member.Key, _members.Count))
        {
            return false;
        }

        _members.Add(member);
        return true;
    }

    /// <summary>Adds a member, or replaces the value of the member with the same key.</summary>
    public void Set(ObjectMember member)
    {
        if (_index.TryGetValue(member.Key, out var at))
        {
            _members[at] = member;
        }
        else
        {
            TryAdd(member);
        }
    }

    public ObjectMember? Find(string key) => _index.TryGetValue(key, out var at) ? _members[at] : null;
}

/// <summary>An array of items in order.</summary>
internal sealed class ArrayNode(ConfigurationSourceLocation location) : CanonicalNode(location)
{
    public List<CanonicalNode> Items { get; } = [];
}

/// <summary>A scalar's exact text and how it was written.</summary>
internal sealed class ScalarNode(ConfigurationSourceLocation location, string text, ScalarKind kind) : CanonicalNode(location)
{
    public string Text { get; } = text;

    public ScalarKind Kind { get; } = kind;
}
