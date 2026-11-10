using System.Security.Cryptography;
using System.Text;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Migrations;

/// <summary>
/// One migration's forward and reverse scripts (contract Migrations rules 1 and 3, research R11): the texts decoded as
/// UTF-8 with any BOM removed and CRLF or CR changed to LF, the checksum over both, and whether the reverse is
/// data-losing. Owned by ISK-0004.
/// </summary>
public sealed class MigrationScript
{
    /// <summary>The first line of a data-losing reverse script.</summary>
    public const string DataLosingMarker = "-- iskender:data-losing";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private MigrationScript(MigrationIdentity identity, string forward, string reverse)
    {
        Identity = identity;
        Forward = forward;
        Reverse = reverse;
        DataLosing = reverse.Split('\n', 2)[0] == DataLosingMarker;
        Checksum = Hash(forward, reverse);
    }

    public MigrationIdentity Identity { get; }

    public string Forward { get; }

    public string Reverse { get; }

    /// <summary>Lowercase hex SHA-256 over the forward text, one <c>0x00</c> byte, and the reverse text.</summary>
    public string Checksum { get; }

    public bool DataLosing { get; }

    /// <summary>False when either script is not valid UTF-8.</summary>
    internal static bool TryCreate(MigrationIdentity identity, ReadOnlySpan<byte> forward, ReadOnlySpan<byte> reverse, out MigrationScript script)
    {
        script = null!;
        if (!TryNormalize(forward, out var forwardText) || !TryNormalize(reverse, out var reverseText))
        {
            return false;
        }

        script = new MigrationScript(identity, forwardText, reverseText);
        return true;
    }

    /// <summary>Decodes strict UTF-8, removes a leading BOM, and changes CRLF and lone CR to LF. Nothing else changes.</summary>
    internal static bool TryNormalize(ReadOnlySpan<byte> content, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            text = "";
            return false;
        }

        if (text.StartsWith('﻿'))
        {
            text = text[1..];
        }

        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return true;
    }

    private static string Hash(string forward, string reverse)
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes(forward), 0, .. Encoding.UTF8.GetBytes(reverse)];
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
