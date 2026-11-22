using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Identity;

/// <summary>
/// The installation state: <c>uninitialized</c> or <c>initialized</c> (operator-security contract, Installation state).
/// <see cref="InitializedAt"/> is set exactly when <see cref="IsInitialized"/>. Owned by ISK-0010.
/// </summary>
public sealed record InstallationStatus
{
    private InstallationStatus(bool isInitialized, DateTimeOffset? initializedAt)
    {
        IsInitialized = isInitialized;
        InitializedAt = initializedAt;
    }

    /// <summary>No installation row: no operator has been bootstrapped.</summary>
    public static InstallationStatus Uninitialized { get; } = new(isInitialized: false, initializedAt: null);

    public bool IsInitialized { get; }

    /// <summary>When the bootstrap redemption committed, by the database's clock, or <c>null</c>.</summary>
    public DateTimeOffset? InitializedAt { get; }

    /// <summary>The installation row exists, written at <paramref name="at"/>. A default time fails with <c>core.value.invalid</c>.</summary>
    public static InstallationStatus Initialized(DateTimeOffset at) =>
        at == default ? throw new GatewayException(ErrorCodes.ValueInvalid) : new(isInitialized: true, at);
}
