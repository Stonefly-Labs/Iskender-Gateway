using System.Text;
using Iskender.Core.Contracts.Configuration;

namespace Iskender.Domain.Registry;

/// <summary>The constraint checks shared by the registry, for defaults, and the validator, for values.</summary>
internal static class FieldRules
{
    /// <summary>
    /// The comparison the contract means by "without regard to case": ordinal, folding only ASCII <c>A-Z</c> to
    /// <c>a-z</c>, so the result is the same under every culture.
    /// </summary>
    public static readonly AsciiIgnoreCaseComparer IgnoreAsciiCase = new();

    /// <summary>A string's length in Unicode scalar values.</summary>
    public static int ScalarLength(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    public static bool HasForbiddenControl(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c) && c is not ('\t' or '\n' or '\r'))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TooLong(string value) => Encoding.UTF8.GetByteCount(value) > ConfigurationLimits.MaxStringBytes;

    public static bool LengthAllowed(string value, FieldConstraints constraints)
    {
        var length = ScalarLength(value);
        return (constraints.MinimumLength is not { } min || length >= min) && (constraints.MaximumLength is not { } max || length <= max);
    }

    public static bool InRange(long value, FieldConstraints constraints) =>
        (constraints.MinimumInteger is not { } min || value >= min) && (constraints.MaximumInteger is not { } max || value <= max);

    public static bool InRange(decimal value, FieldConstraints constraints) =>
        (constraints.MinimumNumber is not { } min || value >= min) && (constraints.MaximumNumber is not { } max || value <= max);

    public static bool InRange(TimeSpan value, FieldConstraints constraints) =>
        (constraints.MinimumDuration is not { } min || value >= min) && (constraints.MaximumDuration is not { } max || value <= max);

    public static bool ItemCountAllowed(int count, FieldConstraints constraints) =>
        (constraints.MinimumItems is not { } min || count >= min) && (constraints.MaximumItems is not { } max || count <= max);

    public static bool PatternAllowed(string value, FieldConstraints constraints) =>
        constraints.AllowedCharacters is not { } pattern || pattern.IsMatch(value);

    public static bool MemberAllowed(string value, FieldConstraints constraints) =>
        constraints.Members.Contains(value, StringComparer.Ordinal);

    public static bool SchemeAllowed(Uri value, FieldConstraints constraints) =>
        constraints.AllowedSchemes.Count == 0 || constraints.AllowedSchemes.Contains(value.Scheme, IgnoreAsciiCase);
}

/// <summary>Ordinal comparison that folds only ASCII letters.</summary>
internal sealed class AsciiIgnoreCaseComparer : IEqualityComparer<string>
{
    public bool Equals(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return x is null && y is null;
        }

        if (x.Length != y.Length)
        {
            return false;
        }

        for (var index = 0; index < x.Length; index++)
        {
            if (Fold(x[index]) != Fold(y[index]))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(string obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var hash = new HashCode();
        foreach (var c in obj)
        {
            hash.Add(Fold(c));
        }

        return hash.ToHashCode();
    }

    public static char Fold(char c) => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
}
