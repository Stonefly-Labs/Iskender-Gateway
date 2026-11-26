using System.Net;
using System.Text.Json.Nodes;
using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.ComposeTests;

/// <summary>
/// FR-003: each plane started alone in Compose, with only what its scenario depends on, is live and answers the same
/// readiness reasons; and FR-008 and TR-5: each image's event 6001 names the modules TR-5 lists, which the in-process
/// plane definitions compose too, so the real <c>Program.cs</c> files and the fixture cannot drift apart.
/// </summary>
[Trait("Category", "Compose")]
public class SinglePlaneTests(ImageBuild build)
{
    private static readonly TimeSpan Settle = TimeSpan.FromMinutes(2);

    public static TheoryData<string> Planes() => [.. Scenarios.Planes];

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task APlaneAloneIsLiveWithTheSameReasons(string plane)
    {
        await using var project = await ComposeProject.CreateAsync(await build.GetAsync());
        var service = Scenarios.Service("database-running", plane);

        await project.UpAsync(service);

        var (live, _) = await project.WhenAsync(service, "/health/live", (status, _) => status == HttpStatusCode.OK, Settle);
        Assert.Equal(HttpStatusCode.OK, live);
        await project.WhenAsync(service, "/health/ready", (status, body) => Scenarios.HasReasons("database-running", status, body), Settle);
        var other = Scenarios.Service("database-running", plane == "data-plane" ? "control-plane" : "data-plane");
        Assert.DoesNotContain(other, await project.RunningServicesAsync());
    }

    // TR-5's registrations, as the module names event 6001 lists in ordinal order.
    public static TheoryData<string, string> Modules() => new()
    {
        { "data-plane", "container,coordination,coordination-postgresql,coordination-redis,hosting,persistence-postgresql,work-postgresql" },
        { "control-plane", "container,control-plane,coordination,coordination-postgresql,coordination-redis,identity-postgresql,operator-security,persistence-postgresql,work-postgresql" },
    };

    [Theory]
    [MemberData(nameof(Modules))]
    public async Task EachImageComposesTheModulesTr5Lists(string plane, string modules)
    {
        await using var project = await ComposeProject.CreateAsync(await build.GetAsync());
        var service = Scenarios.Service("database-running", plane);
        await project.UpAsync(service);
        await project.WhenAsync(service, "/health/live", (status, _) => status == HttpStatusCode.OK, Settle);

        var composed = (await project.LogsAsync(service)).Split('\n').Single(line => line.Contains("\"EventId\":6001", StringComparison.Ordinal));

        Assert.Equal(modules, JsonNode.Parse(composed)!["State"]!["Modules"]!.GetValue<string>());
        Assert.Equal(modules, FixtureModules(plane));
    }

    // The modules the in-process plane definition registers, read from its registrations as the core reads them.
    private static string FixtureModules(string plane)
    {
        var services = new ServiceCollection();
        (plane == "data-plane" ? PlaneDefinitions.DataPlane() : PlaneDefinitions.ControlPlane()).Compose(services, new CompositionSources(null, null, false, null));
        return CompositionLog.ModulesText(services.Select(descriptor => descriptor.ImplementationInstance).OfType<CompositionModule>().Select(module => module.Name));
    }
}
