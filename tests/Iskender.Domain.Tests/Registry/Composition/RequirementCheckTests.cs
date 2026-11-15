using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Iskender.Domain.Tests.Configuration;
using Microsoft.Extensions.Logging;

namespace Iskender.Domain.Tests.Registry.Composition;

/// <summary>The requirement check, with <c>hosting</c> met by either root (hosting contract, Registration rule 7; research R2).</summary>
public class RequirementCheckTests
{
    private static readonly CompositionModule Coordination = new("coordination", [], ["hosting"]);

    private static readonly CompositionModule PostgreSqlCoordination = new("coordination-postgresql", [], ["coordination", "persistence-postgresql"]);

    [Theory]
    [InlineData(CompositionPlane.DataPlane, "hosting")]
    [InlineData(CompositionPlane.ControlPlane, "control-plane")]
    public void EitherRootMeetsTheHostingRequirement(CompositionPlane plane, string module)
    {
        var root = new CompositionModule(new CompositionRoot(plane, module).Module, [], []);

        Assert.Null(CompositionModules.FirstUnmet([root, Coordination], rootRegistered: true));
    }

    [Fact]
    public void WithoutARootHostingIsMissing()
    {
        Assert.Equal(("hosting", "coordination"), CompositionModules.FirstUnmet([Coordination], rootRegistered: false));
    }

    [Fact]
    public void OtherNamesAreMetOnlyByAModuleOfThatName()
    {
        Assert.Equal(("persistence-postgresql", "coordination-postgresql"), CompositionModules.FirstUnmet([Coordination, PostgreSqlCoordination], rootRegistered: true));
    }

    [Fact]
    public async Task TheCheckDoesNothingWithARoot()
    {
        var logger = new CapturingLogger();

        await new CompositionCheck([Coordination], new CompositionRoot(CompositionPlane.ControlPlane, "control-plane"), logger).StartAsync(TestContext.Current.CancellationToken);

        Assert.Empty(logger.Events);
    }

    [Fact]
    public async Task TheCheckDoesNothingWithNoModule()
    {
        var logger = new CapturingLogger();

        await new CompositionCheck([], root: null, logger).StartAsync(TestContext.Current.CancellationToken);

        Assert.Empty(logger.Events);
    }

    [Fact]
    public async Task WithModulesAndNoRootTheCheckWrites6003NamingHostingAndTheFirstModule()
    {
        var logger = new CapturingLogger();
        var check = new CompositionCheck([PostgreSqlCoordination, Coordination], root: null, logger);

        var failure = await Assert.ThrowsAsync<GatewayException>(() => check.StartAsync(TestContext.Current.CancellationToken));

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        var entry = Assert.Single(logger.Events);
        Assert.Equal((6003, "IskenderRegistrationMissing", LogLevel.Error), (entry.Id.Id, entry.Id.Name, entry.Level));
        Assert.Equal("Iskender registration missing: hosting required by coordination.", entry.Message);
        Assert.Equal([new("missing", "hosting"), new("requiredBy", "coordination")], failure.Error!.Diagnostics!.Values);
    }
}
