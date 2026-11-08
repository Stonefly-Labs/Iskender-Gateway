using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>
/// FR-035 and FR-036: one test per edge case in the spec's Edge Cases list, by name. Where an earlier fixture covers a
/// case, the test checks it through the public route again.
/// </summary>
public class EdgeCaseCoverageTests
{
    private const string Required = "\"persistence\":{\"host\":\"db\",\"pool\":{\"name\":\"main\"}}";

    private static ConfigurationValidationReport Json(string text, ConfigurationSchemaRegistry? registry = null) =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            registry ?? TestSchemas.Registry(), ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, text)));

    [Theory]
    [InlineData("edge/empty-document.json")]
    [InlineData("edge/empty-root-missing-required.json")]
    public void EmptyDocumentAndEmptyRoot(string fixture)
    {
        var report = FixtureLoader.AssertMatchesSidecar(fixture);

        Assert.All(report.Errors, error => Assert.Equal("config.field.required", error.Code.Id));
        Assert.True(ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([TestSchemas.Alpha, TestSchemas.Beta]), FixtureLoader.Document(fixture))).IsValid);
    }

    [Theory]
    [InlineData("invalid/config.document.malformed/trailing-comma.json")]
    [InlineData("invalid/config.document.malformed/comment.json")]
    public void MalformedDocument(string fixture)
    {
        Assert.Equal("config.document.malformed", Assert.Single(FixtureLoader.AssertMatchesSidecar(fixture).Errors).Code.Id);
    }

    [Fact]
    public void ExplicitNull()
    {
        FixtureLoader.AssertMatchesSidecar("edge/explicit-null-required.json");
    }

    [Theory]
    [InlineData("edge/integer-too-large.json", "config.field.out_of_range")]
    [InlineData("edge/fractional-integer.json", "config.field.type_mismatch")]
    public void NumberTooLargeOrFractional(string fixture, string code)
    {
        Assert.Equal(code, Assert.Single(FixtureLoader.AssertMatchesSidecar(fixture).Errors).Code.Id);
    }

    [Fact]
    public void JsonDuplicateKey()
    {
        Assert.Equal("config.document.duplicate_key", Assert.Single(FixtureLoader.AssertMatchesSidecar("edge/json-duplicate-key.json").Errors).Code.Id);
    }

    [Theory]
    [InlineData("edge/byte-order-mark.json")]
    [InlineData("edge/invalid-utf8.json")]
    [InlineData("edge/raw-control-character.json")]
    [InlineData("edge/escaped-control-in-string.json")]
    [InlineData("edge/escaped-control-in-key.json")]
    public void ByteOrderMarkInvalidUtf8AndControlCharacters(string fixture)
    {
        Assert.Equal("config.document.malformed", Assert.Single(FixtureLoader.AssertMatchesSidecar(fixture).Errors).Code.Id);
    }

    [Fact]
    public void TwoVariablesDifferingInCase()
    {
        var error = Assert.Single(FixtureLoader.AssertMatchesSidecar("invalid/config.overlay.conflict/two-casings.json").Errors);

        Assert.DoesNotContain("a.example", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("b.example", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoOverlayFileIsRecordedAsNone()
    {
        var report = FixtureLoader.Validate("valid/full.json", "production");

        Assert.True(report.IsValid, report.ToString());
        Assert.Null(report.Snapshot!.Layers.OverlaySource);
        Assert.Equal("full.json", report.Snapshot.Layers.BaseSource);
    }

    [Fact]
    public void MalformedEnvironmentName()
    {
        Assert.Equal("config.environment.invalid", Assert.Single(FixtureLoader.AssertMatchesSidecar("invalid/config.environment.invalid/upper-case.yaml").Errors).Code.Id);
    }

    [Fact]
    public void OneModuleRegisteringTwoVersions()
    {
        var versionTwo = SectionSchema.Create("alpha", TestSchemas.Alpha.OwnerModule, 2);

        var exception = Assert.Throws<GatewayException>(() => ConfigurationSchemaRegistry.Create([TestSchemas.Alpha, versionTwo]));

        Assert.Equal(ErrorCodes.ConfigSchemaDuplicateNamespace, exception.Code);
        Assert.Equal(ErrorCategory.Internal, exception.Code.Category);
    }

    [Fact]
    public void AnInvalidSectionSchema()
    {
        var requiredWithDefault = SectionSchema.Create("gamma", "Tests.Gamma", 1).WithFields(
            FieldSchema.Create("name", ConfigurationFieldKind.Text).Required().WithDefault("x"));
        var minimumAboveMaximum = SectionSchema.Create("gamma", "Tests.Gamma", 1).WithFields(
            FieldSchema.Create("count", ConfigurationFieldKind.WholeNumber).WithConstraints(FieldConstraints.None.WithIntegerRange(5, 1)));
        var developmentOnlySecret = SectionSchema.Create("gamma", "Tests.Gamma", 1).WithFields(
            FieldSchema.Create("key", ConfigurationFieldKind.SecretReference).DevelopmentOnly());

        Assert.All([requiredWithDefault, minimumAboveMaximum, developmentOnlySecret], schema =>
        {
            var exception = Assert.Throws<GatewayException>(() => ConfigurationSchemaRegistry.Create([schema]));
            Assert.Equal(ErrorCodes.ConfigSchemaInvalid, exception.Code);
            Assert.Equal(ErrorCategory.Internal, exception.Code.Category);
        });
    }

    [Fact]
    public void MoreThanOneHundredErrors()
    {
        var report = FixtureLoader.AssertMatchesSidecar("edge/more-than-100-errors.json");

        Assert.Equal(100, report.Errors.Count);
        Assert.True(report.TotalErrorCount > 100);
        Assert.Contains("more error(s) not shown.", report.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TwoValidationsOfTheSameContentOnTwoReplicas()
    {
        var content = File.ReadAllText(FixtureLoader.FullPath("valid/full.json"));

        // Each replica builds its own registry from its own schema instances.
        var first = Json(content, TestSchemas.Registry());
        var second = Json(content, ConfigurationSchemaRegistry.Create([.. TestSchemas.Registry().Schemas.Reverse()]));

        Assert.Equal(first.Snapshot!.Revision, second.Snapshot!.Revision);
    }

    [Fact]
    public void ACredentialLikeStringInANonSecretFieldIsAcceptedAsAKnownLimit()
    {
        // Known limit: only schema-declared secret fields are checked. Heuristic detection is post-MVP work.
        var report = Json("{\"iskender\":{\"alpha\":{\"name\":\"sk-live-4f9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c\"}," + Required + "}}");

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void APlaceholderWhoseSecretValueRotatesKeepsItsRevision()
    {
        // The validator never resolves a placeholder, so nothing but its name can reach the revision.
        var document = "{\"iskender\":{\"persistence\":{\"host\":\"db\",\"pool\":{\"name\":\"main\"},\"password\":{\"secretRef\":\"db/primary\"}}}}";

        var before = Json(document).Snapshot!;
        var after = Json(document).Snapshot!;

        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal("db/primary", after.GetSection(TestSchemas.Persistence).GetSecretReference("password")!.Name);
    }
}
