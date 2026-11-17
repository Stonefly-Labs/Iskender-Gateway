using System.Text.Json;

namespace Iskender.Infrastructure.Container.Health;

/// <summary>One readiness answer (container contract, Readiness rule 1). Owned by ISK-0007.</summary>
public sealed record ReadinessReport(int Version, bool Ready, IReadOnlyList<ReadinessCheck> Checks, IReadOnlyList<RefusedFeature> Refused)
{
    /// <summary>The report's format version.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The report's JSON: <c>version</c>, <c>status</c>, <c>checks</c>, and <c>refused</c>, and nothing else.</summary>
    public byte[] ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteString("status", Status(Ready));
            writer.WriteStartArray("checks");
            foreach (var check in Checks)
            {
                writer.WriteStartObject();
                writer.WriteString("name", check.Name);
                writer.WriteString("status", Status(check.Ready));
                writer.WriteString("reason", check.Reason);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("refused");
            foreach (var feature in Refused)
            {
                writer.WriteStartObject();
                writer.WriteString("feature", feature.Feature);
                writer.WriteString("module", feature.Module);
                writer.WriteString("reason", feature.Reason);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static string Status(bool ready) => ready ? "ready" : "not_ready";
}

/// <summary>One check of a readiness report. Owned by ISK-0007.</summary>
public sealed record ReadinessCheck(string Name, bool Ready, string Reason);

/// <summary>A refused feature without <c>MandatoryPath</c>, as readiness lists it. Owned by ISK-0007.</summary>
public sealed record RefusedFeature(string Feature, string Module, string Reason);
