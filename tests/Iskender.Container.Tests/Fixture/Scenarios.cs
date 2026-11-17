using System.Net;
using System.Text.Json;

namespace Iskender.Container.Tests.Fixture;

/// <summary>
/// The User Story 1 scenarios in <c>compose/compose.yaml</c>, their expected readiness (quickstart.md section 4), and the
/// values their documents hold, none of which may appear in an answer or a log (SC-006).
/// </summary>
internal static class Scenarios
{
    public static readonly string[] Planes = ["data-plane", "control-plane"];

    /// <summary>Each running scenario: persistence's reason, coordination's reason, and whether coordination is ready.</summary>
    public static readonly IReadOnlyDictionary<string, (string Persistence, string Coordination, bool CoordinationReady)> Running =
        new Dictionary<string, (string, string, bool)>(StringComparer.Ordinal)
        {
            ["no-database"] = ("unreachable", "not_configured", true),
            ["database-running"] = ("authentication_failed", "not_configured", true),
            ["redis-down"] = ("authentication_failed", "unreachable", false),
            ["redis-unset"] = ("authentication_failed", "not_configured", false),
        };

    public const string Rejected = "rejected";

    /// <summary>Every host, port, user name, database name, secret reference name, and distinct field value the scenario documents hold.</summary>
    public static readonly string[] DocumentValues =
    [
        "canary-host-nodb.invalid", "canary-database-z8q1", "canary-cache-absent.invalid", "canary_db_q4r7", "canary_user_m2x8",
        "canary/runtime-v6k3", "runtime-v6k3", "99999", "6379",
    ];

    public static string Service(string scenario, string plane) => $"{scenario}-{plane}";

    /// <summary>True when <paramref name="body"/> is a readiness report with <paramref name="scenario"/>'s reasons.</summary>
    public static bool HasReasons(string scenario, HttpStatusCode status, string body)
    {
        if (status != HttpStatusCode.ServiceUnavailable || body.Length == 0)
        {
            return false;
        }

        var expected = Running[scenario];
        using var report = JsonDocument.Parse(body);
        var checks = report.RootElement.GetProperty("checks").EnumerateArray().ToDictionary(check => check.GetProperty("name").GetString()!, check => check.Clone());
        return checks["lifecycle"].GetProperty("reason").GetString() == "started"
            && checks["persistence"].GetProperty("reason").GetString() == expected.Persistence
            && checks["coordination"].GetProperty("reason").GetString() == expected.Coordination
            && checks["coordination"].GetProperty("status").GetString() == (expected.CoordinationReady ? "ready" : "not_ready");
    }

    /// <summary>The event IDs in a container's JSON log lines.</summary>
    public static IReadOnlyList<int> EventIds(string logs) =>
        [.. logs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith('{'))
            .Select(line =>
            {
                using var entry = JsonDocument.Parse(line);
                return entry.RootElement.GetProperty("EventId").GetInt32();
            })];
}
