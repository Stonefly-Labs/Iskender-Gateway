using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// The capability check (FR-005 to FR-008, FR-010, research R5): a pure function of the features' requirements, the
/// topology, the capabilities configured for each primitive, and the single-instance state. Each feature gets the result
/// of the first matching rule of contract Requirements rule 3, so it has exactly one reason. Owned by ISK-0005.
/// </summary>
public static class CoordinationCapabilityChecker
{
    /// <summary>
    /// Checks every feature. <paramref name="capabilities"/> names the coordinator configured for each of the four
    /// primitives, with <c>null</c> for a Redis or PostgreSQL coordinator whose settings are missing. A malformed set of
    /// requirements (a duplicate feature name, or a <c>local</c> minimum in <c>multi_replica</c>) fails with
    /// <c>coordination.capability_unsupported</c>; a <c>null</c> or undefined argument, or a primitive missing from
    /// <paramref name="capabilities"/>, fails with <c>core.value.invalid</c>.
    /// </summary>
    public static CapabilityReport Check(
        IReadOnlyCollection<FeatureRequirement> requirements,
        CoordinationTopology topology,
        IReadOnlyDictionary<CoordinationPrimitive, CoordinatorCapabilities?> capabilities,
        SingleInstanceState singleInstance)
    {
        if (requirements is null || requirements.Any(requirement => requirement is null) || capabilities is null
            || !Enum.IsDefined(topology) || !Enum.IsDefined(singleInstance)
            || !Enum.GetValues<CoordinationPrimitive>().All(capabilities.ContainsKey))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requirement in requirements)
        {
            if (!names.Add(requirement.Name)
                || (topology == CoordinationTopology.MultiReplica && requirement.Minimums.Values.Contains(ConsistencyLevel.Local)))
            {
                throw CoordinationFailures.Raise(ErrorCodes.CoordinationCapabilityUnsupported, CoordinationFailureClass.CapabilityUnsupported);
            }
        }

        return new CapabilityReport([.. requirements.Select(requirement => Check(requirement, topology, capabilities, singleInstance))], topology, singleInstance);
    }

    private static FeatureCapability Check(
        FeatureRequirement requirement,
        CoordinationTopology topology,
        IReadOnlyDictionary<CoordinationPrimitive, CoordinatorCapabilities?> capabilities,
        SingleInstanceState singleInstance)
    {
        var singleInstanceTopology = topology == CoordinationTopology.SingleInstance;
        if (requirement.MandatoryPath && requirement.Outage.Degrades)
        {
            return FeatureCapability.Refuse(requirement, CapabilityReason.DegradeNotPermitted);
        }

        if (singleInstanceTopology && singleInstance == SingleInstanceState.HeldByOther)
        {
            return FeatureCapability.Refuse(requirement, CapabilityReason.SingleInstanceViolated);
        }

        if (requirement.Minimums.Keys.Any(primitive => capabilities[primitive] is null))
        {
            return FeatureCapability.Refuse(requirement, CapabilityReason.CoordinatorNotConfigured);
        }

        var declared = requirement.Minimums.Keys.ToDictionary(primitive => primitive, primitive => capabilities[primitive]!.LevelOf(primitive));
        var local = declared.Where(pair => pair.Value == ConsistencyLevel.Local).Select(pair => pair.Key).ToList();
        if (!singleInstanceTopology && local.Count > 0)
        {
            return FeatureCapability.Refuse(requirement, CapabilityReason.LocalInMultiReplica);
        }

        if (local.Any(primitive => requirement.Minimums[primitive] == ConsistencyLevel.Strict))
        {
            return FeatureCapability.Refuse(requirement, CapabilityReason.UnsupportedConsistency);
        }

        if (declared.Any(pair => pair.Value != ConsistencyLevel.Local && pair.Value?.Satisfies(requirement.Minimums[pair.Key]) != true))
        {
            return FeatureCapability.Refuse(requirement, CapabilityReason.UnsupportedConsistency);
        }

        var mode = singleInstanceTopology || local.Count > 0 ? CoordinationMode.SingleInstance : CoordinationMode.Distributed;
        return FeatureCapability.Accept(requirement, mode, declared.ToDictionary(pair => pair.Key, pair => pair.Value!.Value));
    }
}
