using System.Net;
using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.ComposeTests;

/// <summary>
/// User Story 1, acceptance scenarios 1 to 6, and SC-002: over 5 consecutive <c>up</c> and <c>down</c> cycles, every
/// running scenario's containers stay running, are live, and answer readiness with quickstart.md section 4's reasons; the
/// rejected configuration exits 2 with events 3002, 3003, and 6002; and no answer or log holds a document's value.
/// </summary>
[Trait("Category", "Compose")]
public class SplitContainerAcceptanceTests(ImageBuild build)
{
    private const int Cycles = 5;

    private static readonly TimeSpan Settle = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task FiveCyclesGiveTheSameStatesReasonsAndExitCodes()
    {
        var images = await build.GetAsync();
        for (var cycle = 1; cycle <= Cycles; cycle++)
        {
            await using var project = await ComposeProject.CreateAsync(images);
            await project.UpAsync();
            var answers = new List<string>();
            foreach (var (scenario, _) in Scenarios.Running)
            {
                foreach (var plane in Scenarios.Planes)
                {
                    var service = Scenarios.Service(scenario, plane);
                    var (_, ready) = await project.WhenAsync(service, "/health/ready", (status, body) => Scenarios.HasReasons(scenario, status, body), Settle);
                    var (live, _) = await project.WhenAsync(service, "/health/live", (status, _) => status == HttpStatusCode.OK, Settle);
                    Assert.Equal(HttpStatusCode.OK, live);
                    Assert.Equal("running", (await project.StateAsync(service)).Status);
                    answers.Add(ready);
                }
            }

            foreach (var plane in Scenarios.Planes)
            {
                var service = Scenarios.Service(Scenarios.Rejected, plane);
                Assert.Equal(2, await project.WhenExitedAsync(service, Settle));
                var events = Scenarios.EventIds(await project.LogsAsync(service));
                Assert.Contains(3002, events);
                Assert.Contains(3003, events);
                Assert.Contains(6002, events);
            }

            var logs = new List<string>();
            foreach (var scenario in Scenarios.Running.Keys.Append(Scenarios.Rejected))
            {
                foreach (var plane in Scenarios.Planes)
                {
                    logs.Add(await project.LogsAsync(Scenarios.Service(scenario, plane)));
                }
            }

            foreach (var value in Scenarios.DocumentValues)
            {
                Assert.All(answers, answer => Assert.DoesNotContain(value, answer, StringComparison.Ordinal));
                Assert.All(logs, log => Assert.DoesNotContain(value, log, StringComparison.Ordinal));
            }

            TestContext.Current.SendDiagnosticMessage($"Cycle {cycle} of {Cycles} passed.");
        }
    }
}
