using System.Reflection;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Tests;

// The current versions are checked by rules V2 and H4 against the base and the published contract. These tests pin only
// what no compatible change or declared break may alter, so a version increment does not fail them (CONV-0002-001).
public class VersioningTests
{
    [Fact]
    public void CoreContractsDeclaresItsContractAndAVersionNoItemIsNewerThan()
    {
        var package = typeof(WireContracts).Assembly.GetCustomAttribute<ContractPackageAttribute>();

        Assert.NotNull(package);
        Assert.Equal("core-contracts", package.ContractId);
        Assert.InRange(package.Version, 1, int.MaxValue);
        Assert.All(ErrorCodes.All, code => Assert.InRange(code.Since, 1, package.Version));
        Assert.All(ResourceTypes.All, type => Assert.InRange(type.Since, 1, package.Version));
    }

    [Fact]
    public void WireContractsRegistersTheErrorEnvelopeAndResourceIdFirst()
    {
        Assert.Equal(["error-envelope", "resource-id"], WireContracts.All.Take(2).Select(contract => contract.Id));
        Assert.All(WireContracts.All, contract =>
        {
            Assert.NotEmpty(contract.SupportedVersions);
            Assert.InRange(contract.SupportedVersions[0], 1, int.MaxValue);
            Assert.Equal(contract.SupportedVersions.Distinct(), contract.SupportedVersions);
            Assert.Equal(contract.SupportedVersions[^1], contract.CurrentVersion);
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
