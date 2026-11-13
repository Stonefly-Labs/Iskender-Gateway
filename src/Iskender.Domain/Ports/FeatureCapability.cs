using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// The capability check's result for one feature (TR-7): accepted with its mode and the level granted per primitive, or
/// refused with one reason and no mode or levels. Owned by ISK-0005.
/// </summary>
public sealed record FeatureCapability
{
    private FeatureCapability(FeatureRequirement feature, CapabilityReason reason, CoordinationMode? mode, IReadOnlyDictionary<CoordinationPrimitive, ConsistencyLevel> levels)
    {
        Feature = feature;
        Reason = reason;
        Mode = mode;
        Levels = levels;
    }

    public FeatureRequirement Feature { get; }

    public bool Accepted => Reason == CapabilityReason.Accepted;

    public CapabilityReason Reason { get; }

    /// <summary>How the feature runs when accepted, or <c>null</c> when refused.</summary>
    public CoordinationMode? Mode { get; }

    /// <summary>The level granted for each primitive the feature uses when accepted, and empty when refused.</summary>
    public IReadOnlyDictionary<CoordinationPrimitive, ConsistencyLevel> Levels { get; }

    /// <summary>An accepted result. <paramref name="levels"/> must cover exactly the primitives the feature uses.</summary>
    public static FeatureCapability Accept(FeatureRequirement feature, CoordinationMode mode, IReadOnlyDictionary<CoordinationPrimitive, ConsistencyLevel> levels)
    {
        if (feature is null || !Enum.IsDefined(mode) || levels is null
            || levels.Count != feature.Minimums.Count || !feature.Minimums.Keys.All(levels.ContainsKey))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        return new(feature, CapabilityReason.Accepted, mode, new Dictionary<CoordinationPrimitive, ConsistencyLevel>(levels));
    }

    /// <summary>A refused result with one refusal reason.</summary>
    public static FeatureCapability Refuse(FeatureRequirement feature, CapabilityReason reason)
    {
        if (feature is null || !Enum.IsDefined(reason) || reason == CapabilityReason.Accepted)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        return new(feature, reason, mode: null, new Dictionary<CoordinationPrimitive, ConsistencyLevel>());
    }
}

/// <summary>How an accepted feature runs: on one process only, or shared across replicas (FR-010). Owned by ISK-0005.</summary>
public enum CoordinationMode
{
    SingleInstance,
    Distributed,
}

/// <summary>The snake-case code of each mode. Owned by ISK-0005.</summary>
public static class CoordinationModes
{
    public static string ToCode(this CoordinationMode mode) => mode switch
    {
        CoordinationMode.SingleInstance => "single_instance",
        CoordinationMode.Distributed => "distributed",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };
}
