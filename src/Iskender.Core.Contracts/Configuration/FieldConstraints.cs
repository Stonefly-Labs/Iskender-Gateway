using System.Collections.Immutable;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Configuration;

/// <summary>
/// The constraints on one field. Each applies only to the field types named on it; the registry rejects one set on
/// another type, and a minimum above its maximum (FR-008). Every <c>With</c> method returns a new instance.
/// </summary>
[ContractVersion("core-contracts", 2)]
public sealed class FieldConstraints
{
    private FieldConstraints()
    {
    }

    private FieldConstraints(FieldConstraints other)
    {
        MinimumInteger = other.MinimumInteger;
        MaximumInteger = other.MaximumInteger;
        MinimumNumber = other.MinimumNumber;
        MaximumNumber = other.MaximumNumber;
        MinimumDuration = other.MinimumDuration;
        MaximumDuration = other.MaximumDuration;
        MinimumLength = other.MinimumLength;
        MaximumLength = other.MaximumLength;
        AllowedCharacters = other.AllowedCharacters;
        Members = other.Members;
        MinimumItems = other.MinimumItems;
        MaximumItems = other.MaximumItems;
        AllowedSchemes = other.AllowedSchemes;
    }

    /// <summary>No constraint.</summary>
    public static FieldConstraints None { get; } = new();

    /// <summary>Inclusive minimum of an integer field.</summary>
    public long? MinimumInteger { get; private init; }

    /// <summary>Inclusive maximum of an integer field.</summary>
    public long? MaximumInteger { get; private init; }

    /// <summary>Inclusive minimum of a number field.</summary>
    public decimal? MinimumNumber { get; private init; }

    /// <summary>Inclusive maximum of a number field.</summary>
    public decimal? MaximumNumber { get; private init; }

    /// <summary>Inclusive minimum of a duration field.</summary>
    public TimeSpan? MinimumDuration { get; private init; }

    /// <summary>Inclusive maximum of a duration field.</summary>
    public TimeSpan? MaximumDuration { get; private init; }

    /// <summary>Minimum length of a string field, in Unicode scalar values.</summary>
    public int? MinimumLength { get; private init; }

    /// <summary>Maximum length of a string field, in Unicode scalar values. Not above 65,536.</summary>
    public int? MaximumLength { get; private init; }

    /// <summary>The characters a string field allows.</summary>
    public CharacterClassPattern? AllowedCharacters { get; private init; }

    /// <summary>The members of an enumeration field: at least one, unique, case-sensitive. Empty when unset.</summary>
    public IReadOnlyList<string> Members { get; private init; } = ImmutableArray<string>.Empty;

    /// <summary>Inclusive minimum item count of an array field.</summary>
    public int? MinimumItems { get; private init; }

    /// <summary>Inclusive maximum item count of an array field.</summary>
    public int? MaximumItems { get; private init; }

    /// <summary>The schemes a URI field allows, compared without regard to case. Empty when any scheme is allowed.</summary>
    public IReadOnlyList<string> AllowedSchemes { get; private init; } = ImmutableArray<string>.Empty;

    public FieldConstraints WithIntegerRange(long? minimum, long? maximum) =>
        new(this) { MinimumInteger = minimum, MaximumInteger = maximum };

    public FieldConstraints WithNumberRange(decimal? minimum, decimal? maximum) =>
        new(this) { MinimumNumber = minimum, MaximumNumber = maximum };

    public FieldConstraints WithDurationRange(TimeSpan? minimum, TimeSpan? maximum) =>
        new(this) { MinimumDuration = minimum, MaximumDuration = maximum };

    public FieldConstraints WithLength(int? minimum, int? maximum) =>
        new(this) { MinimumLength = minimum, MaximumLength = maximum };

    public FieldConstraints WithAllowedCharacters(CharacterClassPattern? pattern) =>
        new(this) { AllowedCharacters = pattern };

    public FieldConstraints WithMembers(params string[] members) =>
        new(this) { Members = ImmutableArray.Create(members ?? []) };

    public FieldConstraints WithItemCount(int? minimum, int? maximum) =>
        new(this) { MinimumItems = minimum, MaximumItems = maximum };

    public FieldConstraints WithAllowedSchemes(params string[] schemes) =>
        new(this) { AllowedSchemes = ImmutableArray.Create(schemes ?? []) };
}
