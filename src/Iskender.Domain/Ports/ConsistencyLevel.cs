using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// The guarantee a coordinator gives for one primitive (contract Consistency rule 1, TR-4). <see cref="BestEffort"/> &lt;
/// <see cref="Leased"/> &lt; <see cref="Strict"/>, and <see cref="Local"/> is outside that order. Owned by ISK-0005.
/// </summary>
public enum ConsistencyLevel
{
    /// <summary>Exact within one process only.</summary>
    Local,

    /// <summary>Shared across replicas. An acknowledged update may be lost or applied twice on coordinator restart or failover.</summary>
    BestEffort,

    /// <summary>Shared and atomic per key while the coordinator stays up. On failover, grants and updates may be lost, bounded by expiry.</summary>
    Leased,

    /// <summary>Linearizable per key. No acknowledged update or fencing token is lost or repeated across restart or failover.</summary>
    Strict,
}

/// <summary>The order between levels, and each level's snake-case code. Owned by ISK-0005.</summary>
public static class ConsistencyLevels
{
    /// <summary>
    /// Whether a coordinator declaring <paramref name="declared"/> meets a <paramref name="required"/> minimum. A
    /// <c>local</c> declaration satisfies only a <c>local</c> minimum. Among the distributed levels the order
    /// <c>best_effort &lt; leased &lt; strict</c> applies, and every distributed level satisfies a <c>local</c> minimum,
    /// which only a <c>single_instance</c> feature may declare (decided by the maintainer in <c>implement</c> on 2026-11-11).
    /// </summary>
    public static bool Satisfies(this ConsistencyLevel declared, ConsistencyLevel required)
    {
        if (!Enum.IsDefined(declared) || !Enum.IsDefined(required))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        return declared == ConsistencyLevel.Local ? required == ConsistencyLevel.Local : declared >= required;
    }

    public static string ToCode(this ConsistencyLevel level) => level switch
    {
        ConsistencyLevel.Local => "local",
        ConsistencyLevel.BestEffort => "best_effort",
        ConsistencyLevel.Leased => "leased",
        ConsistencyLevel.Strict => "strict",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };
}
