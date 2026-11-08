using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Contracts.Compatibility.Tests.Registries;

public sealed record CategoryEntry(string Id, string RetryRule);

public sealed record CodeEntry(string Id, string Category, string Meaning, string SafeMessage, string Status, int Since);

public sealed record ResourceTypeEntry(string Name, string Prefix, string Status, int Since);

public sealed record WireContractEntry(string Id, int CurrentVersion, IReadOnlyList<int> SupportedVersions);

/// <summary>
/// The registries as <c>baselines/registries.json</c> holds them: every compared field, each list sorted by ID, written
/// as indented JSON with LF line endings and a final newline, so the file is the same on every platform.
/// </summary>
public sealed record RegistrySnapshot(
    IReadOnlyList<CategoryEntry> ErrorCategories,
    IReadOnlyList<CodeEntry> ErrorCodes,
    IReadOnlyList<ResourceTypeEntry> ResourceTypes,
    IReadOnlyList<WireContractEntry> WireContracts)
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static RegistrySnapshot Current() => new(
        ErrorCategory.All.Select(category => new CategoryEntry(category.Id, category.RetryRule.ToString())).OrderBy(entry => entry.Id, StringComparer.Ordinal).ToList(),
        Core.Contracts.Errors.ErrorCodes.All.Select(code => new CodeEntry(code.Id, code.Category.Id, code.Meaning, code.SafeMessage, Status(code.Status), code.Since))
            .OrderBy(entry => entry.Id, StringComparer.Ordinal).ToList(),
        Core.Contracts.Identifiers.ResourceTypes.All.Select(type => new ResourceTypeEntry(type.Name, type.Prefix, Status(type.Status), type.Since))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal).ToList(),
        Core.Contracts.Versioning.WireContracts.All.Select(contract => new WireContractEntry(contract.Id, contract.CurrentVersion, contract.SupportedVersions))
            .OrderBy(entry => entry.Id, StringComparer.Ordinal).ToList());

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, Options))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("errorCategories");
            foreach (var entry in ErrorCategories)
            {
                writer.WriteStartObject();
                writer.WriteString("id", entry.Id);
                writer.WriteString("retryRule", entry.RetryRule);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("errorCodes");
            foreach (var entry in ErrorCodes)
            {
                writer.WriteStartObject();
                writer.WriteString("id", entry.Id);
                writer.WriteString("category", entry.Category);
                writer.WriteString("meaning", entry.Meaning);
                writer.WriteString("safeMessage", entry.SafeMessage);
                writer.WriteString("status", entry.Status);
                writer.WriteNumber("since", entry.Since);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("resourceTypes");
            foreach (var entry in ResourceTypes)
            {
                writer.WriteStartObject();
                writer.WriteString("name", entry.Name);
                writer.WriteString("prefix", entry.Prefix);
                writer.WriteString("status", entry.Status);
                writer.WriteNumber("since", entry.Since);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("wireContracts");
            foreach (var entry in WireContracts)
            {
                writer.WriteStartObject();
                writer.WriteString("id", entry.Id);
                writer.WriteNumber("currentVersion", entry.CurrentVersion);
                writer.WriteStartArray("supportedVersions");
                foreach (var version in entry.SupportedVersions)
                {
                    writer.WriteNumberValue(version);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    public static RegistrySnapshot Parse(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        static IEnumerable<JsonElement> Items(JsonElement root, string name) =>
            root.TryGetProperty(name, out var array) ? array.EnumerateArray() : [];

        return new RegistrySnapshot(
            Items(root, "errorCategories").Select(item => new CategoryEntry(Text(item, "id"), Text(item, "retryRule"))).ToList(),
            Items(root, "errorCodes").Select(item => new CodeEntry(
                Text(item, "id"), Text(item, "category"), Text(item, "meaning"), Text(item, "safeMessage"), Text(item, "status"), item.GetProperty("since").GetInt32())).ToList(),
            Items(root, "resourceTypes").Select(item => new ResourceTypeEntry(Text(item, "name"), Text(item, "prefix"), Text(item, "status"), item.GetProperty("since").GetInt32())).ToList(),
            Items(root, "wireContracts").Select(item => new WireContractEntry(
                Text(item, "id"), item.GetProperty("currentVersion").GetInt32(), item.GetProperty("supportedVersions").EnumerateArray().Select(version => version.GetInt32()).ToList())).ToList());
    }

    private static string Text(JsonElement item, string name) => item.GetProperty(name).GetString()!;

    private static string Status(RegistryStatus status) => status == RegistryStatus.Active ? "active" : "retired";
}
