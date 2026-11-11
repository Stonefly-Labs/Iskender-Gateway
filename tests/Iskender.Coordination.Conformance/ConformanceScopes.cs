using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;

namespace Iskender.Coordination.Conformance;

/// <summary>
/// Two organizations and one project in each, as coordination scopes from attested references, for the cross-scope
/// isolation checks (SC-007). New IDs are drawn for each instance, so suites never share state by accident.
/// </summary>
public sealed class ConformanceScopes
{
    public ConformanceScopes()
        : this(ResourceId.New(ResourceTypes.Organization), ResourceId.New(ResourceTypes.Organization))
    {
    }

    public ConformanceScopes(ResourceId organizationA, ResourceId organizationB)
    {
        OrganizationAScope = new OrganizationScope(organizationA);
        OrganizationBScope = new OrganizationScope(organizationB);
        ProjectAScope = new ProjectScope(organizationA, ResourceId.New(ResourceTypes.Project));
        ProjectBScope = new ProjectScope(organizationB, ResourceId.New(ResourceTypes.Project));
        OrganizationA = For(OrganizationAScope);
        OrganizationB = For(OrganizationBScope);
        ProjectA = For(ProjectAScope);
        ProjectB = For(ProjectBScope);
    }

    public OrganizationScope OrganizationAScope { get; }

    public OrganizationScope OrganizationBScope { get; }

    public ProjectScope ProjectAScope { get; }

    public ProjectScope ProjectBScope { get; }

    public CoordinationScope OrganizationA { get; }

    public CoordinationScope OrganizationB { get; }

    /// <summary>A project in organization A.</summary>
    public CoordinationScope ProjectA { get; }

    /// <summary>A project in organization B.</summary>
    public CoordinationScope ProjectB { get; }

    /// <summary>The four tenant scopes, for isolation checks.</summary>
    public IReadOnlyList<CoordinationScope> All => [OrganizationA, ProjectA, OrganizationB, ProjectB];

    /// <summary>A coordination scope for <paramref name="owner"/>, through an attested reference.</summary>
    public static CoordinationScope For(OwnerScope owner) => CoordinationScope.ForResource(Attester.Attest(owner));

    private sealed class Attester : OwnershipResolver
    {
        public static ScopedResourceReference Attest(OwnerScope owner) => Attest(ResourceId.New(ResourceTypes.Endpoint), owner);
    }
}
