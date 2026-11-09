using System.Globalization;

namespace Iskender.Domain.Registry;

/// <summary>The lexical forms the contract gives integers, numbers, durations, and URIs. No regular expression is used.</summary>
internal static class ScalarSyntax
{
    /// <summary>The largest whole number of milliseconds a <see cref="TimeSpan"/> holds, the bound past which a duration is out of range.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMilliseconds(MaxDurationMilliseconds);

    private const long MaxDurationMilliseconds = 922_337_203_685_477;
    public enum DurationResult
    {
        Valid,
        Invalid,
        TooLarge,
    }

    /// <summary><c>-?(0|[1-9][0-9]*)</c>.</summary>
    public static bool IsInteger(string text)
    {
        var digits = text.StartsWith('-') ? text[1..] : text;
        return IsUnsignedInteger(digits);
    }

    /// <summary>The JSON number grammar: <c>-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?</c>.</summary>
    public static bool IsNumber(string text)
    {
        var index = 0;
        if (index < text.Length && text[index] == '-')
        {
            index++;
        }

        var start = index;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
        }

        if (!IsUnsignedInteger(text[start..index]))
        {
            return false;
        }

        if (index < text.Length && text[index] == '.')
        {
            var fraction = ++index;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index++;
            }

            if (index == fraction)
            {
                return false;
            }
        }

        if (index < text.Length && text[index] is 'e' or 'E')
        {
            index++;
            if (index < text.Length && text[index] is '+' or '-')
            {
                index++;
            }

            var exponent = index;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index++;
            }

            if (index == exponent)
            {
                return false;
            }
        }

        return index == text.Length;
    }

    /// <summary>The decimal a number names, or null when it is outside decimal's range or would be rounded.</summary>
    public static decimal? ExactDecimal(string text)
    {
        // Zero is in range and needs no rounding whatever its exponent, which decimal.TryParse and Significand refuse when large.
        var e = text.IndexOfAny(['e', 'E']);
        if (text.AsSpan(0, e < 0 ? text.Length : e).TrimStart('-').IndexOfAnyExcept('0', '.') < 0)
        {
            return 0m;
        }

        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return Significand(text) is { } written && written == Significand(FieldRules.Canonical(value)) ? value : null;
    }

    /// <summary>A non-negative integer written <c>0|[1-9][0-9]*</c> and a unit, <c>ms</c>, <c>s</c>, <c>m</c>, <c>h</c>, or <c>d</c>.</summary>
    public static DurationResult Duration(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var unitStart = 0;
        while (unitStart < text.Length && char.IsAsciiDigit(text[unitStart]))
        {
            unitStart++;
        }

        long? milliseconds = text[unitStart..] switch
        {
            "ms" => 1,
            "s" => 1_000,
            "m" => 60_000,
            "h" => 3_600_000,
            "d" => 86_400_000,
            _ => null,
        };
        var digits = text[..unitStart];
        if (milliseconds is not { } unit || !IsUnsignedInteger(digits))
        {
            return DurationResult.Invalid;
        }

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            || count > MaxDurationMilliseconds / unit)
        {
            return DurationResult.TooLarge;
        }

        duration = TimeSpan.FromMilliseconds(count * unit);
        return DurationResult.Valid;
    }

    /// <summary>Starts with a URI scheme and a colon, so a bare path is never taken as a file URI.</summary>
    public static bool StartsWithScheme(string text)
    {
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && char.IsAsciiLetter(text[0]) && Uri.CheckSchemeName(text[..colon]);
    }

    /// <summary>
    /// Written in RFC 3986's URI syntax with a scheme: only unreserved and reserved characters and well-formed percent
    /// escapes, at most one <c>#</c>, and <c>[</c> and <c>]</c> only in the authority, so a backslash or a space is never
    /// accepted because <see cref="Uri"/> would repair it.
    /// </summary>
    public static bool IsUriSyntax(string text)
    {
        if (!StartsWithScheme(text))
        {
            return false;
        }

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var authorityEnd = 0;
        if (text.AsSpan(colon + 1).StartsWith("//", StringComparison.Ordinal))
        {
            var end = text.IndexOfAny(['/', '?', '#'], colon + 3);
            authorityEnd = end < 0 ? text.Length : end;
        }

        var fragment = false;
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (c == '%')
            {
                if (index + 2 >= text.Length || !char.IsAsciiHexDigit(text[index + 1]) || !char.IsAsciiHexDigit(text[index + 2]))
                {
                    return false;
                }

                index += 2;
            }
            else if (c == '#')
            {
                if (fragment)
                {
                    return false;
                }

                fragment = true;
            }
            else if (c is '[' or ']')
            {
                if (index >= authorityEnd)
                {
                    return false;
                }
            }
            else if (!char.IsAsciiLetterOrDigit(c) && !"-._~:/?@!$&'()*+,;=".Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The scheme as written, before the first colon. Call only on text that <see cref="StartsWithScheme"/> accepts.</summary>
    public static string WrittenScheme(string text) => text[..text.IndexOf(':', StringComparison.Ordinal)];

    private static bool IsUnsignedInteger(string digits) =>
        digits.Length > 0 && digits.All(char.IsAsciiDigit) && (digits.Length == 1 || digits[0] != '0');

    /// <summary>A number's significant digits and power of ten, so two spellings of one value compare equal.</summary>
    private static (bool Negative, string Digits, long Exponent)? Significand(string text)
    {
        var negative = text.StartsWith('-');
        if (negative)
        {
            text = text[1..];
        }

        long exponent = 0;
        var e = text.IndexOfAny(['e', 'E']);
        if (e >= 0)
        {
            if (!long.TryParse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)
                || Math.Abs(exponent) > 1_000_000)
            {
                return null;
            }

            text = text[..e];
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0)
        {
            exponent -= text.Length - dot - 1;
            text = text.Remove(dot, 1);
        }

        var digits = text.TrimStart('0');
        var trimmed = digits.TrimEnd('0');
        exponent += digits.Length - trimmed.Length;
        return trimmed.Length == 0 ? (false, "0", 0) : (negative, trimmed, exponent);
    }
}
