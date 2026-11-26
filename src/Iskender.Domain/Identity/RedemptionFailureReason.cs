using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Identity;

/// <summary>
/// Why a redemption failed, in the order the reasons are checked (operator-security contract, Redemption rule 3). The
/// reason never reaches the caller: it appears only in the audit record and in event 9002. Owned by ISK-0010.
/// </summary>
public enum RedemptionFailureReason
{
    /// <summary>Code <c>malformed</c>: the text is not a token. Not audited.</summary>
    Malformed,

    /// <summary>Code <c>unknown</c>: no stored token has the identifier. Not audited.</summary>
    Unknown,

    /// <summary>Code <c>wrong_secret</c>.</summary>
    WrongSecret,

    /// <summary>Code <c>wrong_purpose</c>: the token was presented at the other purpose's route.</summary>
    WrongPurpose,

    /// <summary>Code <c>consumed</c>.</summary>
    Consumed,

    /// <summary>Code <c>superseded</c>.</summary>
    Superseded,

    /// <summary>Code <c>expired</c>, by the database's clock.</summary>
    Expired,

    /// <summary>Code <c>already_initialized</c>: a bootstrap on an initialized installation.</summary>
    AlreadyInitialized,

    /// <summary>Code <c>not_initialized</c>: a recovery on an installation that is not initialized.</summary>
    NotInitialized,
}

/// <summary>The codes of <see cref="RedemptionFailureReason"/>. Owned by ISK-0010.</summary>
public static class RedemptionFailureReasons
{
    public static string ToCode(this RedemptionFailureReason reason) => reason switch
    {
        RedemptionFailureReason.Malformed => "malformed",
        RedemptionFailureReason.Unknown => "unknown",
        RedemptionFailureReason.WrongSecret => "wrong_secret",
        RedemptionFailureReason.WrongPurpose => "wrong_purpose",
        RedemptionFailureReason.Consumed => "consumed",
        RedemptionFailureReason.Superseded => "superseded",
        RedemptionFailureReason.Expired => "expired",
        RedemptionFailureReason.AlreadyInitialized => "already_initialized",
        RedemptionFailureReason.NotInitialized => "not_initialized",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };

    /// <summary>Whether a failure for the reason writes an audit record: every reason after <c>unknown</c> (FR-028).</summary>
    public static bool IsAudited(this RedemptionFailureReason reason) =>
        Enum.IsDefined(reason) && reason > RedemptionFailureReason.Unknown;
}
