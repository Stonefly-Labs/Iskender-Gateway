using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>Applies every pending migration. Owned by ISK-0004.</summary>
public sealed record MigrationApplyRequest;

/// <summary>
/// Reverses the latest <see cref="Steps"/> applied migrations. Without <see cref="ConfirmDataLoss"/>, a data-losing step
/// anywhere in the request refuses the whole request before any step runs. Owned by ISK-0004.
/// </summary>
public sealed record MigrationReverseRequest
{
    /// <summary>How many migrations to reverse. Positive.</summary>
    public int Steps
    {
        get;
        init => field = value > 0 ? value : throw new GatewayException(ErrorCodes.ValueInvalid);
    } = 1;

    /// <summary>Confirms that data-losing reverse steps may run.</summary>
    public bool ConfirmDataLoss { get; init; }
}
