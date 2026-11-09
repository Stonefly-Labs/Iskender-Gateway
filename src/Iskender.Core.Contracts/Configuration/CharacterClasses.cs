using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>The ASCII character classes a <see cref="CharacterClassPattern"/> can allow.</summary>
[Flags]
[ContractVersion("core-contracts", 2)]
public enum CharacterClasses
{
    /// <summary>No class.</summary>
    None = 0,

    /// <summary><c>a-z</c>.</summary>
    LowerCaseLetters = 1,

    /// <summary><c>A-Z</c>.</summary>
    UpperCaseLetters = 2,

    /// <summary><c>0-9</c>.</summary>
    Digits = 4,
}
