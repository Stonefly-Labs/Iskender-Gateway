using System.Text.Json;
using Iskender.Infrastructure.Container.Health;

namespace Iskender.Container.Tests.Readiness;

/// <summary>Container contract, Readiness rule 1: the report's exact shape.</summary>
public class ReadinessReportTests
{
    private const string ContractExample = """
        {
          "version": 1,
          "status": "not_ready",
          "checks": [
            { "name": "lifecycle", "status": "ready", "reason": "started" },
            { "name": "persistence", "status": "not_ready", "reason": "authentication_failed" },
            { "name": "coordination", "status": "ready", "reason": "not_configured" },
            { "name": "single_instance", "status": "ready", "reason": "not_applicable" },
            { "name": "capability", "status": "ready", "reason": "refused_features" }
          ],
          "refused": [
            { "feature": "example.feature", "module": "example", "reason": "local_in_multi_replica" }
          ]
        }
        """;

    private static string Compact(string json) => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void TheJsonIsTheContractsExample()
    {
        var report = new ReadinessReport(
            1,
            false,
            [
                new("lifecycle", true, "started"),
                new("persistence", false, "authentication_failed"),
                new("coordination", true, "not_configured"),
                new("single_instance", true, "not_applicable"),
                new("capability", true, "refused_features"),
            ],
            [new("example.feature", "example", "local_in_multi_replica")]);

        Assert.Equal(Compact(ContractExample), System.Text.Encoding.UTF8.GetString(report.ToJson()));
    }

    [Fact]
    public void AReadyReportHasAnEmptyRefusedList()
    {
        var report = new ReadinessReport(1, true, [new("lifecycle", true, "started")], []);

        Assert.Equal(
            """{"version":1,"status":"ready","checks":[{"name":"lifecycle","status":"ready","reason":"started"}],"refused":[]}""",
            System.Text.Encoding.UTF8.GetString(report.ToJson()));
    }
}
