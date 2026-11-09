using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

public class SchemaValidatorTests
{
    private const string Base = "\"host\": \"db.example\", \"pool\": { \"name\": \"main\" }";
    private static readonly string[] BaseMembers = ["\"host\": \"db.example\"", "\"pool\": { \"name\": \"main\" }"];

    internal static (IReadOnlyList<ConfigurationFieldError> Errors, int Total, IReadOnlyList<(SectionSchema Schema, SectionValues Values)> Sections) Run(
        ConfigurationDocumentSource source, ConfigurationSchemaRegistry? registry = null, string? environment = null)
    {
        var read = source.Format == ConfigurationDocumentFormat.Yaml
            ? YamlDocumentReader.Read(source, ConfigurationLayer.Base)
            : JsonDocumentReader.Read(source, ConfigurationLayer.Base);
        Assert.Null(read.Error);
        var issues = new FieldIssues();
        var validator = new SchemaValidator(registry ?? TestSchemas.Registry(), environment, issues);
        var iskender = validator.Iskender(read.Root);
        if (iskender is not null)
        {
            validator.CheckUnknown(iskender);
        }

        var sections = validator.Validate(iskender);
        var (reported, total) = issues.Truncated();
        return (reported, total, sections);
    }

    private static (IReadOnlyList<ConfigurationFieldError> Errors, int Total, IReadOnlyList<(SectionSchema Schema, SectionValues Values)> Sections) Persistence(
        string fields, string? environment = null) =>
        Run(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json,
            $"{{\"iskender\":{{\"persistence\":{{{string.Join(",", BaseMembers.Where(member => !fields.StartsWith(member[..member.IndexOf(':', StringComparison.Ordinal)], StringComparison.Ordinal)).Append(fields).Where(part => part.Length > 0))}}}}}}}"),
            environment: environment);

    private static ConfigurationFieldError Single(string fields, string? environment = null) =>
        Assert.Single(Persistence(fields, environment).Errors);

    private static SectionValues PersistenceValues(string fields) =>
        Persistence(fields).Sections.Single(section => section.Schema.Namespace == "persistence").Values;

    private static readonly string[] FieldCodes =
        ["config.namespace.unknown", "config.field.unknown", "config.field.required", "config.field.type_mismatch", "config.field.out_of_range", "config.field.invalid_format"];

    public static TheoryData<string> Fixtures => new(
        FieldCodes
            .SelectMany(code => FixtureLoader.Fixtures("invalid/" + code))
            .Concat(["edge/empty-document.json", "edge/empty-root-missing-required.json", "edge/required-in-unmentioned-object.json",
                "edge/explicit-null-required.json", "edge/integer-too-large.json", "edge/fractional-integer.json", "edge/more-than-100-errors.json"]));

    [Theory]
    [InlineData("\"pool\": { \"name\": \"n\", \"secretRef\": \"x\" }")]
    [InlineData("\"pool\": { \"name\": \"n\", \"size\": 5, \"secretRef\": { \"a\": 1 } }")]
    public void AnObjectFieldHoldingASecretRefMemberReportsOnlyTheTypeMismatchNotItsPresentMembersAsMissing(string fields)
    {
        var error = Single(fields);

        Assert.Equal("config.field.type_mismatch", error.Code.Id);
        Assert.Equal("iskender.persistence.pool", error.Path);
    }

    [Fact]
    public void AnObjectFieldHoldingASecretRefMemberStillReportsItsUnknownMembers()
    {
        Assert.Equal(
            [("config.field.type_mismatch", "iskender.persistence.pool"), ("config.field.unknown", "iskender.persistence.pool.nmae")],
            Persistence("\"pool\": { \"nmae\": \"n\", \"secretRef\": \"x\" }").Errors.Select(error => (error.Code.Id, error.Path)).Order());
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EachFieldFixtureReportsExactlyItsSidecarErrors(string fixture)
    {
        var (errors, _, _) = Run(FixtureLoader.Document(fixture));

        Assert.Equal(FixtureLoader.Expected(fixture), errors.Select(ExpectedError.Of).ToList());
    }

    [Fact]
    public void MoreThanAHundredErrorsKeepTheFirstHundredAndTheTotal()
    {
        var (errors, total, _) = Run(FixtureLoader.Document("edge/more-than-100-errors.json"));

        Assert.Equal(100, errors.Count);
        Assert.Equal(120, total);
    }

    [Fact]
    public void AnEmptyRootIsValidWhenEveryRequiredFieldHasADefault()
    {
        var registry = ConfigurationSchemaRegistry.Create([TestSchemas.Alpha, TestSchemas.Beta]);

        Assert.Empty(Run(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{}}"), registry).Errors);
        Assert.Empty(Run(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, ""), registry).Errors);
    }

    [Fact]
    public void AValidSectionTypesEveryFieldAndWritesDefaultsIn()
    {
        var values = PersistenceValues(
            "\"connectionTimeout\": 45, \"sampleRatio\": 0.25, \"readOnly\": true, \"mode\": \"replica\", \"idleTimeout\": \"90s\", " +
            "\"endpoint\": \"postgres://db.example:5432/app\", \"password\": { \"secretRef\": \"db/primary\" }, " +
            "\"replicas\": [{ \"host\": \"r1\" }], \"tags\": [\"a\", \"b\"]").Values;

        Assert.Equal("db.example", values["host"]);
        Assert.Equal(45L, values["connectionTimeout"]);
        Assert.Equal(0.25m, values["sampleRatio"]);
        Assert.Equal(true, values["readOnly"]);
        Assert.Equal("replica", values["mode"]);
        Assert.Equal(TimeSpan.FromSeconds(90), values["idleTimeout"]);
        Assert.Equal(new Uri("postgres://db.example:5432/app"), values["endpoint"]);
        Assert.Equal(new SecretReference("db/primary"), values["password"]);
        Assert.Equal(false, values["trace"]);
        var pool = Assert.IsType<SectionValues>(values["pool"]);
        Assert.Equal(10L, pool.Values["size"]);
        var replica = Assert.IsType<SectionValues>(Assert.Single((IEnumerable<object>)values["replicas"]));
        Assert.Equal(1L, replica.Values["weight"]);
        Assert.Equal(["a", "b"], ((IEnumerable<object>)values["tags"]).Cast<string>());
    }

    [Fact]
    public void AbsentOptionalFieldsWithNoDefaultHaveNoValue()
    {
        var values = PersistenceValues("").Values;

        Assert.False(values.ContainsKey("endpoint"));
        Assert.False(values.ContainsKey("password"));
        Assert.False(values.ContainsKey("tags"));
    }

    [Fact]
    public void AnUnknownFieldFixNamesTheNearMiss()
    {
        var error = Single("\"conenctionTimeout\": 30");

        Assert.Equal(ErrorCodes.ConfigFieldUnknown, error.Code);
        Assert.Equal("did you mean connectionTimeout?", error.Fix);
    }

    [Fact]
    public void AnUnknownNestedFieldIsReportedWithItsPath()
    {
        var error = Assert.Single(Run(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json,
            "{\"iskender\":{\"persistence\":{\"host\":\"h\",\"pool\":{\"name\":\"n\",\"sise\":1},\"replicas\":[{\"host\":\"r\",\"wieght\":2}]}}}")).Errors
, e => e.Path.EndsWith("sise", StringComparison.Ordinal));
        Assert.Equal("iskender.persistence.pool.sise", error.Path);
        Assert.Contains(Persistence("\"replicas\":[{\"host\":\"r\",\"wieght\":2}]").Errors, e => e.Path == "iskender.persistence.replicas[0].wieght");
    }

    [Fact]
    public void AnObjectFieldHoldingASecretRefStillReportsItsOtherUndeclaredMembers()
    {
        var errors = Persistence("\"pool\": { \"name\": \"main\", \"secretRef\": \"a\", \"sise\": 2 }, \"replicas\": [{ \"secretRef\": \"b\", \"wieght\": 2 }]").Errors;

        Assert.Equal(
            [("config.field.type_mismatch", "iskender.persistence.pool"), ("config.field.unknown", "iskender.persistence.pool.sise"),
                ("config.field.type_mismatch", "iskender.persistence.replicas[0]"), ("config.field.unknown", "iskender.persistence.replicas[0].wieght")],
            errors.Where(error => error.Code != ErrorCodes.ConfigFieldRequired).Select(error => (error.Code.Id, error.Path)));
        Assert.DoesNotContain(errors, error => error.Path.EndsWith(".secretRef", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"connectionTimeout\": \"30\"")]
    [InlineData("\"connectionTimeout\": 1.0")]
    [InlineData("\"connectionTimeout\": 1e3")]
    [InlineData("\"connectionTimeout\": true")]
    [InlineData("\"connectionTimeout\": {}")]
    [InlineData("\"host\": 5")]
    [InlineData("\"readOnly\": \"true\"")]
    [InlineData("\"readOnly\": 1")]
    [InlineData("\"sampleRatio\": \"0.5\"")]
    [InlineData("\"idleTimeout\": 30")]
    [InlineData("\"endpoint\": 5")]
    [InlineData("\"tags\": \"a\"")]
    [InlineData("\"tags\": [1]")]
    [InlineData("\"tags\": [null]")]
    [InlineData("\"replicas\": [5]")]
    [InlineData("\"pool\": 5")]
    [InlineData("\"host\": { \"secretRef\": \"a\" }")]
    [InlineData("\"pool\": { \"secretRef\": \"a\" }")]
    public void AValueOfTheWrongTypeIsATypeMismatch(string fields)
    {
        Assert.Contains(Persistence(fields).Errors, error => error.Code == ErrorCodes.ConfigFieldTypeMismatch);
    }

    [Theory]
    [InlineData("\"connectionTimeout\": 0")]
    [InlineData("\"connectionTimeout\": 301")]
    [InlineData("\"connectionTimeout\": -9223372036854775809")]
    [InlineData("\"sampleRatio\": 1.01")]
    [InlineData("\"sampleRatio\": 0.12345678901234567890123456789012")]
    [InlineData("\"idleTimeout\": \"0s\"")]
    [InlineData("\"idleTimeout\": \"2d\"")]
    [InlineData("\"idleTimeout\": \"99999999999999999999d\"")]
    [InlineData("\"idleTimeout\": \"9999999999999d\"")]
    [InlineData("\"host\": \"\"")]
    [InlineData("\"tags\": [\"\"]")]
    [InlineData("\"replicas\": [{\"host\":\"a\"},{\"host\":\"b\"},{\"host\":\"c\"},{\"host\":\"d\"},{\"host\":\"e\"}]")]
    public void AValueOutsideItsRangeOrTypeIsOutOfRange(string fields)
    {
        Assert.Equal(ErrorCodes.ConfigFieldOutOfRange, Single(fields).Code);
    }

    [Fact]
    public void AStringLengthCountsUnicodeScalarValues()
    {
        Assert.Empty(Persistence("\"tags\": [\"" + string.Concat(Enumerable.Repeat("😀", 32)) + "\"]").Errors);
        Assert.Equal(ErrorCodes.ConfigFieldOutOfRange, Single("\"tags\": [\"" + string.Concat(Enumerable.Repeat("😀", 33)) + "\"]").Code);
    }

    [Fact]
    public void ANumberThatDecimalHoldsExactlyIsKeptExactly()
    {
        Assert.Equal(0.1234567890123456789012345678m, PersistenceValues("\"sampleRatio\": 0.1234567890123456789012345678").Values["sampleRatio"]);
        Assert.Equal(0.5m, PersistenceValues("\"sampleRatio\": 5e-1").Values["sampleRatio"]);
    }

    [Theory]
    [InlineData("\"idleTimeout\": \"030s\"")]
    [InlineData("\"idleTimeout\": \"30\"")]
    [InlineData("\"idleTimeout\": \"30 s\"")]
    [InlineData("\"idleTimeout\": \"-30s\"")]
    [InlineData("\"idleTimeout\": \"1.5s\"")]
    [InlineData("\"idleTimeout\": \"30S\"")]
    [InlineData("\"mode\": \"Primary\"")]
    [InlineData("\"host\": \"Db.example\"")]
    [InlineData("\"host\": \"-db\"")]
    [InlineData("\"endpoint\": \"db.example\"")]
    [InlineData("\"endpoint\": \"/var/run/db\"")]
    [InlineData("\"endpoint\": \"http://db.example/\"")]
    public void AValueBreakingItsFormIsAnInvalidFormat(string fields)
    {
        Assert.Equal(ErrorCodes.ConfigFieldInvalidFormat, Single(fields).Code);
    }

    [Fact]
    public void ASchemeIsComparedWithoutRegardToCase()
    {
        Assert.Empty(Persistence("\"endpoint\": \"HTTPS://db.example/\"").Errors);
    }

    private static readonly SectionSchema Links = SectionSchema.Create("links", "Tests.Links", 1).WithFields(
        FieldSchema.Create("file", ConfigurationFieldKind.Uri).WithConstraints(FieldConstraints.None.WithAllowedSchemes("file")),
        FieldSchema.Create("any", ConfigurationFieldKind.Uri));

    private static IReadOnlyList<ConfigurationFieldError> LinkErrors(string field, string value) =>
        Run(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json,
                $"{{\"iskender\":{{\"links\":{{\"{field}\":{System.Text.Json.JsonSerializer.Serialize(value)}}}}}}}"),
            ConfigurationSchemaRegistry.Create([Links])).Errors;

    [Theory]
    [InlineData("file", "c:\\x")]
    [InlineData("file", "C:/x")]
    [InlineData("any", "https://x/a b")]
    [InlineData("any", "https://x/a\\b")]
    [InlineData("any", "https://x/a\"b")]
    [InlineData("any", "https://x/a<b>")]
    [InlineData("any", "https://x/%zz")]
    [InlineData("any", "https://x/a%2")]
    [InlineData("any", "https://x/#a#b")]
    [InlineData("any", "https://x/[a]")]
    [InlineData("any", "https://x/caf\u00e9")]
    public void AUriOutsideRfc3986SyntaxOrWithAnotherWrittenSchemeIsAnInvalidFormat(string field, string value)
    {
        var error = Assert.Single(LinkErrors(field, value));

        Assert.Equal((ErrorCodes.ConfigFieldInvalidFormat, $"iskender.links.{field}"), (error.Code, error.Path));
    }

    [Theory]
    [InlineData("file", "file:///c:/x")]
    [InlineData("any", "https://user:p%40ss@[::1]:8443/a/b;c=d?q=1&r=%2F#frag")]
    [InlineData("any", "urn:isbn:0451450523")]
    [InlineData("any", "mailto:ops@example.com")]
    public void AUriInRfc3986SyntaxIsAccepted(string field, string value)
    {
        Assert.Empty(LinkErrors(field, value));
    }

    [Fact]
    public void ADefaultOutsideRfc3986SyntaxOrWithAnotherWrittenSchemeIsASchemaFault()
    {
        foreach (var (uri, scheme) in new[] { (new Uri("https://x/a b"), (string?)null), (new Uri("c:\\x"), "file") })
        {
            var constraints = scheme is null ? FieldConstraints.None : FieldConstraints.None.WithAllowedSchemes(scheme);
            var schema = SectionSchema.Create("links", "Tests.Links", 1).WithFields(
                FieldSchema.Create("u", ConfigurationFieldKind.Uri).WithConstraints(constraints).WithDefault(uri));

            Assert.Equal(ErrorCodes.ConfigSchemaInvalid, Assert.Throws<GatewayException>(() => ConfigurationSchemaRegistry.Create([schema])).Code);
        }
    }

    [Fact]
    public void EveryErrorIsCollectedAndOrderedBySourceLocation()
    {
        var errors = Run(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json,
            "{\"iskender\":{\"persistence\":{\n\"mode\":\"x\",\n\"connectionTimeout\":0,\n\"hots\":1,\n\"pool\":{}}}}")).Errors;

        Assert.Equal(
            ["config.field.required", "config.field.invalid_format", "config.field.out_of_range", "config.field.unknown", "config.field.required"],
            errors.Select(error => error.Code.Id));
        Assert.Equal([1, 2, 3, 4, 5], errors.Select(error => error.Location.Line));
        Assert.Equal(["iskender.persistence.host", "iskender.persistence.pool.name"], new[] { errors[0], errors[4] }.Select(error => error.Path));
    }

    [Fact]
    public void KeysOutsideTheIskenderRootAreNeverValidated()
    {
        Assert.Empty(Run(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json,
            $"{{\"host\":{{\"anything\":[1,\"x\"]}},\"iskender\":{{\"persistence\":{{{Base}}}}}}}")).Errors);
    }

    [Fact]
    public void ANonObjectIskenderOrSectionIsATypeMismatch()
    {
        Assert.Equal("iskender", Assert.Single(Run(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, "{\"iskender\":5}"),
            ConfigurationSchemaRegistry.Create([TestSchemas.Alpha])).Errors).Path);
        Assert.Equal("iskender.alpha", Assert.Single(Run(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"alpha\":[]}}"),
            ConfigurationSchemaRegistry.Create([TestSchemas.Alpha])).Errors).Path);
    }

    [Fact]
    public void AMissingRequiredFieldWithNoPresentAncestorIsLocatedAtNone()
    {
        var error = Assert.Single(Run(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, "{}"),
            ConfigurationSchemaRegistry.Create([SectionSchema.Create("x", "X", 1).WithFields(FieldSchema.Create("y", ConfigurationFieldKind.Text).Required())])).Errors);

        Assert.Equal(ConfigurationSourceLocation.None, error.Location);
        Assert.Equal("iskender.x.y", error.Path);
    }
}
