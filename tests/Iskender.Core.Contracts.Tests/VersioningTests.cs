using System.Reflection;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Tests;

public class VersioningTests
{
    [Fact]
    public void CoreContractsDeclaresItsContractAndVersion()
    {
        var package = typeof(WireContracts).Assembly.GetCustomAttribute<ContractPackageAttribute>();

        Assert.NotNull(package);
        Assert.Equal("core-contracts", package.ContractId);
        Assert.Equal(1, package.Version);
    }

    [Fact]
    public void WireContractsRegistersTheErrorEnvelopeAndResourceIdAtVersionOne()
    {
        Assert.Equal(["error-envelope", "resource-id"], WireContracts.All.Select(contract => contract.Id));
        Assert.All(WireContracts.All, contract =>
        {
            Assert.Equal(1, contract.CurrentVersion);
            Assert.Equal([1], contract.SupportedVersions);
        });
    }

    [Fact]
    public void TryGetFindsARegisteredContractOrdinally()
    {
        Assert.True(WireContracts.TryGet("error-envelope", out var found));
        Assert.Same(WireContracts.ErrorEnvelope, found);
        Assert.False(WireContracts.TryGet("Error-Envelope", out var missing));
        Assert.Null(missing);
    }
}
