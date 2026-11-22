namespace Iskender.Domain.Telemetry;

/// <summary>
/// An audit record's reason class: <see cref="None"/> exactly when the result is <c>succeeded</c>, otherwise one of the
/// audited redemption reasons (operator-security contract, Redemption rule 3). Owned by ISK-0010.
/// </summary>
public enum AuditReason
{
    /// <summary>Code <c>none</c>.</summary>
    None,

    /// <summary>Code <c>wrong_secret</c>.</summary>
    WrongSecret,

    /// <summary>Code <c>wrong_purpose</c>.</summary>
    WrongPurpose,

    /// <summary>Code <c>consumed</c>.</summary>
    Consumed,

    /// <summary>Code <c>superseded</c>.</summary>
    Superseded,

    /// <summary>Code <c>expired</c>.</summary>
    Expired,

    /// <summary>Code <c>already_initialized</c>.</summary>
    AlreadyInitialized,

    /// <summary>Code <c>not_initialized</c>.</summary>
    NotInitialized,
}
