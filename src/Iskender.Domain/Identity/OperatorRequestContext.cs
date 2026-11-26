using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Identity;

/// <summary>
/// What every audited operator operation records: its correlation ID, the configuration revision it runs under, and its
/// actor (operator-security contract, Audit records rule 2). A <c>null</c> value fails with <c>core.value.invalid</c>.
/// Owned by ISK-0010.
/// </summary>
public sealed record OperatorRequestContext
{
    public OperatorRequestContext(RequestCorrelationId correlationId, ConfigurationRevision configurationRevision, OperatorActor actor)
    {
        CorrelationId = correlationId ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        ConfigurationRevision = configurationRevision ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        Actor = actor ?? throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    public RequestCorrelationId CorrelationId { get; }

    public ConfigurationRevision ConfigurationRevision { get; }

    public OperatorActor Actor { get; }
}
