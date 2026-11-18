using Iskender.Core.Contracts.Identifiers;
using Iskender.Infrastructure.PostgreSql.Directory;

namespace Iskender.Work.Tests.Directory;

/// <summary>
/// The directory's one attestation (ISK-0009 T013, research R4, FR-008, TR-5): each canonical form yields exactly its
/// scope, and every malformed form yields none, never a wider one.
/// </summary>
public class DirectoryScopeResolverTests
{
    private static readonly ResourceId Organization = ResourceId.New(ResourceTypes.Organization);

    private static readonly ResourceId Project = ResourceId.New(ResourceTypes.Project);

    [Fact]
    public void OperatorYieldsTheOperatorScope()
    {
        Assert.True(DirectoryScopeResolver.TryResolve("operator", out var scope));

        Assert.IsType<OperatorScope>(scope.Owner);
    }

    [Fact]
    public void AnOrganizationYieldsThatOrganizationsScope()
    {
        Assert.True(DirectoryScopeResolver.TryResolve($"org:{Organization}", out var scope));

        var owner = Assert.IsType<OrganizationScope>(scope.Owner);
        Assert.Equal(Organization, owner.Organization);
    }

    [Fact]
    public void AProjectYieldsThatProjectsScope()
    {
        Assert.True(DirectoryScopeResolver.TryResolve($"project:{Organization}/{Project}", out var scope));

        var owner = Assert.IsType<ProjectScope>(scope.Owner);
        Assert.Equal((Organization, Project), (owner.Organization, owner.Project));
    }

    public static TheoryData<string> Malformed() =>
    [
        "",
        " ",
        "OPERATOR",
        "Operator",
        "operator ",
        " operator",
        "org:",
        "org: ",
        $"ORG:{Organization}",
        $"org:{Organization.Value.ToUpperInvariant()}",
        $"org:{NonCanonical(Organization)}",
        $"org:{Project}",
        $"org:{Organization} ",
        $"org:{Organization}/{Project}",
        "project:",
        $"project:{Organization}",
        $"project:{Organization}/",
        $"project:/{Project}",
        $"project:{Project}/{Organization}",
        $"project:{Organization}/{Project}/{Project}",
        $"project:{Organization}/{NonCanonical(Project)}",
        $"PROJECT:{Organization}/{Project}",
        $"scope:{Organization}",
    ];

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedRowYieldsNoScope(string listed)
    {
        Assert.False(DirectoryScopeResolver.TryResolve(listed, out var scope));

        Assert.Null(scope);
    }

    [Fact]
    public void NoTextYieldsNoScope()
    {
        Assert.False(DirectoryScopeResolver.TryResolve(null, out var scope));

        Assert.Null(scope);
    }

    // The same ID with the body's first character 8, which sets a bit above the 128 an ID holds, as ISK-0002's tests do.
    private static string NonCanonical(ResourceId id)
    {
        var separator = id.Value.IndexOf('_', StringComparison.Ordinal);
        return id.Value[..(separator + 1)] + "8" + id.Value[(separator + 2)..];
    }
}
