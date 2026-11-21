using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// A producing module's statement that it enqueues <see cref="Kind"/>, with payload versions 1 to
/// <see cref="HighestPayloadVersion"/>. The outbox refuses any other kind or version with <c>work.kind_undeclared</c>
/// (work contract, Registration rule 3). Owned by ISK-0009.
/// </summary>
public sealed record WorkKindDeclaration
{
    /// <summary>A <c>null</c> kind, or a highest payload version outside 1 to 1,000, fails with <c>core.value.invalid</c>.</summary>
    public WorkKindDeclaration(WorkKind kind, int highestPayloadVersion)
    {
        Kind = kind ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        HighestPayloadVersion = WorkLimits.RequirePayloadVersion(highestPayloadVersion);
    }

    public WorkKind Kind { get; }

    public int HighestPayloadVersion { get; }
}
