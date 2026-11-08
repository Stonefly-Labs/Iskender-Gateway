using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

public class YamlReaderTests
{
    private const string Required = "  persistence:\n    host: db.example\n    pool:\n      name: main\n";

    public static TheoryData<string> YamlNegativeFixtures() =>
        [.. FixtureLoader.Fixtures("invalid").Where(path => path.EndsWith(".yaml", StringComparison.Ordinal) && path.Contains("config.document.", StringComparison.Ordinal))];

    public static TheoryData<string> ScalarStyles() =>
        [.. FixtureLoader.Fixtures("valid").Where(path => path.Contains("/scalar-", StringComparison.Ordinal))];

    private static ConfigurationValidationReport Yaml(string text, string? environment = null) =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(), ConfigurationDocumentSource.FromString("t.yaml", ConfigurationDocumentFormat.Yaml, text), environmentName: environment));

    private static ConfigurationValidationReport Persistence(string field) =>
        Yaml("iskender:\n  persistence:\n    host: db.example\n    pool:\n      name: main\n    " + field + "\n");

    [Theory]
    [MemberData(nameof(YamlNegativeFixtures))]
    public void EachBannedConstructAndDuplicateKeyStopsAtItsFirstOccurrence(string fixture)
    {
        Assert.Equal(".yaml", Path.GetExtension(fixture));
        FixtureLoader.AssertMatchesSidecar(fixture);
    }

    [Fact]
    public void ThereIsAFixtureForEveryBannedConstruct()
    {
        string[] expected = ["alias", "anchor", "local-tag", "merge-key", "second-document", "standard-tag", "tag-directive", "yaml-directive"];

        Assert.Equal(expected, FixtureLoader.Fixtures("invalid/config.document.unsupported_construct")
            .Where(path => path.EndsWith(".yaml", StringComparison.Ordinal)).Select(Path.GetFileNameWithoutExtension));
    }

    [Theory]
    [MemberData(nameof(ScalarStyles))]
    public void EveryAllowedScalarStyleGivesTheSameText(string fixture)
    {
        var report = FixtureLoader.Validate(fixture);

        Assert.True(report.IsValid, report.ToString());
        Assert.Equal("first value", report.Snapshot!.GetSection(TestSchemas.Alpha).GetString("name"));
    }

    [Fact]
    public void AYamlDocumentAndAJsonDocumentWithTheSameContentGiveTheSameRevision()
    {
        var yaml = FixtureLoader.Validate("valid/scalar-plain.yaml");
        var json = ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), ConfigurationDocumentSource.FromString(
            "t.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"alpha\":{\"name\":\"first value\"},\"persistence\":{\"host\":\"db.example\",\"pool\":{\"name\":\"main\"}}}}")));

        Assert.Equal(json.Snapshot!.Revision, yaml.Snapshot!.Revision);
    }

    [Theory]
    [InlineData("no", "no")]
    [InlineData("yes", "yes")]
    [InlineData("0755", "0755")]
    [InlineData("1e3", "1e3")]
    [InlineData("'off'", "off")]
    public void APlainScalarInATextFieldKeepsItsExactText(string value, string expected)
    {
        var report = Yaml("iskender:\n  alpha:\n    name: " + value + "\n" + Required);

        Assert.True(report.IsValid, report.ToString());
        Assert.Equal(expected, report.Snapshot!.GetSection(TestSchemas.Alpha).GetString("name"));
    }

    [Fact]
    public void APlainScalarInAnEnumerationFieldIsItsExactText()
    {
        Assert.Equal("replica", Persistence("mode: replica").Snapshot!.GetSection(TestSchemas.Persistence).GetString("mode"));
    }

    [Theory]
    [InlineData("readOnly: yes")]
    [InlineData("readOnly: on")]
    [InlineData("readOnly: True")]
    [InlineData("readOnly: 'true'")]
    [InlineData("connectionTimeout: 0755")]
    [InlineData("connectionTimeout: 1e3")]
    [InlineData("connectionTimeout: 1.0")]
    [InlineData("connectionTimeout: \"30\"")]
    [InlineData("connectionTimeout: '30'")]
    public void YamlNeverCoercesAValueIntoAnotherType(string field)
    {
        var error = Assert.Single(Persistence(field).Errors);

        Assert.Equal("config.field.type_mismatch", error.Code.Id);
        Assert.Equal(6, error.Location.Line);
    }

    [Theory]
    [InlineData("readOnly: true", true)]
    [InlineData("readOnly: false", false)]
    public void APlainTrueOrFalseIsABoolean(string field, bool expected)
    {
        Assert.Equal(expected, Persistence(field).Snapshot!.GetSection(TestSchemas.Persistence).GetBoolean("readOnly"));
    }

    [Theory]
    [InlineData("endpoint: ~")]
    [InlineData("endpoint: null")]
    [InlineData("endpoint:")]
    public void TildeNullAndAnEmptyValueMeanAbsent(string field)
    {
        var report = Persistence(field);

        Assert.True(report.IsValid, report.ToString());
        Assert.Null(report.Snapshot!.GetSection(TestSchemas.Persistence).GetUri("endpoint"));
    }

    [Fact]
    public void AQuotedNullIsTheTextNull()
    {
        Assert.Equal("config.field.invalid_format", Assert.Single(Persistence("endpoint: 'null'").Errors).Code.Id);
    }

    [Fact]
    public void LocationsCountUnicodeScalarValues()
    {
        var error = Assert.Single(Yaml("iskender:\n  alpha: { name: \"\U0001F600é\", nope: 1 }\n" + Required).Errors);

        Assert.Equal("config.field.unknown", error.Code.Id);
        Assert.Equal(new ConfigurationSourceLocation("t.yaml", 2, 24, ConfigurationLayer.Base), error.Location);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# only a comment\n")]
    [InlineData("~\n")]
    public void AnEmptyDocumentIsAnEmptyRoot(string text)
    {
        var report = Yaml(text);

        Assert.Equal(["iskender.persistence.host", "iskender.persistence.pool.name"], report.Errors.Select(error => error.Path));
    }

    [Theory]
    [InlineData("- a\n- b\n")]
    [InlineData("just text\n")]
    [InlineData("iskender: [unclosed\n")]
    [InlineData("? [a, b]\n: c\n")]
    public void ANonMappingRootAComplexKeyOrBadSyntaxIsMalformed(string text)
    {
        var error = Assert.Single(Yaml(text).Errors);

        Assert.Equal("config.document.malformed", error.Code.Id);
        Assert.Equal("(document)", error.Path);
    }

    [Fact]
    public void AnEscapedControlCharacterIsMalformedAtItsString()
    {
        var error = Assert.Single(Yaml("iskender:\n  alpha:\n    name: \"a\\x00b\"\n").Errors);

        Assert.Equal("config.document.malformed", error.Code.Id);
        Assert.Equal((3, 11), (error.Location.Line, error.Location.Column));
    }

    [Fact]
    public void AParserErrorEchoesNoInput()
    {
        var report = Yaml("iskender:\n  alpha: \"ISKENDER-CANARY-7f3a\n");

        Assert.Equal("config.document.malformed", Assert.Single(report.Errors).Code.Id);
        Assert.DoesNotContain("CANARY", report.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DepthAndNodeLimitsApply()
    {
        var deep = string.Concat(Enumerable.Repeat("[", 33)) + string.Concat(Enumerable.Repeat("]", 33));
        Assert.Equal("config.document.too_large", Assert.Single(Yaml("iskender: " + deep + "\n").Errors).Code.Id);

        var many = "x: [" + string.Join(",", Enumerable.Repeat("1", 10_000)) + "]\n";
        Assert.Equal("config.document.too_large", Assert.Single(Yaml(many).Errors).Code.Id);
    }
}
