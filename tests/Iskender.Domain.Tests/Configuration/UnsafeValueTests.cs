using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>User Story 2: plaintext secrets, malformed placeholders, and development-only values are rejected.</summary>
public class UnsafeValueTests
{
    private static ConfigurationValidationReport Json(string password) =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), ConfigurationDocumentSource.FromString(
            "t.json", ConfigurationDocumentFormat.Json,
            "{\"iskender\":{\"persistence\":{\"host\":\"db\",\"pool\":{\"name\":\"main\"},\"password\":" + password + "}}}")));

    public static TheoryData<string> MalformedReferences() =>
        [.. FixtureLoader.Fixtures("invalid/config.secret.reference_malformed"), "invalid/secret-ref-traversal.yaml"];

    [Theory]
    [InlineData("invalid/secret-plaintext.yaml")]
    [InlineData("invalid/config.secret.plaintext/plaintext-password.json")]
    [InlineData("invalid/config.secret.plaintext/object-without-placeholder.json")]
    public void Scenario1APlainValueInASecretFieldIsPlaintextAndTheFixNamesAPlaceholder(string fixture)
    {
        var error = Assert.Single(FixtureLoader.AssertMatchesSidecar(fixture).Errors);

        Assert.Contains("secretRef placeholder", error.Fix, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MalformedReferences))]
    public void Scenario2AMalformedPlaceholderIsReferenceMalformed(string fixture)
    {
        Assert.Equal("config.secret.reference_malformed", Assert.Single(FixtureLoader.AssertMatchesSidecar(fixture).Errors).Code.Id);
    }

    [Theory]
    [InlineData("{\"secretRef\":\"/db\"}")]
    [InlineData("{\"secretRef\":\"db/\"}")]
    [InlineData("{\"secretRef\":\"db//primary\"}")]
    [InlineData("{\"secretRef\":\"..\"}")]
    [InlineData("{\"secretRef\":\"\"}")]
    [InlineData("{\"secretRef\":\"db primary\"}")]
    [InlineData("{\"secretRef\":true}")]
    [InlineData("{\"secretRef\":{\"name\":\"db\"}}")]
    [InlineData("{\"secretRef\":[\"db\"]}")]
    [InlineData("{\"secretRef\":null}")]
    [InlineData("{\"secretRef\":null,\"other\":null}")]
    public void EveryOtherMalformedNameIsReferenceMalformed(string placeholder)
    {
        Assert.Equal("config.secret.reference_malformed", Assert.Single(Json(placeholder).Errors).Code.Id);
    }

    [Theory]
    [InlineData("{\"secretRef\":null,\"x\":1}")]
    [InlineData("{\"x\":1,\"secretRef\":null}")]
    [InlineData("{\"secretRef\":5,\"x\":1}")]
    public void ANullSecretRefBesideAnotherMemberInASecretFieldIsReferenceMalformedNotPlaintext(string placeholder)
    {
        var error = Assert.Single(Json(placeholder).Errors);

        Assert.Equal(("config.secret.reference_malformed", "iskender.persistence.password"), (error.Code.Id, error.Path));
    }

    [Fact]
    public void ANullSecretRefBesideAnotherMemberInASecretFieldIsReferenceMalformedInYamlAndAtEveryLayer()
    {
        var yaml = ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), ConfigurationDocumentSource.FromString(
            "t.yaml", ConfigurationDocumentFormat.Yaml, "iskender:\n  persistence:\n    host: db\n    pool: {name: main}\n    password: {secretRef: null, x: 1}\n")));
        var overlay = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(),
            ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json,
                "{\"iskender\":{\"persistence\":{\"host\":\"db\",\"pool\":{\"name\":\"main\"},\"password\":{\"secretRef\":\"good/name\"}}}}"),
            ConfigurationDocumentSource.FromString("o.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"persistence\":{\"password\":{\"secretRef\":null,\"x\":1}}}}")));

        Assert.Equal("config.secret.reference_malformed", Assert.Single(yaml.Errors).Code.Id);
        var error = Assert.Single(overlay.Errors);
        Assert.Equal(("config.secret.reference_malformed", ConfigurationLayer.Overlay), (error.Code.Id, error.Location.Layer));
    }

    [Fact]
    public void ANullSecretRefBesideOtherMembersOfAnObjectIsDroppedLikeAnyOtherNull()
    {
        var withNull = ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), ConfigurationDocumentSource.FromString(
            "t.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"persistence\":{\"host\":\"db\",\"pool\":{\"name\":\"main\",\"secretRef\":null}}}}")));
        var without = ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), ConfigurationDocumentSource.FromString(
            "t.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"persistence\":{\"host\":\"db\",\"pool\":{\"name\":\"main\"}}}}")));

        Assert.True(withNull.IsValid, withNull.ToString());
        Assert.Equal(without.Snapshot!.Revision, withNull.Snapshot!.Revision);
    }

    [Fact]
    public void ANameOfExactly256CharactersIsAccepted()
    {
        var report = Json("{\"secretRef\":\"" + new string('a', 256) + "\"}");

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void Scenario3APlaceholderInAFieldNotDeclaredSecretIsATypeMismatch()
    {
        var error = Assert.Single(FixtureLoader.AssertMatchesSidecar("invalid/secret-in-plain-field.yaml").Errors);

        Assert.Contains("secretRef placeholder", error.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSnapshotHoldsOnlyTheReferenceName()
    {
        var reference = Json("{\"secretRef\":\"db/primary\"}").Snapshot!.GetSection(TestSchemas.Persistence).GetSecretReference("password");

        Assert.Equal(new SecretReference("db/primary"), reference);
        Assert.Equal("db/primary", reference!.Name);
        Assert.Equal("secretRef:db/primary", reference.ToString());
    }

    [Fact]
    public void ChangingOnlyAPlaceholderNameChangesTheRevision()
    {
        var first = Json("{\"secretRef\":\"db/primary\"}").Snapshot!.Revision;

        Assert.NotEqual(first, Json("{\"secretRef\":\"db/replica\"}").Snapshot!.Revision);
        Assert.Equal(first, Json("{ \"secretRef\" : \"db/primary\" }").Snapshot!.Revision);
    }

    [Theory]
    [InlineData("production")]
    [InlineData(null)]
    [InlineData("staging")]
    [InlineData("develop")]
    public void Scenario4ADevelopmentOnlyValueOutsideDevelopmentIsUnsafe(string? environment)
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(), FixtureLoader.Document("invalid/dev-only-in-production.yaml"), environmentName: environment));

        var error = Assert.Single(report.Errors);
        Assert.Equal("config.value.unsafe_for_environment", error.Code.Id);
        Assert.Equal("iskender.persistence.trace", error.Path);
    }

    [Fact]
    public void Scenario4ADevelopmentOnlyValuePassesInDevelopment()
    {
        var report = FixtureLoader.Validate("invalid/dev-only-in-production.yaml", "development");

        Assert.True(report.IsValid, report.ToString());
        Assert.True(report.Snapshot!.GetSection(TestSchemas.Persistence).GetBoolean("trace"));
    }

    [Fact]
    public void ADevelopmentOnlyFieldAtItsDefaultIsSafeEverywhere()
    {
        var report = Json("{\"secretRef\":\"db\"}");

        Assert.True(report.IsValid, report.ToString());
        Assert.False(report.Snapshot!.GetSection(TestSchemas.Persistence).GetBoolean("trace"));
    }

    [Theory]
    [InlineData("{\"debug\":{\"level\":5}}", "production", true)]
    [InlineData("{\"debug\":{\"level\":5}}", null, true)]
    [InlineData("{\"debug\":{\"inner\":{\"on\":true}}}", "staging", true)]
    [InlineData("{\"debug\":{\"level\":5}}", "development", false)]
    [InlineData("{\"debug\":{}}", "production", false)]
    [InlineData("{\"debug\":{\"level\":null,\"inner\":{}}}", "production", false)]
    [InlineData("{}", "production", false)]
    [InlineData("{\"debug\":{\"level\":1}}", "production", false)]
    [InlineData("{\"debug\":{\"level\":1,\"inner\":{\"on\":false}}}", null, false)]
    [InlineData("{\"debug\":{\"level\":1,\"inner\":{\"on\":true}}}", null, true)]
    [InlineData("{\"debug\":{\"note\":\"x\"}}", "production", true)]
    [InlineData("{\"debug\":{\"marks\":[]}}", "production", true)]
    public void ADevelopmentOnlyObjectIsUnsafeOutsideDevelopmentOnlyWhenItDiffersFromItsDefaults(string section, string? environment, bool unsafeValue)
    {
        var schema = SectionSchema.Create("diag", "Tests.Diag", 1).WithFields(
            FieldSchema.Create("debug", ConfigurationFieldKind.Section).DevelopmentOnly().WithFields(
                FieldSchema.Create("level", ConfigurationFieldKind.WholeNumber).WithDefault(1),
                FieldSchema.Create("note", ConfigurationFieldKind.Text),
                FieldSchema.Create("marks", ConfigurationFieldKind.Array).WithItems(FieldSchema.Create("mark", ConfigurationFieldKind.Text)),
                FieldSchema.Create("inner", ConfigurationFieldKind.Section).WithFields(
                    FieldSchema.Create("on", ConfigurationFieldKind.Boolean).WithDefault(false))));

        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([schema]),
            ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"diag\":" + section + "}}"),
            environmentName: environment));

        if (unsafeValue)
        {
            var error = Assert.Single(report.Errors);
            Assert.Equal("config.value.unsafe_for_environment", error.Code.Id);
            Assert.Equal("iskender.diag.debug", error.Path);
        }
        else
        {
            Assert.True(report.IsValid, report.ToString());
        }
    }

    [Theory]
    [InlineData("https://x.example/", "production", false)]
    [InlineData("https://x.example/", null, false)]
    [InlineData("https://admin:pw@x.example/", "production", true)]
    [InlineData("https://x.example/#debug", "production", true)]
    [InlineData("HTTPS://x.example/", "production", true)]
    [InlineData("https://X.EXAMPLE/", null, true)]
    [InlineData("https://x.example", "staging", true)]
    [InlineData("https://admin:pw@x.example/", "development", false)]
    public void ADevelopmentOnlyUriMatchesItsDefaultByItsExactText(string value, string? environment, bool unsafeValue)
    {
        var schema = SectionSchema.Create("diag", "Tests.Diag", 1).WithFields(
            FieldSchema.Create("target", ConfigurationFieldKind.Uri).WithDefault(new Uri("https://x.example/")).DevelopmentOnly());

        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([schema]),
            ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"diag\":{\"target\":\"" + value + "\"}}}"),
            environmentName: environment));

        if (unsafeValue)
        {
            var error = Assert.Single(report.Errors);
            Assert.Equal("config.value.unsafe_for_environment", error.Code.Id);
            Assert.Equal("iskender.diag.target", error.Path);
        }
        else
        {
            Assert.True(report.IsValid, report.ToString());
        }
    }

    [Theory]
    [InlineData("invalid/config.environment.invalid/empty.yaml")]
    [InlineData("invalid/config.environment.invalid/too-long.yaml")]
    [InlineData("invalid/config.environment.invalid/upper-case.yaml")]
    [InlineData("invalid/config.environment.invalid/underscore.yaml")]
    public void AnInvalidEnvironmentNameIsReportedFirstAlongsideDocumentErrorsAndStopsFieldValidation(string fixture)
    {
        var error = Assert.Single(FixtureLoader.AssertMatchesSidecar(fixture).Errors);
        Assert.Equal(ConfigurationSourceLocation.None, error.Location);

        var withDocumentError = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(), ConfigurationDocumentSource.FromString("bad.json", ConfigurationDocumentFormat.Json, "{,"),
            environmentName: FixtureLoader.EnvironmentOf(fixture)));
        Assert.Equal(["config.environment.invalid", "config.document.malformed"], withDocumentError.Errors.Select(e => e.Code.Id));

        var withFieldErrors = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(), ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"nope\":1}}"),
            environmentName: FixtureLoader.EnvironmentOf(fixture)));
        Assert.Equal("config.environment.invalid", Assert.Single(withFieldErrors.Errors).Code.Id);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("production")]
    [InlineData("eu-west-2")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    public void AValidEnvironmentNameIsAccepted(string environment)
    {
        Assert.True(ConfigurationValidator.IsEnvironmentName(environment));
    }
}
