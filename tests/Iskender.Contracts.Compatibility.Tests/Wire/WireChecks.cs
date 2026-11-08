using System.Text;
using System.Text.Json;
using Iskender.Contracts.Compatibility.Tests.Harness;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Contracts.Compatibility.Tests.Wire;

/// <summary>A wire contract's versions as the checks see them. The real ones come from <see cref="WireContracts"/>.</summary>
public sealed record WireVersions(string Id, int Current, IReadOnlyList<int> Supported)
{
    public static WireVersions From(WireContract contract) => new(contract.Id, contract.CurrentVersion, contract.SupportedVersions);
}

/// <summary>
/// The API half of the harness (W1 to W6). Each wire contract's fixtures live under <c>&lt;root&gt;/&lt;contract&gt;/v&lt;n&gt;/</c>.
/// Every fixture of a version supported at the base is compared with the base, read with the current reader, and the
/// current version's fixtures are also written with the current writer and compared byte for byte.
/// </summary>
public sealed class WireChecks(IFileSet baseFiles, string baseRoot, IFileSet currentFiles, string currentRoot)
{
    private const string Envelope = ".envelope.json";
    private const string Read = ".read.json";
    private const string Input = ".input.json";

    private readonly List<Violation> _violations = [];

    /// <summary>Runs both registered wire contracts with the real codec and parser.</summary>
    public IReadOnlyList<Violation> RunAll() =>
        [.. ErrorEnvelope(WireVersions.From(WireContracts.ErrorEnvelope), EnvelopeFixtureCodec.Instance), .. ResourceId(WireVersions.From(WireContracts.ResourceId))];

    public IReadOnlyList<Violation> ErrorEnvelope(WireVersions versions, IEnvelopeCodec codec)
    {
        _violations.Clear();
        var contract = versions.Id;

        foreach (var version in BaseVersions(contract))
        {
            var directory = $"{contract}/v{version}";
            if (!versions.Supported.Contains(version))
            {
                Report("W6", contract, version, directory, "supported version dropped");
                continue;
            }

            CompareSchemas(contract, version);

            foreach (var path in baseFiles.List(Join(baseRoot, directory)))
            {
                var relative = Relative(baseRoot, path);
                var was = baseFiles.Read(path)!;
                var now = currentFiles.Read(Join(currentRoot, relative));

                // schema.json describes the fields; its changes are judged by W1 to W3, so an added optional field passes.
                if (relative.EndsWith("/schema.json", StringComparison.Ordinal))
                {
                    continue;
                }

                if (now is null)
                {
                    Report("W6", contract, version, relative, "fixture removed");
                }
                else if (!was.AsSpan().SequenceEqual(now) && !OnlySafeMessageChanged(was, now))
                {
                    Report("W6", contract, version, relative, "fixture changed");
                }

                if (path.EndsWith(Envelope, StringComparison.Ordinal))
                {
                    var expected = baseFiles.Read(path[..^Envelope.Length] + Read);
                    CheckRead(codec, contract, version, relative, was, expected, allowSafeMessage: true);
                }
            }
        }

        foreach (var version in versions.Supported)
        {
            var directory = $"{contract}/v{version}";
            var files = currentFiles.List(Join(currentRoot, directory));
            if (!files.Any(file => file.EndsWith(Envelope, StringComparison.Ordinal)))
            {
                Report("H1", contract, version, directory, "supported version has no fixture");
                continue;
            }

            foreach (var path in files.Where(file => file.EndsWith(Envelope, StringComparison.Ordinal)))
            {
                var relative = Relative(currentRoot, path);
                var bytes = currentFiles.Read(path)!;
                CheckRead(codec, contract, version, relative, bytes, currentFiles.Read(path[..^Envelope.Length] + Read), allowSafeMessage: false);

                if (version == versions.Current && currentFiles.Read(path[..^Envelope.Length] + Input) is { } input)
                {
                    using var document = JsonDocument.Parse(input);
                    var written = codec.Write(document.RootElement);
                    if (!written.AsSpan().SequenceEqual(bytes))
                    {
                        Report("W5", contract, version, relative, $"the writer's output differs from the fixture: {Encoding.UTF8.GetString(written)}");
                    }
                }
            }
        }

        return [.. _violations];
    }

    public IReadOnlyList<Violation> ResourceId(WireVersions versions)
    {
        _violations.Clear();
        var contract = versions.Id;

        foreach (var version in BaseVersions(contract))
        {
            if (!versions.Supported.Contains(version))
            {
                Report("W6", contract, version, $"{contract}/v{version}", "supported version dropped");
                continue;
            }

            foreach (var name in new[] { "valid.txt", "invalid.txt" })
            {
                var relative = $"{contract}/v{version}/{name}";
                var was = baseFiles.Read(Join(baseRoot, relative));
                if (was is null)
                {
                    continue;
                }

                var now = currentFiles.Read(Join(currentRoot, relative));
                if (now is null || !was.AsSpan().SequenceEqual(now))
                {
                    Report("W6", contract, version, relative, now is null ? "fixture removed" : "fixture changed");
                }

                CheckIds(contract, version, relative, was, valid: name == "valid.txt");
            }
        }

        foreach (var version in versions.Supported)
        {
            var valid = currentFiles.Read(Join(currentRoot, $"{contract}/v{version}/valid.txt"));
            var invalid = currentFiles.Read(Join(currentRoot, $"{contract}/v{version}/invalid.txt"));
            if (valid is null || invalid is null)
            {
                Report("H1", contract, version, $"{contract}/v{version}", "supported version has no valid.txt and invalid.txt fixture");
                continue;
            }

            CheckIds(contract, version, $"{contract}/v{version}/valid.txt", valid, valid: true);
            CheckIds(contract, version, $"{contract}/v{version}/invalid.txt", invalid, valid: false);
        }

        return [.. _violations];
    }

    /// <summary>
    /// Every line is parsed as an organization ID. A <c>valid.txt</c> line must parse; an <c>invalid.txt</c> line,
    /// <c>&lt;input&gt;\t&lt;code&gt;</c>, must fail with that code. Lines starting with <c>#</c> are comments.
    /// </summary>
    private void CheckIds(string contract, int version, string relative, byte[] content, bool valid)
    {
        foreach (var line in Encoding.UTF8.GetString(content).Split('\n').Where(line => line.Length > 0 && line[0] != '#'))
        {
            var parts = line.Split('\t');
            var input = parts[0];
            var parsed = Core.Contracts.Identifiers.ResourceId.TryParse(input, ResourceTypes.Organization, out _, out var error);
            if (valid && !parsed)
            {
                Report("W4", contract, version, $"{relative}: {input}", $"rejected with {error!.Id}");
            }
            else if (!valid && (parts.Length != 2 || parsed || error!.Id != parts[1]))
            {
                Report("W4", contract, version, $"{relative}: {input}", parsed ? "now accepted" : $"fails with {error!.Id}, not {(parts.Length == 2 ? parts[1] : "a stated code")}");
            }
        }
    }

    private void CheckRead(IEnvelopeCodec codec, string contract, int version, string relative, byte[] envelope, byte[]? expected, bool allowSafeMessage)
    {
        IReadOnlyDictionary<string, string?> actual;
        try
        {
            actual = codec.Read(envelope);
        }
        catch (GatewayException exception)
        {
            Report("W4", contract, version, relative, $"the reader rejects the fixture with {exception.Code.Id}");
            return;
        }

        if (expected is null)
        {
            Report("W4", contract, version, relative, "the fixture has no .read.json of expected values");
            return;
        }

        var wanted = EnvelopeFixtureCodec.Flatten(expected);
        var differences = wanted.Keys.Union(actual.Keys)
            .Where(key => !string.Equals(wanted.GetValueOrDefault(key), actual.GetValueOrDefault(key), StringComparison.Ordinal))
            .Where(key => !(allowSafeMessage && key == "message" && IsRegisteredSafeMessage(actual.GetValueOrDefault("code"), actual.GetValueOrDefault("message"))))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (differences.Count > 0)
        {
            Report("W4", contract, version, relative, $"read with a different meaning: {string.Join(", ", differences)}");
        }
    }

    private void CompareSchemas(string contract, int version)
    {
        var path = $"{contract}/v{version}/schema.json";
        var was = Schema(baseFiles.Read(Join(baseRoot, path)));
        var now = Schema(currentFiles.Read(Join(currentRoot, path)));

        foreach (var (name, field) in was)
        {
            if (!now.TryGetValue(name, out var current))
            {
                Report("W1", contract, version, name, "field removed or renamed");
                continue;
            }

            if (field.Type != current.Type)
            {
                Report("W2", contract, version, name, $"type changed from {field.Type} to {current.Type}");
            }

            if (!field.Required && current.Required)
            {
                Report("W3", contract, version, name, "optional field became required");
            }
        }
    }

    private static Dictionary<string, (string Type, bool Required)> Schema(byte[]? json)
    {
        var fields = new Dictionary<string, (string, bool)>(StringComparer.Ordinal);
        if (json is null)
        {
            return fields;
        }

        using var document = JsonDocument.Parse(json);
        foreach (var field in document.RootElement.GetProperty("fields").EnumerateArray())
        {
            fields[field.GetProperty("name").GetString()!] = (field.GetProperty("type").GetString()!, field.GetProperty("required").GetBoolean());
        }

        return fields;
    }

    /// <summary>A fixture whose only change is a <c>message</c> that equals the current registered safe message for its code.</summary>
    private static bool OnlySafeMessageChanged(byte[] was, byte[] now)
    {
        try
        {
            using var before = JsonDocument.Parse(was);
            using var after = JsonDocument.Parse(now);
            if (!after.RootElement.TryGetProperty("message", out var message) || !after.RootElement.TryGetProperty("code", out var code)
                || !IsRegisteredSafeMessage(code.GetString(), message.GetString()))
            {
                return false;
            }

            var replaced = Encoding.UTF8.GetString(was).Replace(
                JsonSerializer.Serialize(before.RootElement.GetProperty("message").GetString()),
                JsonSerializer.Serialize(message.GetString()),
                StringComparison.Ordinal);
            return string.Equals(replaced, Encoding.UTF8.GetString(now), StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsRegisteredSafeMessage(string? code, string? message) =>
        code is not null && ErrorCodes.TryGet(code, out var registered) && registered.SafeMessage == message;

    private IEnumerable<int> BaseVersions(string contract) =>
        baseFiles.List(Join(baseRoot, contract))
            .Select(path => Relative(baseRoot, path).Split('/'))
            .Where(parts => parts.Length >= 3 && parts[1].StartsWith('v') && int.TryParse(parts[1][1..], out _))
            .Select(parts => int.Parse(parts[1][1..], System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .Order();

    private void Report(string rule, string contract, int version, string item, string what) =>
        _violations.Add(new Violation(rule, contract, version, item, what, $"fixtures/wire/{contract}/v{version}", baseFiles.Name));

    private static string Join(string root, string path) => root.Length == 0 ? path : $"{root.TrimEnd('/')}/{path}";

    private static string Relative(string root, string path) => root.Length == 0 ? path : path[(root.TrimEnd('/').Length + 1)..];
}
