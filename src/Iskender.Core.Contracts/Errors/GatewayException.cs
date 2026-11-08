using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>How a contract type fails: always with a registered <see cref="ErrorCode"/> (FR-042).</summary>
[ContractVersion("core-contracts", 1)]
public sealed class GatewayException : Exception
{
    public GatewayException(ErrorCode code)
        : this(code, detail: null, innerException: null)
    {
    }

    internal GatewayException(ErrorCode code, string? detail, Exception? innerException = null)
        : base(FormatMessage(code, detail), innerException)
    {
        Code = code;
    }

    /// <summary>The registered code the failure was raised with. Callers and tests branch on this, never on the message.</summary>
    public ErrorCode Code { get; }

    private static string FormatMessage(ErrorCode code, string? detail) =>
        detail is null ? $"{code.Id}: {code.SafeMessage}" : $"{code.Id}: {code.SafeMessage} {detail}";
}
