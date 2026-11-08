namespace Iskender.Domain.Registry;

/// <summary>RFC 4648 base32 with the lower-case alphabet <c>a-z2-7</c> and no padding.</summary>
internal static class Base32Lower
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var text = new char[(data.Length * 8 + 4) / 5];
        var buffer = 0;
        var bits = 0;
        var at = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                text[at++] = Alphabet[(buffer >> (bits - 5)) & 31];
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            text[at++] = Alphabet[(buffer << (5 - bits)) & 31];
        }

        return new string(text, 0, at);
    }
}
