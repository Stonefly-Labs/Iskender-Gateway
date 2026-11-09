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

    /// <summary>Where a string value's text sits in a document, between its quotes when it has them.</summary>
    internal readonly record struct Slot(int Start, int Length);

    /// <summary>Every string value in the document, in order. Keys, numbers, booleans, nulls, and structure are not slots.</summary>
    internal static IReadOnlyList<Slot> Slots(string text, ConfigurationDocumentFormat format)
    {
        if (format == ConfigurationDocumentFormat.Json)
        {
            return [.. JsonString().Matches(text).Where(match => !match.Groups["key"].Success).Select(match => new Slot(match.Index + 1, match.Length - 2))];
        }

        var quoted = YamlQuoted().Matches(text).ToList();
        var slots = quoted.Where(match => !match.Groups["key"].Success).Select(match => new Slot(match.Index + 1, match.Length - 2));
        var plain = YamlPlainValue().Matches(text).Select(match => match.Groups["value"])
            .Where(value => !quoted.Any(quote => value.Index < quote.Index + quote.Length && quote.Index < value.Index + value.Length))
            .Select(value => new Slot(value.Index, value.Length));
        return [.. slots.Concat(plain).OrderBy(slot => slot.Start)];
    }

    /// <summary>The document with each slot's text replaced by the matching value.</summary>
    internal static string Render(string text, IReadOnlyList<Slot> slots, IReadOnlyList<string> values)
    {
        var rendered = new StringBuilder();
        var at = 0;
        for (var index = 0; index < slots.Count; index++)
        {
            rendered.Append(text, at, slots[index].Start - at).Append(values[index]);
            at = slots[index].Start + slots[index].Length;
        }

        return rendered.Append(text, at, text.Length - at).ToString();
    }

    internal static string Plant(string text, ConfigurationDocumentFormat format)
    {
        var slots = Slots(text, format);
        return Render(text, slots, [.. slots.Select(_ => Canary)]);
    }

    /// <summary>
    /// The ways to plant the canary in one value, tried in order: in place of it, then around it, so that a defect in the
    /// value itself (a control character, a too-long string, a malformed reference name) survives the planting.
    /// </summary>
    private static string[] Candidates(string value) => [Canary, Canary + value, value + Canary, value + "/" + Canary];

    /// <summary>A fixture with the canary planted, the variables its sidecar names, and every value the canary went into.</summary>
    private sealed record PlantedFixture(ConfigurationDocumentSource Document, ConfigurationEnvironmentSource? Variables, string? EnvironmentName, IReadOnlyList<string> PlantedValues)
    {
        public ConfigurationValidationReport Validate(CapturingLogger? logger = null) =>
            ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), Document, environment: Variables, environmentName: EnvironmentName), logger);
    }

    private static List<(string Code, string Path)> CodesAndPaths(IEnumerable<ExpectedError> errors) => [.. errors.Select(error => (error.Code, error.Path))];

    /// <summary>
    /// The runs for one fixture: <see cref="KeepsDefect"/> holds the canary wherever it keeps the fixture's defect, and
    /// <see cref="Separate"/> holds one run per value that run could not hold, with the canary in place of that value, so
    /// every string value carries the canary in at least one run. <see cref="Values"/> counts the document's string values
    /// and the sidecar's variables.
    /// </summary>
    private sealed record CanaryRuns(PlantedFixture KeepsDefect, IReadOnlyList<PlantedFixture> Separate, int Values);

    /// <summary>
    /// Plants the canary in every string value of the document and of the sidecar's variables where it keeps the fixture's
    /// defect: one value at a time, the first candidate that leaves the sidecar's codes and paths unchanged is kept, and a
    /// value no candidate can hold keeps its text there and gets a separate run. Lines and columns move with the planted
    /// text, so only they may change in the run that keeps the defect.
    /// </summary>
    private static CanaryRuns Planted(string fixture)
    {
        var expected = CodesAndPaths(FixtureLoader.Expected(fixture));
        var name = Path.GetFileName(fixture);
        var format = FixtureLoader.FormatOf(fixture);
        var environmentName = FixtureLoader.EnvironmentOf(fixture);
        var variables = FixtureLoader.VariablesOf(fixture)?.Variables.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList() ?? [];
        var variableValues = variables.Select(pair => pair.Value).ToArray();
        var bytes = File.ReadAllBytes(FixtureLoader.FullPath(fixture));
        string? text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Invalid UTF-8 cannot hold a planted string, so the document is read as it is.
            text = null;
        }

        var slots = text is null ? [] : Slots(text, format);
        var values = slots.Select(slot => text!.Substring(slot.Start, slot.Length)).ToArray();

        PlantedFixture Current(List<string> planted) => new(
            text is null ? ConfigurationDocumentSource.FromBytes(name, format, bytes) : ConfigurationDocumentSource.FromString(name, format, Render(text, slots, values)),
            variables.Count == 0 ? null : ConfigurationEnvironmentSource.FromDictionary([.. variables.Select((pair, index) => KeyValuePair.Create(pair.Key, variableValues[index]))]),
            environmentName,
            planted);

        bool KeepsDefect() => CodesAndPaths(Current([]).Validate().Errors.Select(ExpectedError.Of)).SequenceEqual(expected);

        Assert.True(KeepsDefect(), $"{fixture} does not match its sidecar before planting.");
        var planted = new List<string>();
        var unplanted = new List<(string[] Target, int Index)>();
        foreach (var target in new[] { values, variableValues })
        {
            for (var index = 0; index < target.Length; index++)
            {
                var original = target[index];
                var held = false;
                foreach (var candidate in Candidates(original))
                {
                    target[index] = candidate;
                    if (KeepsDefect())
                    {
                        planted.Add(candidate);
                        held = true;
                        break;
                    }

                    target[index] = original;
                }

                if (!held)
                {
                    unplanted.Add((target, index));
                }
            }
        }

        var keepsDefect = Current(planted);
        var separate = new List<PlantedFixture>();
        foreach (var (target, index) in unplanted)
        {
            var original = target[index];
            target[index] = Canary;
            separate.Add(Current([Canary]));
            target[index] = original;
        }

        return new CanaryRuns(keepsDefect, separate, values.Length + variableValues.Length);
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
            $"a:\n  host: {Canary}\n  q: \"{Canary}\"\n  n: 30\n  b: true\n  e: ~\n  list:\n    - {Canary}\n  m: {{ secretRef: {Canary} }}\n",
            Plant("a:\n  host: db.example\n  q: \"x y\"\n  n: 30\n  b: true\n  e: ~\n  list:\n    - blue\n  m: { secretRef: db/x }\n", ConfigurationDocumentFormat.Yaml));
    }

    [Theory]
    [MemberData(nameof(NegativeFixtures))]
    public void ThePlantedCanaryAppearsNowhere(string fixture)
    {
        var logger = new CapturingLogger();

        var report = Planted(fixture).KeepsDefect.Validate(logger);

        Assert.Equal(CodesAndPaths(FixtureLoader.Expected(fixture)), CodesAndPaths(report.Errors.Select(ExpectedError.Of)));
        AssertNoCanary(report, logger);
    }

    /// <summary>A value whose canary changes the fixture's errors is run on its own, and only the absence of the canary is asserted.</summary>
    [Theory]
    [MemberData(nameof(NegativeFixtures))]
    public void ACanaryThatChangesTheErrorsAppearsNowhereInItsOwnRun(string fixture)
    {
        Assert.All(Planted(fixture).Separate, run =>
        {
            var logger = new CapturingLogger();
            AssertNoCanary(run.Validate(logger), logger);
        });
    }

    /// <summary>
    /// Every string value of every negative fixture, in its document and in its sidecar's variables, carries the canary in
    /// at least one run, and every fixture with any string value carries it somewhere.
    /// </summary>
    [Fact]
    public void EveryValueOfEveryNegativeFixtureReceivesTheCanary()
    {
        var fixtures = NegativeFixtures().Select(row => (string)row.Data!).ToList();

        Assert.NotEmpty(fixtures);
        Assert.All(fixtures, fixture =>
        {
            var runs = Planted(fixture);
            Assert.Equal(runs.Values, runs.KeepsDefect.PlantedValues.Count + runs.Separate.Count);
            Assert.All(runs.KeepsDefect.PlantedValues.Concat(runs.Separate.SelectMany(run => run.PlantedValues)), value => Assert.Contains(Canary, value, StringComparison.Ordinal));
            Assert.All(runs.Separate.Prepend(runs.KeepsDefect).Where(run => run.PlantedValues.Count > 0), run => Assert.Contains(Canary, Carried(run), StringComparison.Ordinal));
        });
    }

    /// <summary>The document's text and the variables' values that a run validates.</summary>
    private static string Carried(PlantedFixture run) =>
        Encoding.UTF8.GetString(run.Document.Content) + string.Concat(run.Variables?.Variables.Values ?? []);

    [Fact]
    public void AFixtureWithAValueTheDefectKeepingRunCannotHoldGetsASeparateRun()
    {
        var runs = Planted("edge/required-in-unmentioned-object.json");

        Assert.True(runs.Values > 0);
        Assert.NotEmpty(runs.Separate);
        Assert.Equal(runs.Values, runs.KeepsDefect.PlantedValues.Count + runs.Separate.Count);
    }

    [Theory]
    [InlineData("edge/raw-control-character.json", "ab\u0001cd")]
    [InlineData("edge/escaped-control-in-string.json", "ab\\u0000cd")]
    [InlineData("edge/variable-errors-do-not-stop-validation.json", "bad\u0001text")]
    [InlineData("invalid/config.document.too_large/string.json", "aaaaaaaa")]
    [InlineData("invalid/config.secret.reference_malformed/name-too-long.json", "aaaaaaaa")]
    [InlineData("invalid/secret-ref-traversal.yaml", "db/../admin")]
    public void TheCanaryIsPlantedInsideADefectiveValue(string fixture, string defect)
    {
        Assert.Contains(Planted(fixture).KeepsDefect.PlantedValues, value => value.Contains(Canary, StringComparison.Ordinal) && value.Contains(defect, StringComparison.Ordinal));
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
