using System.Net;
using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.ComposeTests;

/// <summary>FR-003: each plane started alone in Compose, with only what its scenario depends on, is live and answers the same readiness reasons.</summary>
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
}
