using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

public class SnapshotTests
{
    private const string Full =
        "{\"iskender\":{\"alpha\":{\"count\":2},\"persistence\":{\"host\":\"db.example\",\"pool\":{\"name\":\"main\"},\"sampleRatio\":0.25," +
        "\"endpoint\":\"postgres://db.example/app\",\"password\":{\"secretRef\":\"db/primary\"},\"idleTimeout\":\"90s\"," +
        "\"replicas\":[{\"host\":\"r1\"},{\"host\":\"r2\",\"weight\":3}],\"tags\":[\"a\",\"b\"]}}}";

    private static ValidatedSnapshot Snapshot(string json = Full, string? environment = "production")
    {
        var (errors, _, sections) = SchemaValidatorTests.Run(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, json));
        Assert.Empty(errors);
        return ValidatedSnapshot.Create(sections, environment, new ConfigurationLayers("t.json", null, 0));
    }

    private static void AssertInvalidAccess(Action action)
    {
        var exception = Assert.Throws<GatewayException>(action);
        Assert.Equal(ErrorCodes.ConfigSectionInvalidAccess, exception.Code);
        Assert.Equal(ErrorCategory.Internal, exception.Code.Category);
    }

    [Fact]
    public void TheRevisionIsCfgAndFiftyTwoBase32CharactersOfTheCanonicalHash()
    {
        var snapshot = Snapshot();

        Assert.StartsWith("cfg-", snapshot.Revision.Value, StringComparison.Ordinal);
        Assert.Equal(56, snapshot.Revision.Value.Length);
        Assert.Equal("cfg-" + Base32Lower.Encode(System.Security.Cryptography.SHA256.HashData(snapshot.Canonical)), snapshot.Revision.Value);
        Assert.Equal(snapshot.Revision, Snapshot().Revision);
    }

    [Fact]
    public void TheSnapshotRecordsEnvironmentNamespacesAndLayers()
    {
        var snapshot = Snapshot();

        Assert.Equal("production", snapshot.EnvironmentName);
        Assert.Equal(["alpha@1", "beta@1", "persistence@1"], snapshot.Namespaces.Select(version => version.ToString()));
        Assert.Equal(new ConfigurationLayers("t.json", null, 0), snapshot.Layers);
    }

    [Fact]
    public void TheEnvironmentAndSourceNamesAreNotPartOfTheRevision()
    {
        var (_, _, sections) = SchemaValidatorTests.Run(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, Full));

        Assert.Equal(Snapshot().Revision, ValidatedSnapshot.Create(sections, null, new ConfigurationLayers("other.yaml", "o.yaml", 4)).Revision);
    }

    [Fact]
    public void TypedGettersReturnValuesDefaultsAndNull()
    {
        var section = Snapshot().GetSection(TestSchemas.Persistence);

        Assert.Equal("iskender.persistence", section.Path);
        Assert.Equal("persistence", section.Namespace);
        Assert.Equal(1, section.SchemaVersion);
        Assert.Equal("db.example", section.GetString("host"));
        Assert.Equal(30, section.GetInt64("connectionTimeout"));
        Assert.Equal(0.25m, section.GetDecimal("sampleRatio"));
        Assert.False(section.GetBoolean("readOnly"));
        Assert.Equal("primary", section.GetString("mode"));
        Assert.Equal(TimeSpan.FromSeconds(90), section.GetDuration("idleTimeout"));
        Assert.Equal(new Uri("postgres://db.example/app"), section.GetUri("endpoint"));
        Assert.Equal("secretRef:db/primary", section.GetSecretReference("password")!.ToString());
        Assert.Equal(["a", "b"], section.GetStrings("tags")!);
        var pool = section.GetSection("pool");
        Assert.Equal("iskender.persistence.pool", pool.Path);
        Assert.Equal(10, pool.GetInt64("size"));
        var replicas = section.GetSections("replicas")!;
        Assert.Equal(["iskender.persistence.replicas[0]", "iskender.persistence.replicas[1]"], replicas.Select(replica => replica.Path));
        Assert.Equal([1L, 3L], replicas.Select(replica => replica.GetInt64("weight")!.Value));
    }

    [Fact]
    public void AbsentValuesWithNoDefaultAreNullAndANestedSectionIsNeverNull()
    {
        var section = Snapshot("{\"iskender\":{\"persistence\":{\"host\":\"h\",\"pool\":{\"name\":\"n\"}}}}").GetSection(TestSchemas.Persistence);

        Assert.Null(section.GetUri("endpoint"));
        Assert.Null(section.GetSecretReference("password"));
        Assert.Null(section.GetStrings("tags"));
        Assert.Null(section.GetSections("replicas"));
        Assert.NotNull(Snapshot().GetSection(TestSchemas.Alpha).GetString("name"));
        Assert.Null(Snapshot().GetSection(TestSchemas.Beta).GetString("name"));
    }

    [Fact]
    public void OnlyTheRegisteredSchemaInstanceReadsItsSection()
    {
        var snapshot = Snapshot();
        var copy = SectionSchema.Create("persistence", "Tests.Persistence", 1).WithFields([.. TestSchemas.Persistence.Fields]);

        AssertInvalidAccess(() => snapshot.GetSection(copy));
        AssertInvalidAccess(() => snapshot.GetSection(SectionSchema.Create("gamma", "G", 1)));
        AssertInvalidAccess(() => snapshot.GetSection(null!));
        Assert.False(typeof(SectionSchema).GetMethod(nameof(Equals), [typeof(object)])!.DeclaringType == typeof(SectionSchema));
    }

    [Fact]
    public void AnUndeclaredNameOrTheWrongGetterFailsWithNoValueInTheException()
    {
        var section = Snapshot().GetSection(TestSchemas.Persistence);

        AssertInvalidAccess(() => section.GetString("nope"));
        AssertInvalidAccess(() => section.GetInt64("host"));
        AssertInvalidAccess(() => section.GetString("pool"));
        AssertInvalidAccess(() => section.GetSection("host"));
        AssertInvalidAccess(() => section.GetInt64s("tags"));
        AssertInvalidAccess(() => section.GetStrings("host"));
        var exception = Assert.Throws<GatewayException>(() => section.GetInt64("host"));
        Assert.DoesNotContain("db.example", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(exception.Error!.Diagnostics.Values, pair => pair.Value.Contains("db.example", StringComparison.Ordinal));
    }

    [Fact]
    public void ArraysCannotBeChangedThroughACast()
    {
        var tags = Assert.IsAssignableFrom<IList<string>>(Snapshot().GetSection(TestSchemas.Persistence).GetStrings("tags"));

        Assert.True(tags.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => tags.Add("c"));
    }
}
