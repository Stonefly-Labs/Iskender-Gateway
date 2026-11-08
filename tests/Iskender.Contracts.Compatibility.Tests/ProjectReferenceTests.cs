using System.Reflection;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Contracts.Compatibility.Tests;

public class ProjectReferenceTests
{
    [Theory]
    [InlineData("Iskender.Core.Contracts", "core-contracts")]
    [InlineData("Iskender.Providers.Abstractions", "providers-abstractions")]
    [InlineData("Iskender.Policy.Sdk", "policy-sdk")]
    public void EachPublicContractPackageLoadsWithItsContractId(string assembly, string contractId)
    {
        var package = Assembly.Load(assembly).GetCustomAttribute<ContractPackageAttribute>();

        Assert.NotNull(package);
        Assert.Equal(contractId, package.ContractId);
    }

    [Fact]
    public void TheFixturesAssemblyLoads()
    {
        Assert.Equal("Iskender.Contracts.Compatibility.Fixtures", Assembly.Load("Iskender.Contracts.Compatibility.Fixtures").GetName().Name);
    }
}
