using System.Globalization;
using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Canary;

/// <summary>
/// SC-006: a configuration value, a secret reference name, a database host, and a user name, each a distinct canary,
/// appear 0 times in readiness answers, exit diagnostics, standard error, captured logs, and <c>docker compose logs</c>
/// (container contract, Readiness rule 10 and Diagnostics rule 6), for both planes and the worker (ISK-0009 T088, FR-034).
/// </summary>
public class CanaryTests(ImageBuild build)
{
    internal const string Value = "canary-value-r3k8";

    internal const string SecretReference = "canary/secret-n8c1";

    internal const string Host = "canary-host-p9w2.invalid";

    internal const string User = "canary_user_h4t6";

    internal static readonly string[] Canaries = [Value, SecretReference, "secret-n8c1", Host, User];

    private static readonly TimeSpan Settle = TimeSpan.FromMinutes(2);

    public static TheoryData<string> Planes() => ["data_plane", "control_plane", "worker"];

    // The configuration value is the database name, a field value the running plane holds but never reports.
    private static string Persistence(string tlsMode = "require") =>
        $"\"persistence\":{{\"host\":\"{Host}\",\"database\":\"{Value}\",\"username\":\"{User}\",\"password\":{{\"secretRef\":\"{SecretReference}\"}},\"tlsMode\":\"{tlsMode}\"}}";

    // Outside development, ISK-0005 requires a Redis user name and password, so the section carries both canaries too.
    private static string Coordination =>
        $"\"coordination\":{{\"atomic\":{{\"coordinator\":\"redis\"}},\"redis\":{{\"endpoints\":[\"{Host}:6379\"],\"connectTimeout\":\"1s\",\"username\":\"{User}\",\"password\":{{\"secretRef\":\"{SecretReference}\"}}}}}}";

    // The worker's leases are on PostgreSQL, as work.dispatch requires, and its document has a work section.
    private static string WorkerSections => Coordination[..^1] + ",\"leases\":{\"coordinator\":\"postgresql\"}},\"work\":{}";

    // The worker is the control plane's definition run with the work command.
    private static ContainerPlaneDefinition Plane(string plane) => plane == "data_plane" ? PlaneDefinitions.DataPlane() : PlaneDefinitions.ControlPlane();

    private static string[] Arguments(string plane) => plane == "worker" ? ["work"] : [];

    private static string Sections(string plane, string persistence) => persistence + "," + (plane == "worker" ? WorkerSections : Coordination);

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task NoCanaryReachesReadinessLogsOrStandardErrorWhileRunningAndStopping(string plane)
    {
        using var host = new ContainerTestHost().WithDocument(sections: Sections(plane, Persistence()));
        using var client = new HttpClient();
        var run = await host.StartAsync(Plane(plane), Arguments(plane));

        var (status, body) = await host.WhenStartedAsync(client);
        var answer = body.GetRawText();
        host.Stop();
        var exitCode = await run;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("unreachable", ContainerTestHost.Reason(body, "persistence"));
        // No credential provider ships until ISK-0021, so the Redis password cannot be resolved (spec, Assumptions).
        Assert.Equal("authentication_failed", ContainerTestHost.Reason(body, "coordination"));
        Assert.Equal(ExitCodes.Success, exitCode);
        AssertAbsent(host, answer);
    }

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task NoCanaryReachesTheExitDiagnosticsOfARejectedStart(string plane)
    {
        // The configuration value is also an enumeration member the schema does not have.
        using var host = new ContainerTestHost().WithDocument(sections: Sections(plane, Persistence(tlsMode: Value)));

        var exitCode = await host.RunAsync(Plane(plane), Arguments(plane));

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

        var (_, worker) = await project.WhenAsync(Scenarios.Worker, "/health/ready", (status, body) => status == HttpStatusCode.ServiceUnavailable && body.Contains("\"started\"", StringComparison.Ordinal), Settle);
        output.Add(worker);

        foreach (var plane in Scenarios.Planes)
        {
            Assert.Equal(2, await project.WhenExitedAsync(Scenarios.Service(Scenarios.Rejected, plane), Settle));
        }

        await project.DownAsync();
        output.Add(await project.LogsAsync(Scenarios.Worker));
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
