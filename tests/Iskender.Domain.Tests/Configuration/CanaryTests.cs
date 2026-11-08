using System.Text;
using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>User Story 2, scenario 7, and SC-003: a value planted everywhere appears in no error, rendering, log, or exception.</summary>
public partial class CanaryTests
{
    private const string Canary = "ISKENDER-CANARY-7f3a";

    public static TheoryData<string> NegativeFixtures() =>
        [.. FixtureLoader.Fixtures("invalid").Concat(FixtureLoader.Fixtures("edge")).Where(path => FixtureLoader.Expected(path).Count > 0)];

    /// <summary>Every JSON string, matched whole so that a match never starts inside one. A string followed by a colon is a key.</summary>
    [GeneratedRegex("\"(?:[^\"\\\\\\n]|\\\\.)*\"(?<key>\\s*:)?")]
    private static partial Regex JsonString();

    [GeneratedRegex("(?<quote>['\"])(?:(?!\\k<quote>).)*\\k<quote>(?<key>\\s*:)?")]
    private static partial Regex YamlQuoted();

    /// <summary>A YAML block value after <c>key: </c> or <c>- </c> that is plain text, not a number, boolean, null, or structure.</summary>
    [GeneratedRegex("(?<lead>(?::|^\\s*-)[ \\t]+)(?<value>(?![-+]?[0-9.]|true\\b|false\\b|null\\b|~|[\\[{&*!|>'\"#])[^\\n#,\\]}]*[^\\s,\\]}#])", RegexOptions.Multiline)]
    private static partial Regex YamlPlainValue();

    internal static string Plant(string text, ConfigurationDocumentFormat format) => format == ConfigurationDocumentFormat.Json
        ? JsonString().Replace(text, match => match.Groups["key"].Success ? match.Value : $"\"{Canary}\"")
        : YamlPlainValue().Replace(YamlQuoted().Replace(text, match => match.Groups["key"].Success ? match.Value : $"'{Canary}'"), match => match.Groups["lead"].Value + Canary);

    private static ConfigurationDocumentSource Planted(string fixture)
    {
        var bytes = File.ReadAllBytes(FixtureLoader.FullPath(fixture));
        var format = FixtureLoader.FormatOf(fixture);
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Invalid UTF-8 cannot hold a planted string, so it is read as it is.
            return ConfigurationDocumentSource.FromBytes(Path.GetFileName(fixture), format, bytes);
        }

        return ConfigurationDocumentSource.FromString(Path.GetFileName(fixture), format, Plant(text, format));
    }

    private static void AssertNoCanary(ConfigurationValidationReport report, CapturingLogger logger)
    {
        Assert.All(report.Errors, error =>
        {
            Assert.DoesNotContain(Canary, error.Problem, StringComparison.Ordinal);
            Assert.DoesNotContain(Canary, error.Fix, StringComparison.Ordinal);
            Assert.DoesNotContain(Canary, error.Path, StringComparison.Ordinal);
            Assert.DoesNotContain(Canary, error.ToString(), StringComparison.Ordinal);
        });
        Assert.DoesNotContain(Canary, report.ToString(), StringComparison.Ordinal);
        Assert.All(logger.Events, captured =>
        {
            Assert.DoesNotContain(Canary, captured.Message, StringComparison.Ordinal);
            Assert.All(captured.State, pair => Assert.DoesNotContain(Canary, Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.Ordinal));
            Assert.DoesNotContain(Canary, captured.Exception?.ToString() ?? string.Empty, StringComparison.Ordinal);
        });
    }

    private static void AssertNoCanary(GatewayException exception)
    {
        Assert.DoesNotContain(Canary, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(exception.Error?.Diagnostics.Values ?? [], pair => pair.Value.Contains(Canary, StringComparison.Ordinal));
    }

    [Fact]
    public void PlantingReplacesStringValuesAndLeavesKeysNumbersAndStructure()
    {
        Assert.Equal($"{{\"host\": \"{Canary}\", \"n\": 3, \"a\": [\"{Canary}\"]}}", Plant("{\"host\": \"db\", \"n\": 3, \"a\": [\"x\"]}", ConfigurationDocumentFormat.Json));
        Assert.Equal(
            $"a:\n  host: {Canary}\n  q: '{Canary}'\n  n: 30\n  b: true\n  e: ~\n  list:\n    - {Canary}\n  m: {{ secretRef: {Canary} }}\n",
            Plant("a:\n  host: db.example\n  q: \"x y\"\n  n: 30\n  b: true\n  e: ~\n  list:\n    - blue\n  m: { secretRef: db/x }\n", ConfigurationDocumentFormat.Yaml));
    }

    [Theory]
    [MemberData(nameof(NegativeFixtures))]
    public void ThePlantedCanaryAppearsNowhere(string fixture)
    {
        var logger = new CapturingLogger();
        var environment = ConfigurationEnvironmentSource.FromDictionary(new Dictionary<string, string>
        {
            ["ISKENDER__PERSISTENCE__HOST"] = Canary,
            ["ISKENDER__PERSISTENCE__CONNECTIONTIMEOUT"] = Canary,
            ["ISKENDER__PERSISTENCE__PASSWORD"] = Canary,
            ["ISKENDER__NOPE__FIELD"] = Canary,
        });

        var report = ConfigurationValidator.Validate(
            new ConfigurationLoadRequest(TestSchemas.Registry(), Planted(fixture), environment: environment, environmentName: FixtureLoader.EnvironmentOf(fixture)),
            logger);

        AssertNoCanary(report, logger);
    }

    [Fact]
    public void EveryFixtureWhoseDefectIsInAValueReceivesTheCanary()
    {
        string[] valueFolders = ["/config.field.", "/config.secret.", "/config.value."];
        var fixtures = NegativeFixtures().Select(row => (string)row.Data!).Where(fixture => valueFolders.Any(folder => fixture.Contains(folder, StringComparison.Ordinal))).ToList();

        Assert.NotEmpty(fixtures);
        Assert.All(fixtures, fixture => Assert.Contains(Canary, Encoding.UTF8.GetString(Planted(fixture).Content), StringComparison.Ordinal));
    }

    [Fact]
    public void AnInvalidEnvironmentNameIsNeverLogged()
    {
        var logger = new CapturingLogger();
        var name = Canary + "\nforged log line " + new string('x', 5_000);

        var report = ConfigurationValidator.Validate(
            new ConfigurationLoadRequest(TestSchemas.Registry(), ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, "{"), environmentName: name),
            logger);

        Assert.Equal(["config.environment.invalid", "config.document.malformed"], report.Errors.Select(error => error.Code.Id));
        AssertNoCanary(report, logger);
        var rejected = Assert.Single(logger.Events, captured => captured.Id.Id == 3003);
        Assert.Equal("(none)", Assert.Single(rejected.State, pair => pair.Key == "Environment").Value);
        Assert.All(logger.Events, captured => Assert.DoesNotContain("forged", captured.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void AMalformedSecretReferenceNameIsNotInItsException()
    {
        AssertNoCanary(Assert.Throws<GatewayException>(() => new SecretReference($"{Canary}/../x")));
        AssertNoCanary(Assert.Throws<GatewayException>(() => new SecretReference(new string('a', 257) + Canary)));
    }

    [Fact]
    public void ARegistryFaultDoesNotCarryADefaultValue()
    {
        var schema = SectionSchema.Create("canary", "Tests.Canary", 1).WithFields(
            FieldSchema.Create("name", ConfigurationFieldKind.Text).WithConstraints(FieldConstraints.None.WithLength(1, 4)).WithDefault(Canary));

        AssertNoCanary(Assert.Throws<GatewayException>(() => ConfigurationSchemaRegistry.Create([schema])));
    }

    [Fact]
    public void ASectionAccessFaultDoesNotCarryTheValue()
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), ConfigurationDocumentSource.FromString(
            "t.json", ConfigurationDocumentFormat.Json, $"{{\"iskender\":{{\"persistence\":{{\"host\":\"db\",\"pool\":{{\"name\":\"main\"}},\"tags\":[\"{Canary}\"]}},\"alpha\":{{\"name\":\"{Canary}\"}}}}}}")));
        var alpha = report.Snapshot!.GetSection(TestSchemas.Alpha);
        var persistence = report.Snapshot.GetSection(TestSchemas.Persistence);

        AssertNoCanary(Assert.Throws<GatewayException>(() => alpha.GetInt64("name")));
        AssertNoCanary(Assert.Throws<GatewayException>(() => alpha.GetSection("name")));
        AssertNoCanary(Assert.Throws<GatewayException>(() => persistence.GetString("tags")));
        AssertNoCanary(Assert.Throws<GatewayException>(() => persistence.GetInt64s("tags")));
    }
}
