using System.Reflection;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Hosting.Tests.Surface;

/// <summary>The hosting assembly declares public contract <c>hosting</c>, version 1 (research R2, HCON Public surface rule 1).</summary>
public class ContractPackageTests
{
    [Fact]
    public void TheHostingAssemblyDeclaresContractHostingVersionOne()
    {
        var package = Assembly.Load("Iskender.Hosting").GetCustomAttribute<ContractPackageAttribute>();

        Assert.NotNull(package);
        Assert.Equal("hosting", package.ContractId);
        Assert.Equal(1, package.Version);
    }
}
