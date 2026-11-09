using System.Globalization;
using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>FR-015 and User Story 4, scenario 6: how <c>ISKENDER__</c> variables map onto declared paths.</summary>
public class EnvironmentVariableTests
{
    private static readonly SectionSchema Dashed = SectionSchema.Create("my-ns", "Tests.Dashed", 1).WithFields(
        FieldSchema.Create("timeout", ConfigurationFieldKind.WholeNumber));

    private static ConfigurationSchemaRegistry Registry() =>
        ConfigurationSchemaRegistry.Create([TestSchemas.Alpha, TestSchemas.Beta, TestSchemas.Persistence, Dashed]);

    private static (ObjectNode? Tree, int Applied, IReadOnlyList<ConfigurationFieldError> Errors) Read(params (string Name, string Value)[] variables)
    {
        var issues = new FieldIssues();
        var source = ConfigurationEnvironmentSource.FromDictionary(variables.Select(variable => KeyValuePair.Create(variable.Name, variable.Value)));
        var (tree, applied) = EnvironmentVariableReader.Read(source, Registry(), issues);
        return (tree, applied, issues.Ordered());
    }

    private static CanonicalNode? At(ObjectNode? tree, params string[] keys)
    {
        CanonicalNode? node = tree;
        foreach (var key in keys)
        {
            node = (node as ObjectNode)?.Find(key)?.Value;
        }

        return node;
    }

    private static ConfigurationFieldError Single(string name, string value = "x")
    {
        var (tree, applied, errors) = Read((name, value));

        Assert.Null(tree);
        Assert.Equal(0, applied);
        var error = Assert.Single(errors);
        Assert.Equal(new ConfigurationSourceLocation(name, 0, 0, ConfigurationLayer.Environment), error.Location);
        return error;
    }

    [Theory]
    [InlineData("PERSISTENCE__HOST")]
    [InlineData("ISKENDER_PERSISTENCE__HOST")]
    [InlineData("ISKENDERX__PERSISTENCE__HOST")]
    [InlineData("XISKENDER__PERSISTENCE__HOST")]
    [InlineData("PATH")]
    public void Scenario6AVariableWithoutThePrefixIsIgnored(string name)
    {
        var (tree, applied, errors) = Read((name, "x"));

        Assert.Null(tree);
        Assert.Equal(0, applied);
        Assert.Empty(errors);
    }

    [Fact]
    public void Scenario6TheFixtureWithUnprefixedVariablesIsValid()
    {
        var report = FixtureLoader.Validate("edge/variables-without-prefix.json");

        Assert.True(report.IsValid, report.ToString());
        Assert.Empty(FixtureLoader.Expected("edge/variables-without-prefix.json"));
    }

    [Theory]
    [InlineData("ISKENDER__PERSISTENCE__CONNECTIONTIMEOUT")]
    [InlineData("iskender__persistence__connectiontimeout")]
    [InlineData("Iskender__Persistence__ConnectionTimeout")]
    public void ThePrefixAndEverySegmentMatchWithoutRegardToCase(string name)
    {
        var (tree, applied, errors) = Read((name, "45"));

        Assert.Empty(errors);
        Assert.Equal(1, applied);
        var scalar = Assert.IsType<ScalarNode>(At(tree, "persistence", "connectionTimeout"));
        Assert.Equal("45", scalar.Text);
        Assert.Equal(ScalarKind.Plain, scalar.Kind);
        Assert.Equal(new ConfigurationSourceLocation(name, 0, 0, ConfigurationLayer.Environment), scalar.Location);
    }

    [Fact]
    public void ASingleUnderscoreInTheFirstSegmentMatchesAHyphenInTheNamespace()
    {
        var (tree, _, errors) = Read(("ISKENDER__MY_NS__TIMEOUT", "5"));

        Assert.Empty(errors);
        Assert.Equal("5", Assert.IsType<ScalarNode>(At(tree, "my-ns", "timeout")).Text);
    }

    [Fact]
    public void AnUnderscoreInAFieldSegmentMatchesNothing()
    {
        Assert.Equal("config.field.unknown", Single("ISKENDER__PERSISTENCE__CONNECTION_TIMEOUT").Code.Id);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    [InlineData("")]
    public void MatchingIsTheSameUnderEveryCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var (tree, _, errors) = Read(("iskender__PERSISTENCE__HOST", "db"), ("ISKENDER__persistence__IDLETIMEOUT", "5m"), ("ISKENDER__ALPHA__NAME", "i"));

            Assert.Empty(errors);
            Assert.NotNull(At(tree, "persistence", "host"));
            Assert.NotNull(At(tree, "persistence", "idleTimeout"));
            Assert.NotNull(At(tree, "alpha", "name"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("ISKENDER__PERSISTENCE__POOL", "iskender.persistence.pool")]
    [InlineData("ISKENDER__PERSISTENCE__TAGS", "iskender.persistence.tags")]
    [InlineData("ISKENDER__PERSISTENCE__REPLICAS__HOST", "iskender.persistence.replicas")]
    [InlineData("ISKENDER__PERSISTENCE", "iskender.persistence")]
    public void AVariableSetsAScalarFieldOnly(string name, string path)
    {
        var error = Single(name);

        Assert.Equal("config.field.type_mismatch", error.Code.Id);
        Assert.Equal(path, error.Path);
    }

    [Fact]
    public void AVariableReachesANestedScalarThroughAnObjectField()
    {
        var (tree, _, errors) = Read(("ISKENDER__PERSISTENCE__POOL__SIZE", "20"));

        Assert.Empty(errors);
        Assert.Equal("20", Assert.IsType<ScalarNode>(At(tree, "persistence", "pool", "size")).Text);
    }

    [Fact]
    public void ASecretRefSegmentSetsAPlaceholderWithTheTextAsItsName()
    {
        var (tree, _, errors) = Read(("ISKENDER__PERSISTENCE__PASSWORD__SECRETREF", "db/primary"));

        Assert.Empty(errors);
        var placeholder = Assert.IsType<ObjectNode>(At(tree, "persistence", "password"));
        var member = Assert.Single(placeholder.Members);
        Assert.Equal("secretRef", member.Key);
        Assert.Equal("db/primary", Assert.IsType<ScalarNode>(member.Value).Text);
    }

    [Fact]
    public void NamingASecretFieldDirectlySetsAPlainValueThatTheValidatorRejects()
    {
        var (tree, _, _) = Read(("ISKENDER__PERSISTENCE__PASSWORD", "sk-plaintext"));

        Assert.IsType<ScalarNode>(At(tree, "persistence", "password"));
    }

    [Fact]
    public void ASecretRefSegmentOnAnotherFieldSetsAPlaceholderThatTheValidatorRejects()
    {
        var (tree, _, _) = Read(("ISKENDER__PERSISTENCE__HOST__SECRETREF", "db/host"));

        Assert.True(SchemaValidator.IsPlaceholderShaped(Assert.IsType<ObjectNode>(At(tree, "persistence", "host"))));
    }

    [Fact]
    public void TwoVariablesForOnePathConflictAtTheFirstNameNamingBothAndNeitherValue()
    {
        var (tree, applied, errors) = Read(("iskender__persistence__host", "value-two"), ("ISKENDER__PERSISTENCE__HOST", "value-one"));

        Assert.Null(tree);
        Assert.Equal(0, applied);
        var error = Assert.Single(errors);
        Assert.Equal("config.overlay.conflict", error.Code.Id);
        Assert.Equal("iskender.persistence.host", error.Path);
        Assert.Equal("ISKENDER__PERSISTENCE__HOST", error.Location.Source);
        Assert.Contains("ISKENDER__PERSISTENCE__HOST", error.Problem, StringComparison.Ordinal);
        Assert.Contains("iskender__persistence__host", error.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("value-", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlainAndSecretRefFormsOfOneFieldConflict()
    {
        var (_, _, errors) = Read(("ISKENDER__PERSISTENCE__PASSWORD", "a"), ("ISKENDER__PERSISTENCE__PASSWORD__SECRETREF", "b"));

        Assert.Equal("config.overlay.conflict", Assert.Single(errors).Code.Id);
    }

    [Fact]
    public void TheConflictFixtureMatchesItsSidecar()
    {
        var error = Assert.Single(FixtureLoader.AssertMatchesSidecar("invalid/config.overlay.conflict/two-casings.json").Errors);

        Assert.Equal("ISKENDER__PERSISTENCE__HOST", error.Location.Source);
    }

    [Theory]
    [InlineData("ISKENDER__GAMMA__HOST", "config.namespace.unknown", "iskender.GAMMA")]
    [InlineData("ISKENDER__PERSISTENCE__HOTS", "config.field.unknown", "iskender.persistence.HOTS")]
    [InlineData("ISKENDER__PERSISTENCE__HOST__EXTRA", "config.field.unknown", "iskender.persistence.host.EXTRA")]
    [InlineData("ISKENDER__", "config.field.unknown", "iskender.")]
    [InlineData("ISKENDER__PERSISTENCE____HOST", "config.field.unknown", "iskender.PERSISTENCE..HOST")]
    [InlineData("ISKENDER__PERSISTENCE__HOST__", "config.field.unknown", "iskender.PERSISTENCE.HOST.")]
    public void UnknownNamesAndEmptySegmentsAreReportedInTheEnvironmentLayer(string name, string code, string path)
    {
        var error = Single(name);

        Assert.Equal(code, error.Code.Id);
        Assert.Equal(path, error.Path);
    }

    [Fact]
    public void AnUnknownFieldSuggestsTheClosestDeclaredName()
    {
        Assert.Contains("host", Single("ISKENDER__PERSISTENCE__HOTS").Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlCharactersAndOverlongTextAreDocumentErrorsAtTheMappedPath()
    {
        var (_, applied, errors) = Read(("ISKENDER__ALPHA__NAME", "a\u0000b"), ("ISKENDER__BETA__NAME", new string('x', 65_537)), ("ISKENDER__PERSISTENCE__HOST", "db"));

        Assert.Equal(1, applied);
        Assert.Equal(
            [("config.document.malformed", "iskender.alpha.name"), ("config.document.too_large", "iskender.beta.name")],
            errors.Select(error => (error.Code.Id, error.Path)));
    }

    [Fact]
    public void AVariableTextErrorNamesTheVariablesTextAndItsBrokenRule()
    {
        var (_, _, errors) = Read(("ISKENDER__ALPHA__NAME", "a\u0000b"), ("ISKENDER__BETA__NAME", new string('x', 65_537)), ("ISKENDER__PERSISTENCE__HOST", "a\uD800"));

        Assert.Equal(3, errors.Count);
        Assert.All(errors.Where(error => error.Code.Id == "config.document.malformed"), error =>
        {
            Assert.Equal("the variable's text holds a control character other than tab, line feed, or carriage return, or a lone surrogate", error.Problem);
            Assert.Equal("remove the control character or lone surrogate from the variable's text.", error.Fix);
        });
        var tooLarge = Assert.Single(errors, error => error.Code.Id == "config.document.too_large");
        Assert.Equal("the variable's text is longer than 65,536 UTF-8 bytes", tooLarge.Problem);
        Assert.Equal("shorten the variable's text to at most 65,536 UTF-8 bytes.", tooLarge.Fix);
        Assert.All(errors, error => Assert.DoesNotContain("document", error.Problem + error.Fix, StringComparison.Ordinal));
    }

    [Fact]
    public void AVariableWithMalformedTextStillConflictsWithAnotherOnItsPath()
    {
        var (tree, applied, errors) = Read(("ISKENDER__ALPHA__NAME", "\u0001"), ("ISKENDER__alpha__name", "6"));

        Assert.Null(tree);
        Assert.Equal(0, applied);
        Assert.Equal(
            [("config.document.malformed", "ISKENDER__ALPHA__NAME"), ("config.overlay.conflict", "ISKENDER__ALPHA__NAME")],
            errors.Select(error => (error.Code.Id, error.Location.Source)).Order());
        Assert.All(errors, error => Assert.Equal("iskender.alpha.name", error.Path));
    }

    // Attribute arguments are stored as UTF-8, which turns a lone surrogate into U+FFFD, so these are built in code.
    private static readonly string[] LoneSurrogateTexts = ["a\uD800", "\uDFFFa", "a\uDFFF\uD800b", "\uD800𐀀"];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TextThatIsNotWellFormedUtf16IsMalformedAtTheMappedPath(int index)
    {
        var text = LoneSurrogateTexts[index];
        var (_, applied, errors) = Read(("ISKENDER__ALPHA__NAME", text), ("ISKENDER__PERSISTENCE__PASSWORD__SECRETREF", text));

        Assert.Equal(0, applied);
        Assert.Equal(
            [("config.document.malformed", "iskender.alpha.name"), ("config.document.malformed", "iskender.persistence.password")],
            errors.Select(error => (error.Code.Id, error.Path)));
    }

    [Fact]
    public void LoneSurrogatesNoLongerShareARevisionAndASurrogatePairIsAccepted()
    {
        foreach (var text in new[] { "a\uD800", "a\uDFFF" })
        {
            var error = Assert.Single(Validate(("ISKENDER__ALPHA__NAME", text)).Errors);
            Assert.Equal(("config.document.malformed", "iskender.alpha.name"), (error.Code.Id, error.Path));
        }

        var (tree, applied, errors) = Read(("ISKENDER__ALPHA__NAME", "a😀"));
        Assert.Empty(errors);
        Assert.Equal(1, applied);
        Assert.Equal("a😀", Assert.IsType<ScalarNode>(At(tree, "alpha", "name")).Text);
    }

    [Fact]
    public void TabsAndLineBreaksAreAllowedAndTextOfExactly65536BytesIsAccepted()
    {
        var (_, applied, errors) = Read(("ISKENDER__ALPHA__NAME", "a\tb\r\nc"), ("ISKENDER__BETA__NAME", new string('x', 65_536)));

        Assert.Empty(errors);
        Assert.Equal(2, applied);
    }

    [Fact]
    public void AVariableErrorDoesNotStopFieldValidation()
    {
        FixtureLoader.AssertMatchesSidecar("edge/variable-errors-do-not-stop-validation.json");
    }

    [Theory]
    [InlineData("")]
    [InlineData("~")]
    [InlineData("null")]
    public void EmptyTildeOrNullTextMeansAbsent(string text)
    {
        var (tree, applied, errors) = Read(("ISKENDER__PERSISTENCE__ENDPOINT", text));

        Assert.Empty(errors);
        Assert.Null(tree);
        Assert.Equal(0, applied);
    }

    private static ConfigurationValidationReport Validate(params (string Name, string Value)[] variables) =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(),
            ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"persistence\":{\"host\":\"db\",\"pool\":{\"name\":\"main\"}}}}"),
            environment: ConfigurationEnvironmentSource.FromDictionary(variables.Select(variable => KeyValuePair.Create(variable.Name, variable.Value)))));

    [Fact]
    public void ThroughTheValidatorASecretRefVariableBecomesASecretReference()
    {
        var report = Validate(("ISKENDER__PERSISTENCE__PASSWORD__SECRETREF", "db/primary"));

        Assert.Equal(new SecretReference("db/primary"), report.Snapshot!.GetSection(TestSchemas.Persistence).GetSecretReference("password"));
    }

    [Fact]
    public void ThroughTheValidatorNamingASecretFieldDirectlyIsPlaintext()
    {
        var error = Assert.Single(Validate(("ISKENDER__PERSISTENCE__PASSWORD", "sk-plaintext")).Errors);

        Assert.Equal("config.secret.plaintext", error.Code.Id);
        Assert.Equal(ConfigurationLayer.Environment, error.Location.Layer);
    }

    [Fact]
    public void ThroughTheValidatorASecretRefSegmentOnAnotherFieldIsATypeMismatch()
    {
        Assert.Equal("config.field.type_mismatch", Assert.Single(Validate(("ISKENDER__PERSISTENCE__MODE__SECRETREF", "db/mode")).Errors).Code.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("~")]
    [InlineData("null")]
    public void ThroughTheValidatorAnEmptyTildeOrNullSecretRefIsAMalformedReference(string text)
    {
        var error = Assert.Single(Validate(("ISKENDER__PERSISTENCE__PASSWORD__SECRETREF", text)).Errors);

        Assert.Equal("config.secret.reference_malformed", error.Code.Id);
        Assert.Equal("iskender.persistence.password", error.Path);
        Assert.Equal(new ConfigurationSourceLocation("ISKENDER__PERSISTENCE__PASSWORD__SECRETREF", 0, 0, ConfigurationLayer.Environment), error.Location);
    }

    [Fact]
    public void ANullSecretRefOverABasePlaceholderIsAbsent()
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(),
            ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json,
                "{\"iskender\":{\"persistence\":{\"host\":\"db\",\"pool\":{\"name\":\"main\"},\"password\":{\"secretRef\":\"db/primary\"}}}}"),
            ConfigurationDocumentSource.FromString("o.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"persistence\":{\"password\":{\"secretRef\":null}}}}"),
            ConfigurationEnvironmentSource.FromDictionary([KeyValuePair.Create("ISKENDER__PERSISTENCE__PASSWORD__SECRETREF", "")])));

        Assert.True(report.IsValid, report.ToString());
        Assert.Equal(new SecretReference("db/primary"), report.Snapshot!.GetSection(TestSchemas.Persistence).GetSecretReference("password"));
    }

    [Fact]
    public void ASecretRefVariableOverABasePlaceholderIsLocatedAtTheVariableAndSortedWithTheEnvironmentErrors()
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(),
            ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json,
                "{\"iskender\":{\"persistence\":{\"host\":\"db\",\"connectionTimeout\":0,\"pool\":{\"name\":\"main\"},\"password\":{\"secretRef\":\"good/name\"}}}}"),
            environment: ConfigurationEnvironmentSource.FromDictionary([
                KeyValuePair.Create("ISKENDER__PERSISTENCE__PASSWORD__SECRETREF", "/bad"),
                KeyValuePair.Create("ISKENDER__PERSISTENCE__HOTS", "x")])));

        Assert.Equal(
            [("config.field.out_of_range", ConfigurationLayer.Base), ("config.field.unknown", ConfigurationLayer.Environment), ("config.secret.reference_malformed", ConfigurationLayer.Environment)],
            report.Errors.Select(error => (error.Code.Id, error.Location.Layer)));
        Assert.Equal(new ConfigurationSourceLocation("ISKENDER__PERSISTENCE__PASSWORD__SECRETREF", 0, 0, ConfigurationLayer.Environment), report.Errors[2].Location);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other: 1\n")]
    public void AMissingRequiredFieldUnderARootOnlyAVariableCreatedIsLocatedAtNoneAndSortedFirst(string? baseDocument)
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(),
            baseDocument is null ? null : ConfigurationDocumentSource.FromString("b.yaml", ConfigurationDocumentFormat.Yaml, baseDocument),
            environment: ConfigurationEnvironmentSource.FromDictionary([
                KeyValuePair.Create("ISKENDER__ALPHA__COUNT", "1"),
                KeyValuePair.Create("ISKENDER__GAMMA__X", "1")])));

        Assert.Equal(
            [("config.field.required", "iskender.persistence.host"), ("config.field.required", "iskender.persistence.pool.name"), ("config.namespace.unknown", "iskender.GAMMA")],
            report.Errors.Select(error => (error.Code.Id, error.Path)));
        Assert.Equal(ConfigurationSourceLocation.None, report.Errors[0].Location);
        Assert.Equal(ConfigurationSourceLocation.None, report.Errors[1].Location);
        Assert.Equal(ConfigurationLayer.Environment, report.Errors[2].Location.Layer);
    }

    private static readonly SectionSchema Rq = SectionSchema.Create("rq", "Tests.Rq", 1).WithFields(
        FieldSchema.Create("host", ConfigurationFieldKind.Text).Required(),
        FieldSchema.Create("n", ConfigurationFieldKind.WholeNumber),
        FieldSchema.Create("inner", ConfigurationFieldKind.Section).WithFields(
            FieldSchema.Create("name", ConfigurationFieldKind.Text).Required(),
            FieldSchema.Create("n", ConfigurationFieldKind.WholeNumber)));

    private static ConfigurationValidationReport ValidateRq(string? baseJson, params string[] variables) =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([Rq]),
            baseJson is null ? null : ConfigurationDocumentSource.FromString("b.json", ConfigurationDocumentFormat.Json, baseJson),
            environment: ConfigurationEnvironmentSource.FromDictionary(variables.Select(variable => KeyValuePair.Create(variable, "1")))));

    [Theory]
    [InlineData("ISKENDER__RQ__N")]
    [InlineData("ISKENDER__RQ__INNER__N")]
    public void AMissingRequiredFieldUnderANamespaceOnlyAVariableCreatedIsLocatedAtNone(string variable)
    {
        var report = ValidateRq(null, variable);

        Assert.Equal(["iskender.rq.host", "iskender.rq.inner.name"], report.Errors.Select(error => error.Path));
        Assert.All(report.Errors, error => Assert.Equal(("config.field.required", ConfigurationSourceLocation.None), (error.Code.Id, error.Location)));
    }

    [Theory]
    [InlineData("ISKENDER__RQ__N")]
    [InlineData("ISKENDER__RQ__INNER__N")]
    public void AMissingRequiredFieldUnderANamespaceOnlyAVariableCreatedIsLocatedAtTheNearestNodeADocumentHolds(string variable)
    {
        var report = ValidateRq("{\"iskender\":{}}", variable);
        var root = new ConfigurationSourceLocation("b.json", 1, 13, ConfigurationLayer.Base);

        Assert.Equal(["iskender.rq.host", "iskender.rq.inner.name"], report.Errors.Select(error => error.Path));
        Assert.All(report.Errors, error => Assert.Equal(("config.field.required", root), (error.Code.Id, error.Location)));

        var nested = ValidateRq("{\"iskender\":{\"rq\":{}}}", "ISKENDER__RQ__INNER__N");
        Assert.Equal(new ConfigurationSourceLocation("b.json", 1, 19, ConfigurationLayer.Base), Assert.Single(nested.Errors, error => error.Path == "iskender.rq.inner.name").Location);
    }

    [Theory]
    [InlineData("ISKENDER__PERSISTENCE__READONLY", "true", true)]
    [InlineData("ISKENDER__PERSISTENCE__READONLY", "yes", false)]
    [InlineData("ISKENDER__PERSISTENCE__CONNECTIONTIMEOUT", "45", true)]
    [InlineData("ISKENDER__PERSISTENCE__CONNECTIONTIMEOUT", "045", false)]
    [InlineData("ISKENDER__PERSISTENCE__IDLETIMEOUT", "90s", true)]
    public void ThroughTheValidatorTheTextIsAPlainScalarTypedByItsField(string name, string text, bool valid)
    {
        Assert.Equal(valid, Validate((name, text)).IsValid);
    }
}
