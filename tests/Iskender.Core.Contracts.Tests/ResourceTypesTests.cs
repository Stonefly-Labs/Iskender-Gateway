using Iskender.Core.Contracts.Identifiers;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Tests;

public class ResourceTypesTests
{
    [Fact]
    public void ThirteenVersionOneTypesInContractOrder()
    {
        // A later version may add a type or retire one, so only the version-1 types are pinned (CONV-0002-001).
        (string Name, string Prefix)[] expected =
        [
            ("organization", "org"), ("project", "prj"), ("endpoint", "ep"), ("model alias", "alias"),
            ("backend pool", "pool"), ("deployment", "dep"), ("credential profile", "cred"), ("API key", "key"),
            ("policy definition", "poldef"), ("policy assignment", "polasgn"), ("budget", "budget"),
            ("conversation", "conv"), ("audit entry", "audit"),
        ];

        var versionOne = ResourceTypes.All.Where(type => type.Since == 1).ToList();

        Assert.Equal(expected, versionOne.Select(type => (type.Name, type.Prefix)));
        Assert.Equal(versionOne, ResourceTypes.All.Take(versionOne.Count));
        Assert.All(ResourceTypes.All, type => Assert.True(Enum.IsDefined(type.Status)));
    }

    [Fact]
    public void PrefixesAreUniqueAndFollowTheRule()
    {
        Assert.Equal(ResourceTypes.All.Count, ResourceTypes.All.Select(type => type.Prefix).Distinct(StringComparer.Ordinal).Count());
        Assert.All(ResourceTypes.All, type => Assert.Matches("^[a-z]{2,8}$", type.Prefix));
    }

    [Fact]
    public void TryGetByPrefixIsOrdinal()
    {
        Assert.True(ResourceTypes.TryGetByPrefix("ep", out var found));
        Assert.Same(ResourceTypes.Endpoint, found);
        Assert.False(ResourceTypes.TryGetByPrefix("EP", out var missing));
        Assert.Null(missing);
        Assert.False(ResourceTypes.TryGetByPrefix("req", out _));
    }

    [Fact]
    public void ValidationRejectsTheReservedRequestPrefix()
    {
        var type = new ResourceType("request", "req", RegistryStatus.Active, 1);

        Assert.Throws<InvalidOperationException>(() => ResourceTypes.Validate([type]));
    }

    [Fact]
    public void ValidationRejectsADuplicatePrefix()
    {
        var duplicate = new ResourceType("other", "org", RegistryStatus.Active, 1);

        Assert.Throws<InvalidOperationException>(() => ResourceTypes.Validate([ResourceTypes.Organization, duplicate]));
    }

    [Theory]
    [InlineData("o")]
    [InlineData("abcdefghi")]
    [InlineData("Org")]
    [InlineData("or1")]
    [InlineData("o_g")]
    [InlineData("")]
    public void ValidationRejectsAnInvalidPrefix(string prefix)
    {
        var type = new ResourceType("bad", prefix, RegistryStatus.Active, 1);

        Assert.Throws<InvalidOperationException>(() => ResourceTypes.Validate([type]));
    }
}
