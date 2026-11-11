using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// The capability check's output (TR-7): one result per feature, the topology, and the single-instance state the check
/// saw. The coordination gate is built from it. Owned by ISK-0005.
/// </summary>
public sealed class CapabilityReport
{
    private readonly Dictionary<string, FeatureCapability> _byName;

    /// <summary>A <c>null</c> argument, an undefined value, or two results for one feature name fails with <c>core.value.invalid</c>.</summary>
    public CapabilityReport(IReadOnlyList<FeatureCapability> features, CoordinationTopology topology, SingleInstanceState singleInstance)
    {
        if (features is null || features.Any(feature => feature is null) || !Enum.IsDefined(topology) || !Enum.IsDefined(singleInstance))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        _byName = new(StringComparer.Ordinal);
        foreach (var feature in features)
        {
            if (!_byName.TryAdd(feature.Feature.Name, feature))
            {
                throw new GatewayException(ErrorCodes.ValueInvalid);
            }
        }

        Features = [.. features];
        Topology = topology;
        SingleInstance = singleInstance;
    }

    /// <summary>The result for each feature, in registration order.</summary>
    public IReadOnlyList<FeatureCapability> Features { get; }

    public CoordinationTopology Topology { get; }

    /// <summary>The single-instance state when the check ran.</summary>
    public SingleInstanceState SingleInstance { get; }

    /// <summary>The result for the feature named <paramref name="name"/>, or <c>null</c> for an unknown feature.</summary>
    public FeatureCapability? Find(string name) => name is not null && _byName.TryGetValue(name, out var feature) ? feature : null;
}
