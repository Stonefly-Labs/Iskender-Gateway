using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>The four coordination primitives a coordinator declares a level for. Owned by ISK-0005.</summary>
public enum CoordinationPrimitive
{
    AtomicOperations,
    Leases,
    Fencing,
    CacheInvalidation,
}

/// <summary>The snake-case code of each primitive, a bounded log and metric label. Owned by ISK-0005.</summary>
public static class CoordinationPrimitives
{
    /// <summary>The four primitives, in declaration order.</summary>
    public static IReadOnlyList<CoordinationPrimitive> All { get; } = Enum.GetValues<CoordinationPrimitive>();

    public static string ToCode(this CoordinationPrimitive primitive) => primitive switch
    {
        CoordinationPrimitive.AtomicOperations => "atomic_operations",
        CoordinationPrimitive.Leases => "leases",
        CoordinationPrimitive.Fencing => "fencing",
        CoordinationPrimitive.CacheInvalidation => "cache_invalidation",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };
}
