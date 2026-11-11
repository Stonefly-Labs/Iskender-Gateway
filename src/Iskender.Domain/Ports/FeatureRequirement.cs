using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// A consuming feature's declaration (FR-006, TR-5): its unique name, owning module, the minimum level for each primitive
/// it uses, whether it is on a mandatory security or hard-budget path, and its outage behavior. Owned by ISK-0005.
/// </summary>
public sealed record FeatureRequirement
{
    private readonly Dictionary<CoordinationPrimitive, ConsistencyLevel> _minimums;

    /// <summary>
    /// An invalid name, module, primitive, or level, or a <c>null</c> outage, fails with <c>core.value.invalid</c>. An
    /// empty set of minimums is a malformed declaration and fails with <c>coordination.capability_unsupported</c>
    /// (contract Requirements rule 4).
    /// </summary>
    public FeatureRequirement(
        string name,
        string module,
        IReadOnlyDictionary<CoordinationPrimitive, ConsistencyLevel> minimums,
        bool mandatoryPath,
        OutageBehavior outage)
    {
        Name = PortNames.RequireOperation(name);
        Module = PortNames.RequireModule(module);
        if (minimums is null || outage is null || minimums.Any(pair => !Enum.IsDefined(pair.Key) || !Enum.IsDefined(pair.Value)))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        if (minimums.Count == 0)
        {
            throw new GatewayException(ErrorCodes.CoordinationCapabilityUnsupported);
        }

        _minimums = new(minimums);
        MandatoryPath = mandatoryPath;
        Outage = outage;
    }

    /// <summary>The feature's name, unique across a process, with the operation pattern.</summary>
    public string Name { get; }

    public string Module { get; }

    /// <summary>The minimum level for each primitive the feature uses. Never empty.</summary>
    public IReadOnlyDictionary<CoordinationPrimitive, ConsistencyLevel> Minimums => _minimums;

    /// <summary>Whether the feature is on a mandatory security or hard-budget path, which may not degrade.</summary>
    public bool MandatoryPath { get; }

    public OutageBehavior Outage { get; }

    public bool Uses(CoordinationPrimitive primitive) => _minimums.ContainsKey(primitive);
}
