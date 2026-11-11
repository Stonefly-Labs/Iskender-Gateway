using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Tests.Fixture;

/// <summary>Attested owners for tests: organizations A and B, and projects A/P1 and A/P2.</summary>
internal static class Owners
{
    public static readonly ResourceId OrgA = ResourceId.New(ResourceTypes.Organization);
    public static readonly ResourceId OrgB = ResourceId.New(ResourceTypes.Organization);
    public static readonly ResourceId ProjectA1 = ResourceId.New(ResourceTypes.Project);
    public static readonly ResourceId ProjectA2 = ResourceId.New(ResourceTypes.Project);

    public static UnitOfWorkScope Organization(ResourceId organization) => Scope(new OrganizationScope(organization));

    public static UnitOfWorkScope Project(ResourceId organization, ResourceId project) => Scope(new ProjectScope(organization, project));

    public static UnitOfWorkScope Scope(OwnerScope owner) => UnitOfWorkScope.ForResource(TestResolver.Resolve(ResourceId.New(ResourceTypes.Endpoint), owner));

    private sealed class TestResolver : OwnershipResolver
    {
        public static ScopedResourceReference Resolve(ResourceId id, OwnerScope owner) => Attest(id, owner);
    }
}
