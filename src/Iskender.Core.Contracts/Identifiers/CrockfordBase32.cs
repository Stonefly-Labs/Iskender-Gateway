namespace Iskender.Core.Contracts.Identifiers;

/// <summary>
/// Lowercase Crockford base32 for 128-bit values: 26 characters, whose 130 bits start with two zero pad bits, so the
/// first character is <c>0</c> to <c>7</c> (research R6). Each value has exactly one canonical text form.
/// </summary>
internal static class CrockfordBase32
{
    internal const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
    internal const int ByteCount = 16;
    internal const int Length = 26;

    internal enum BodyCheck
    {
        Canonical,
        NonCanonical,
    }

    internal static string Encode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteCount)
        {
            throw new ArgumentException($"Expected {ByteCount} bytes.", nameof(bytes));
        }

        UInt128 value = 0;
        foreach (var b in bytes)
        {
            value = (value << 8) | b;
        }

        return string.Create(Length, value, static (chars, v) =>
        {
            for (var i = 0; i < Length; i++)
            {
                var shift = 5 * (Length - 1 - i);
                chars[i] = Alphabet[(int)((v >> shift) & 31)];
            }
        });
    }

    internal static BodyCheck Check(ReadOnlySpan<char> body)
    {
        if (body.Length != Length || body[0] is < '0' or > '7')
        {
            return BodyCheck.NonCanonical;
        }

        foreach (var c in body)
        {
            if (Alphabet.IndexOf(c, StringComparison.Ordinal) < 0)
            {
                return BodyCheck.NonCanonical;
            }
        }

        return BodyCheck.Canonical;
    }

    /// <summary>Decodes a canonical body. The caller has already checked it with <see cref="Check"/>.</summary>
    internal static byte[] Decode(ReadOnlySpan<char> body)
    {
        if (Check(body) != BodyCheck.Canonical)
        {
            throw new ArgumentException("The body is not canonical.", nameof(body));
        }

        UInt128 value = 0;
        foreach (var c in body)
        {
            value = (value << 5) | (uint)Alphabet.IndexOf(c, StringComparison.Ordinal);
        }

        var bytes = new byte[ByteCount];
        for (var i = ByteCount - 1; i >= 0; i--)
        {
            bytes[i] = (byte)(value & 0xFF);
            value >>= 8;
        }

        return bytes;
    }
}
