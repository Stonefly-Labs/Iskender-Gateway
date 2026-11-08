using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>
/// The type a section schema declares for a field (FR-007). <see cref="Text"/>, <see cref="WholeNumber"/>, and
/// <see cref="Section"/> are the contract's <c>string</c>, <c>integer</c>, and <c>object</c> types.
/// </summary>
[ContractVersion("core-contracts", 2)]
public enum ConfigurationFieldKind
{
    /// <summary>A string: text, with an optional length and allowed-character pattern.</summary>
    Text = 0,

    /// <summary>An integer: a 64-bit signed whole number, with an optional inclusive range.</summary>
    WholeNumber = 1,

    /// <summary>A <see cref="decimal"/>, with an optional inclusive range.</summary>
    Number = 2,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean = 3,

    /// <summary>One of the declared members, compared case-sensitively.</summary>
    Enumeration = 4,

    /// <summary>A non-negative integer and a unit, <c>ms</c>, <c>s</c>, <c>m</c>, <c>h</c>, or <c>d</c>, such as <c>30s</c>.</summary>
    Duration = 5,

    /// <summary>An absolute URI, with optional allowed schemes.</summary>
    Uri = 6,

    /// <summary>An object of declared fields, read as a nested section. Always present, so it cannot be required or have a default.</summary>
    Section = 7,

    /// <summary>Items of one declared type, which is not itself an array.</summary>
    Array = 8,

    /// <summary>A secret reference placeholder, <c>{ secretRef: &lt;name&gt; }</c>.</summary>
    SecretReference = 9,
}
