using System.Reflection;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Core.Contracts.Tests;

public class OwnerScopeTests
{
    private static readonly ResourceId Organization = ResourceId.New(ResourceTypes.Organization);
    private static readonly ResourceId Project = ResourceId.New(ResourceTypes.Project);

    [Fact]
    public void OperatorScopeNeverEqualsAnOrganizationOrProjectScope()
    {
        OwnerScope operatorScope = OperatorScope.Instance;
        OwnerScope organizationScope = new OrganizationScope(Organization);
        OwnerScope projectScope = new ProjectScope(Organization, Project);

        Assert.False(operatorScope.Equals(organizationScope));
        Assert.False(operatorScope.Equals(projectScope));
        Assert.False(organizationScope.Equals(operatorScope));
        Assert.False(projectScope.Equals(operatorScope));
        Assert.True(operatorScope.Equals(OperatorScope.Instance));
    }

    [Fact]
    public void AProjectScopeNeverEqualsItsOrganizationScope()
    {
        OwnerScope organizationScope = new OrganizationScope(Organization);
        OwnerScope projectScope = new ProjectScope(Organization, Project);

        Assert.False(projectScope.Equals(organizationScope));
        Assert.False(organizationScope.Equals(projectScope));
    }

    [Fact]
    public void ScopesOfTheSameKindAndIdsAreEqual()
    {
        Assert.Equal(new OrganizationScope(Organization), new OrganizationScope(Organization));
        Assert.Equal(new ProjectScope(Organization, Project), new ProjectScope(Organization, Project));
        Assert.Equal(new ProjectScope(Organization, Project).GetHashCode(), new ProjectScope(Organization, Project).GetHashCode());
        Assert.NotEqual(new OrganizationScope(Organization), new OrganizationScope(ResourceId.New(ResourceTypes.Organization)));
    }

    [Fact]
    public void EqualityOperatorsCompareKindAndIds()
    {
        OwnerScope first = new ProjectScope(Organization, Project);
        OwnerScope second = new ProjectScope(Organization, Project);

        Assert.True(first == second);
        Assert.False(first != second);
        Assert.True(first != new OrganizationScope(Organization));
        Assert.True(OperatorScope.Instance != new OrganizationScope(Organization));
        Assert.False(first == null);
    }

    [Fact]
    public void AWronglyTypedIdIsRejected()
    {
        Assert.Equal("core.resource_id.wrong_type", Assert.Throws<GatewayException>(() => new OrganizationScope(Project)).Code.Id);
        Assert.Equal("core.resource_id.wrong_type", Assert.Throws<GatewayException>(() => new ProjectScope(Project, Project)).Code.Id);
        Assert.Equal("core.resource_id.wrong_type", Assert.Throws<GatewayException>(() => new ProjectScope(Organization, Organization)).Code.Id);
        Assert.Equal("core.value.invalid", Assert.Throws<GatewayException>(() => new OrganizationScope(null!)).Code.Id);
    }

    [Fact]
    public void NoScopeTypeDeclaresAConversionOperator()
    {
        Type[] scopes = [typeof(OwnerScope), typeof(OrganizationScope), typeof(ProjectScope), typeof(OperatorScope)];

        foreach (var scope in scopes)
        {
            var operators = scope.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name is "op_Implicit" or "op_Explicit");
            Assert.Empty(operators);
        }
    }

    [Fact]
    public void NoScopeMemberTurnsOneKindIntoAnother()
    {
        Type[] scopes = [typeof(OwnerScope), typeof(OrganizationScope), typeof(ProjectScope), typeof(OperatorScope)];

        foreach (var scope in scopes)
        {
            var conversions = scope.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName && typeof(OwnerScope).IsAssignableFrom(method.ReturnType));
            Assert.Empty(conversions);
        }
    }

    [Fact]
    public void AScopedReferenceHasNoPublicOrProtectedConstructorAndNoPublicFactory()
    {
        var type = typeof(ScopedResourceReference);

        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly);
        Assert.Empty(constructors);

        var factories = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.Empty(factories);
    }

    [Fact]
    public void ATrustedResolverAttestsAReferenceWithTheGivenIdAndOwner()
    {
        var endpoint = ResourceId.New(ResourceTypes.Endpoint);
        var owner = new ProjectScope(Organization, Project);

        var reference = TestResolver.Resolve(endpoint, owner);

        Assert.Same(endpoint, reference.Id);
        Assert.Same(owner, reference.Owner);
    }

    [Fact]
    public void AttestRejectsAMissingValue()
    {
        var exception = Assert.Throws<GatewayException>(() => TestResolver.Resolve(null!, OperatorScope.Instance));

        Assert.Equal("core.value.invalid", exception.Code.Id);
    }

    [Fact]
    public void OperatorScopeHasNoPublicConstructor()
    {
        Assert.Empty(typeof(OperatorScope).GetConstructors());
        Assert.Empty(typeof(OwnerScope).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    // Test-only. Rule H6 scans production assemblies, so this type is outside its list.
    private sealed class TestResolver : OwnershipResolver
    {
        public static ScopedResourceReference Resolve(ResourceId id, OwnerScope owner) => Attest(id, owner);
    }
}
