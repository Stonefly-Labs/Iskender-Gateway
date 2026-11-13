using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// How many processes share coordination state (TR-6). <see cref="MultiReplica"/> when not configured, so a forgotten
/// setting fails closed. Owned by ISK-0005.
/// </summary>
public enum CoordinationTopology
{
    SingleInstance,
    MultiReplica,
}

/// <summary>The snake-case code of each topology. Owned by ISK-0005.</summary>
public static class CoordinationTopologies
{
    public static string ToCode(this CoordinationTopology topology) => topology switch
    {
        CoordinationTopology.SingleInstance => "single_instance",
        CoordinationTopology.MultiReplica => "multi_replica",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };
}
