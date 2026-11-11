using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>The capability check's result reason: accepted, or one of the 5 refusal reasons of TR-7. Owned by ISK-0005.</summary>
public enum CapabilityReason
{
    Accepted,
    UnsupportedConsistency,
    CoordinatorNotConfigured,
    LocalInMultiReplica,
    DegradeNotPermitted,
    SingleInstanceViolated,
}

/// <summary>The snake-case code of each reason, safe to show on a dashboard. Owned by ISK-0005.</summary>
public static class CapabilityReasonCodes
{
    public static string ToCode(this CapabilityReason reason) => reason switch
    {
        CapabilityReason.Accepted => "accepted",
        CapabilityReason.UnsupportedConsistency => "unsupported_consistency",
        CapabilityReason.CoordinatorNotConfigured => "coordinator_not_configured",
        CapabilityReason.LocalInMultiReplica => "local_in_multi_replica",
        CapabilityReason.DegradeNotPermitted => "degrade_not_permitted",
        CapabilityReason.SingleInstanceViolated => "single_instance_violated",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };
}
