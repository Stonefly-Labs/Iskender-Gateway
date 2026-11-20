using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>The bounds the work port types share (work contract, Work items and Configuration). Owned by ISK-0009.</summary>
internal static partial class WorkLimits
{
    public const int LowestPayloadVersion = 1;

    public const int HighestPayloadVersion = 1_000;

    public const int MaximumCursorLength = 512;

    public static readonly TimeSpan MaximumDelay = TimeSpan.FromDays(30);

    public static int RequirePayloadVersion(int version) =>
        version is >= LowestPayloadVersion and <= HighestPayloadVersion ? version : throw new GatewayException(ErrorCodes.ValueInvalid);

    public static string RequireIdempotencyKey(string? key) =>
        key is not null && IdempotencyKeyPattern().IsMatch(key) ? key : throw new GatewayException(ErrorCodes.ValueInvalid);

    [GeneratedRegex(@"^[A-Za-z0-9._:\-]{1,128}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyKeyPattern();
}
