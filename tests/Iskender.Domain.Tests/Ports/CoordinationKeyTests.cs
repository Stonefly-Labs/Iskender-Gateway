using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class CoordinationKeyTests
{
    private static readonly ResourceId Org = ResourceId.Parse("org_00000000000000000000000001", ResourceTypes.Organization);
    private static readonly ResourceId Prj = ResourceId.Parse("prj_0000000000000000000000000a", ResourceTypes.Project);

    // Computed independently with, for each scope's canonical form <s>:
    //   printf 'iskender.coordination.v1\0<s>\0beta\0log.append' | sha256sum
    [Fact]
    public void MatchesTheIndependentlyComputedVectorForEachScopeKind()
    {
        Assert.Equal("47aeab63b5c4923d80d25f5a13a9c9e31d1675cd7d51395ebcfa7af50a02c51d", CoordinationKey.Hash(new OrganizationScope(Org), "beta", "log.append"));
        Assert.Equal("447c7b60dfa5442d5c9014f0a0caf8f86da16de2e40ca5363e2f69c287455125", CoordinationKey.Hash(new ProjectScope(Org, Prj), "beta", "log.append"));
        Assert.Equal("fccd2293d91e38d2b9b971949f1a16f3af528537f47c5d58518b99c214dd61d4", CoordinationKey.Hash(OperatorScope.Instance, "beta", "log.append"));
    }

    [Fact]
    public void TheScopeKeyIsTheCanonicalForm()
    {
        Assert.Equal("org:org_00000000000000000000000001", CoordinationKey.ScopeKey(new OrganizationScope(Org)));
        Assert.Equal("project:org_00000000000000000000000001/prj_0000000000000000000000000a", CoordinationKey.ScopeKey(new ProjectScope(Org, Prj)));
        Assert.Equal("operator", CoordinationKey.ScopeKey(OperatorScope.Instance));
    }

    [Fact]
    public void DifferentScopesModulesAndNamesGiveDifferentHashes()
    {
        var other = new OrganizationScope(ResourceId.Parse("org_00000000000000000000000002", ResourceTypes.Organization));
        string[] hashes =
        [
            CoordinationKey.Hash(new OrganizationScope(Org), "beta", "log.append"),
            CoordinationKey.Hash(other, "beta", "log.append"),
            CoordinationKey.Hash(new OrganizationScope(Org), "alpha", "log.append"),
            CoordinationKey.Hash(new OrganizationScope(Org), "beta", "log.append2"),
            CoordinationKey.Hash(new ProjectScope(Org, Prj), "beta", "log.append"),
            CoordinationKey.Hash(OperatorScope.Instance, "beta", "log.append"),
        ];

        Assert.Equal(hashes.Length, hashes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AProjectAndItsOrganizationGiveDifferentHashes()
    {
        Assert.NotEqual(CoordinationKey.Hash(new OrganizationScope(Org), "quota", "monthly"), CoordinationKey.Hash(new ProjectScope(Org, Prj), "quota", "monthly"));
    }

    [Fact]
    public void NoHashContainsAnOrganizationOrProjectId()
    {
        foreach (var scope in new OwnerScope[] { new OrganizationScope(Org), new ProjectScope(Org, Prj), OperatorScope.Instance })
        {
            var hash = CoordinationKey.Hash(scope, "beta", "log.append");

            Assert.Matches("^[0-9a-f]{64}$", hash);
            Assert.DoesNotContain(Org.Value, hash, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Prj.Value, hash, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("Beta", "log")]
    [InlineData("beta", "Log")]
    [InlineData("", "log")]
    [InlineData("beta", "")]
    public void AnInvalidModuleOrNameIsRejected(string module, string name)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CoordinationKey.Hash(OperatorScope.Instance, module, name)).Code);
    }

    [Fact]
    public void ANullScopeIsRejected()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CoordinationKey.Hash(null!, "beta", "log")).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => CoordinationKey.ScopeKey(null!)).Code);
    }
}
