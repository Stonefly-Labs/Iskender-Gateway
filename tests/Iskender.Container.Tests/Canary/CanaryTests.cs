using System.Globalization;
using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Canary;

/// <summary>
/// SC-006: a configuration value, a secret reference name, a database host, and a user name, each a distinct canary,
/// appear 0 times in readiness answers, exit diagnostics, standard error, captured logs, and <c>docker compose logs</c>
/// (container contract, Readiness rule 10 and Diagnostics rule 6).
/// </summary>
public class CanaryTests(ImageBuild build)
{
    private const string Value = "canary-value-r3k8";

    private const string SecretReference = "canary/secret-n8c1";

    private const string Host = "canary-host-p9w2.invalid";

    private const string User = "canary_user_h4t6";

    private static readonly string[] Canaries = [Value, SecretReference, "secret-n8c1", Host, User];

    private static readonly TimeSpan Settle = TimeSpan.FromMinutes(2);

    public static TheoryData<string> Planes() => ["data_plane", "control_plane"];

    // The configuration value is the database name, a field value the running plane holds but never reports.
    private static string Persistence(string tlsMode = "require") =>
        $"\"persistence\":{{\"host\":\"{Host}\",\"database\":\"{Value}\",\"username\":\"{User}\",\"password\":{{\"secretRef\":\"{SecretReference}\"}},\"tlsMode\":\"{tlsMode}\"}}";

    private static string Coordination =>
        $"\"coordination\":{{\"atomic\":{{\"coordinator\":\"redis\"}},\"redis\":{{\"endpoints\":[\"{Host}:6379\"],\"connectTimeout\":\"1s\"}}}}";

    private static ContainerPlaneDefinition Plane(string plane) => plane == "data_plane" ? PlaneDefinitions.DataPlane() : PlaneDefinitions.ControlPlane();

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task NoCanaryReachesReadinessLogsOrStandardErrorWhileRunningAndStopping(string plane)
    {
        // Outside development, ISK-0005 requires a Redis user name and password, which would report authentication_failed.
        using var host = new ContainerTestHost().WithDocument(sections: Persistence() + "," + Coordination);
        host.Environment["ISKENDER_ENVIRONMENT"] = "development";
        using var client = new HttpClient();
        var run = await host.StartAsync(Plane(plane));

        var (status, body) = await host.WhenStartedAsync(client);
        var answer = body.GetRawText();
        host.Stop();
        var exitCode = await run;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("unreachable", ContainerTestHost.Reason(body, "persistence"));
        Assert.Equal("unreachable", ContainerTestHost.Reason(body, "coordination"));
        Assert.Equal(ExitCodes.Success, exitCode);
        AssertAbsent(host, answer);
    }

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task NoCanaryReachesTheExitDiagnosticsOfARejectedStart(string plane)
    {
        // The configuration value is also an enumeration member the schema does not have.
        using var host = new ContainerTestHost().WithDocument(sections: Persistence(tlsMode: Value) + "," + Coordination);

        var exitCode = await host.RunAsync(Plane(plane));

        Assert.Equal(ExitCodes.ConfigurationRejected, exitCode);
        Assert.NotEmpty(host.Log.ById(3002));
        Assert.Equal(exitCode.ToString(CultureInfo.InvariantCulture), host.Log.Only(7002).Value("ExitCode"));
        AssertAbsent(host);
    }

    [Fact]
    [Trait("Category", "Compose")]
    public async Task NoScenarioValueReachesAComposeCycle()
    {
        await using var project = await ComposeProject.CreateAsync(await build.GetAsync());
        await project.UpAsync();
        var output = new List<string>();
        foreach (var scenario in Scenarios.Running.Keys)
        {
            foreach (var plane in Scenarios.Planes)
            {
                var service = Scenarios.Service(scenario, plane);
                var (_, ready) = await project.WhenAsync(service, "/health/ready", (status, body) => Scenarios.HasReasons(scenario, status, body), Settle);
                output.Add(ready);
            }
        }

        foreach (var plane in Scenarios.Planes)
        {
            Assert.Equal(2, await project.WhenExitedAsync(Scenarios.Service(Scenarios.Rejected, plane), Settle));
        }

        await project.DownAsync();
        foreach (var scenario in Scenarios.Running.Keys.Append(Scenarios.Rejected))
        {
            foreach (var plane in Scenarios.Planes)
            {
                output.Add(await project.LogsAsync(Scenarios.Service(scenario, plane)));
            }
        }

        foreach (var value in Scenarios.DocumentValues)
        {
            Assert.All(output, text => Assert.DoesNotContain(value, text, StringComparison.Ordinal));
        }
    }

    private static void AssertAbsent(ContainerTestHost host, params string[] answers)
    {
        var logged = host.Log.Entries.SelectMany(entry => entry.Values.Values.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Append(entry.Message)).ToList();
        Assert.NotEmpty(logged);
        foreach (var canary in Canaries)
        {
            Assert.All(answers, answer => Assert.DoesNotContain(canary, answer, StringComparison.Ordinal));
            Assert.All(logged, text => Assert.DoesNotContain(canary, text, StringComparison.Ordinal));
            Assert.DoesNotContain(canary, host.StandardError.ToString(), StringComparison.Ordinal);
        }
    }
}
