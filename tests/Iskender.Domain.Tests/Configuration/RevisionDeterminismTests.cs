using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>SC-005 and FR-028: the same content gives the same revision on every host, and any change gives a new one.</summary>
public class RevisionDeterminismTests
{
    public static TheoryData<string, string> PairsByCulture() =>
        [.. Directory.GetDirectories(FixtureLoader.FullPath("equivalent")).Select(Path.GetFileName).Order(StringComparer.Ordinal)
            .SelectMany(pair => Cultures.Select(culture => (pair!, culture)))];

    public static TheoryData<string> CultureNames() => [.. Cultures];

    private static readonly string[] Cultures = ["", "tr-TR", "de-DE"];

    private static T InCulture<T>(string culture, Func<T> action)
    {
        var (previous, previousUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            return action();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (previous, previousUi);
        }
    }

    private static ValidatedSnapshot Snapshot(ConfigurationLoadRequest request)
    {
        var report = ConfigurationValidator.Validate(request);
        Assert.True(report.IsValid, report.ToString());
        return (ValidatedSnapshot)report.Snapshot!;
    }

    private static ValidatedSnapshot Side(string pair, string side)
    {
        var files = Directory.GetFiles(FixtureLoader.FullPath($"equivalent/{pair}"), side + ".*").Select(Path.GetFileName).ToList();
        string Relative(string file) => $"equivalent/{pair}/{file}";
        if (files.Count == 1)
        {
            return Snapshot(new ConfigurationLoadRequest(TestSchemas.Registry(), FixtureLoader.Document(Relative(files[0]!))));
        }

        var baseFile = files.Single(file => file!.StartsWith(side + ".base.", StringComparison.Ordinal))!;
        var overlayFile = files.Single(file => file!.StartsWith(side + ".overlay.", StringComparison.Ordinal))!;
        using var variables = JsonDocument.Parse(File.ReadAllBytes(FixtureLoader.FullPath(Relative(side + ".env.json"))));
        return Snapshot(new ConfigurationLoadRequest(
            TestSchemas.Registry(),
            FixtureLoader.Document(Relative(baseFile)),
            FixtureLoader.Document(Relative(overlayFile)),
            ConfigurationEnvironmentSource.FromDictionary(variables.RootElement.EnumerateObject().Select(v => KeyValuePair.Create(v.Name, v.Value.GetString()!)))));
    }

    private static ConfigurationRevision Revision(string json, ConfigurationSchemaRegistry? registry = null) =>
        Snapshot(new ConfigurationLoadRequest(registry ?? TestSchemas.Registry(), ConfigurationDocumentSource.FromString("full.json", ConfigurationDocumentFormat.Json, json))).Revision;

    [Fact]
    public void ThereAreAtLeastFivePairsCoveringEachRequiredKind()
    {
        string[] required = ["json-and-yaml", "reordered-keys", "split-across-layers", "whitespace", "yaml-comments"];

        Assert.Superset(required.ToHashSet(StringComparer.Ordinal), Directory.GetDirectories(FixtureLoader.FullPath("equivalent")).Select(path => Path.GetFileName(path)!).ToHashSet(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(PairsByCulture))]
    public void Scenario4EachPairGivesTheSameSnapshotAndRevision(string pair, string culture)
    {
        var (a, b) = InCulture(culture, () => (Side(pair, "a"), Side(pair, "b")));

        Assert.Equal(a.Canonical, b.Canonical);
        Assert.Equal(a.Revision, b.Revision);
        Assert.Equal(a.Revision, InCulture("", () => Side(pair, "a").Revision));
    }

    [Theory]
    [MemberData(nameof(CultureNames))]
    public void CarriageReturnLineEndingsGiveTheSameRevision(string culture)
    {
        var lf = File.ReadAllText(FixtureLoader.FullPath("equivalent/whitespace/b.json"));

        Assert.Equal(InCulture(culture, () => Revision(lf)), InCulture(culture, () => Revision(lf.Replace("\n", "\r\n", StringComparison.Ordinal))));
    }

    [Fact]
    public void AStringIsEscapedOnlyWhereJsonRequiresSoItsBytesDoNotDependOnUnicodeTables()
    {
        // An emoji, U+2028, a private-use character, and U+0378, which is unassigned, are all raw UTF-8.
        var text = "\U0001F600\u2028\uE000\u0378\t\"\\";
        var document = "{\"iskender\":{\"alpha\":{\"name\":" + JsonSerializer.Serialize(text) + "}}}";

        byte[] expected = [.. "{\"form\":2,\"iskender\":{\"alpha\":{\"name\":\""u8, 0xF0, 0x9F, 0x98, 0x80, 0xE2, 0x80, 0xA8, 0xEE, 0x80, 0x80, 0xCD, 0xB8,
            .. "\\t\\\"\\\\\"}},\"schemas\":{\"alpha\":1}}"u8];

        Assert.Equal(expected, Encoding.UTF8.GetBytes(CanonicalWriterTests.Canonical(document, TestSchemas.Alpha)));
    }

    /// <summary>Twenty single-value changes to <c>valid/full.json</c>, covering every field kind.</summary>
    private static readonly (string Name, Action<JsonNode> Change)[] Changes =
    [
        ("text", root => root["iskender"]!["persistence"]!["host"] = "db-2.example"),
        ("whole number", root => root["iskender"]!["persistence"]!["connectionTimeout"] = 46),
        ("number", root => root["iskender"]!["persistence"]!["sampleRatio"] = 0.76m),
        ("boolean", root => root["iskender"]!["persistence"]!["readOnly"] = false),
        ("enumeration", root => root["iskender"]!["persistence"]!["mode"] = "primary"),
        ("duration", root => root["iskender"]!["persistence"]!["idleTimeout"] = "11m"),
        ("uri port", root => root["iskender"]!["persistence"]!["endpoint"] = "postgres://db-1.example:5433/app"),
        ("uri path", root => root["iskender"]!["persistence"]!["endpoint"] = "postgres://db-1.example:5432/app2"),
        ("secret reference", root => root["iskender"]!["persistence"]!["password"]!["secretRef"] = "db/secondary"),
        ("section whole number", root => root["iskender"]!["persistence"]!["pool"]!["size"] = 21),
        ("section text", root => root["iskender"]!["persistence"]!["pool"]!["name"] = "main2"),
        ("array of sections text", root => root["iskender"]!["persistence"]!["replicas"]![0]!["host"] = "r3.example"),
        ("array of sections whole number", root => root["iskender"]!["persistence"]!["replicas"]![1]!["weight"] = 3),
        ("array of sections item removed", root => root["iskender"]!["persistence"]!["replicas"]!.AsArray().RemoveAt(1)),
        ("array of text item", root => root["iskender"]!["persistence"]!["tags"]![1] = "west"),
        ("array of text item added", root => root["iskender"]!["persistence"]!["tags"]!.AsArray().Add("north")),
        ("second namespace text", root => root["iskender"]!["alpha"]!["name"] = "firsts"),
        ("second namespace whole number", root => root["iskender"]!["alpha"]!["count"] = 4),
        ("third namespace text", root => root["iskender"]!["beta"]!["name"] = "third"),
        ("third namespace boolean", root => root["iskender"]!["beta"]!["enabled"] = false),
    ];

    [Theory]
    [MemberData(nameof(CultureNames))]
    public void Scenario5TwentySingleValueChangesGiveTwentyDistinctRevisions(string culture)
    {
        var original = File.ReadAllText(FixtureLoader.FullPath("valid/full.json"));

        var revisions = InCulture(culture, () => Changes.Select(change =>
        {
            var root = JsonNode.Parse(original)!;
            change.Change(root);
            return Revision(root.ToJsonString());
        }).ToList());

        Assert.Equal(20, Changes.Length);
        Assert.Equal(20, revisions.Distinct().Count());
        Assert.DoesNotContain(InCulture(culture, () => Revision(original)), revisions);
    }

    [Fact]
    public void AChangeToARegisteredSchemaVersionAloneChangesTheRevision()
    {
        var original = File.ReadAllText(FixtureLoader.FullPath("valid/full.json"));
        var alphaTwo = SectionSchema.Create("alpha", TestSchemas.Alpha.OwnerModule, 2).WithFields([.. TestSchemas.Alpha.Fields]);

        Assert.NotEqual(Revision(original), Revision(original, ConfigurationSchemaRegistry.Create([alphaTwo, TestSchemas.Beta, TestSchemas.Persistence])));
    }
}
