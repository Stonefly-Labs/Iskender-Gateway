using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;

namespace Iskender.Infrastructure.PostgreSql.Tests.Configuration;

public class PersistenceSectionTests
{
    private const string Required = "\"host\":\"db.internal\",\"database\":\"iskender\",\"username\":\"isk_runtime\",\"password\":{\"secretRef\":\"db/runtime\"}";

    private static string Doc(string extra = "") =>
        "{\"iskender\":{\"persistence\":{" + Required + (extra.Length > 0 ? "," + extra : "") + "}}}";

    private static string OnlyError(string document, string? environment = "development")
    {
        var report = TestSettings.Validate(document, environment);
        Assert.False(report.IsValid);
        return Assert.Single(report.Errors).Code.Id;
    }

    [Fact]
    public void DefaultsAreTheContractsSafetyBounds()
    {
        var settings = TestSettings.Read(Doc(), environment: "production");

        Assert.Equal("db.internal", settings.Host);
        Assert.Equal(5432, settings.Port);
        Assert.Equal("iskender", settings.Database);
        Assert.Equal("isk_runtime", settings.Username);
        Assert.Equal("db/runtime", settings.Password.Name);
        Assert.Equal(PostgreSqlTlsMode.Require, settings.TlsMode);
        Assert.Equal(0, settings.PoolMinimum);
        Assert.Equal(20, settings.PoolMaximum);
        Assert.Equal(TimeSpan.FromSeconds(15), settings.ConnectionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.StatementTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.HealthProbeTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), settings.MigrationLockWait);
    }

    [Fact]
    public void AConfigurationWithNoMigrationObjectIsValidAndHasNoMigrationCredentials()
    {
        var settings = TestSettings.Read(Doc(), environment: "production");

        Assert.Null(settings.MigrationUsername);
        Assert.Null(settings.MigrationPassword);
        Assert.False(settings.HasMigrationCredentials);
    }

    [Fact]
    public void TheMigrationObjectCarriesItsLoginReferenceAndWait()
    {
        var settings = TestSettings.Read(Doc("\"migration\":{\"username\":\"isk_migrator\",\"password\":{\"secretRef\":\"db/migration\"},\"lockWait\":\"0s\"}"));

        Assert.Equal("isk_migrator", settings.MigrationUsername);
        Assert.Equal("db/migration", settings.MigrationPassword!.Name);
        Assert.Equal(TimeSpan.Zero, settings.MigrationLockWait);
        Assert.True(settings.HasMigrationCredentials);
    }

    [Theory]
    [InlineData("\"port\":1")]
    [InlineData("\"port\":65535")]
    [InlineData("\"poolMinimum\":0")]
    [InlineData("\"poolMaximum\":1000,\"poolMinimum\":1000")]
    [InlineData("\"poolMaximum\":1")]
    [InlineData("\"connectionTimeout\":\"1s\"")]
    [InlineData("\"connectionTimeout\":\"5m\"")]
    [InlineData("\"statementTimeout\":\"1s\"")]
    [InlineData("\"statementTimeout\":\"1h\"")]
    [InlineData("\"healthProbeTimeout\":\"1s\"")]
    [InlineData("\"healthProbeTimeout\":\"1m\"")]
    [InlineData("\"migration\":{\"lockWait\":\"1h\"}")]
    public void AcceptsEveryRangeBoundary(string field)
    {
        Assert.True(TestSettings.Validate(Doc(field)).IsValid);
    }

    [Theory]
    [InlineData("\"port\":0")]
    [InlineData("\"port\":65536")]
    [InlineData("\"poolMinimum\":-1")]
    [InlineData("\"poolMinimum\":1001")]
    [InlineData("\"poolMaximum\":0")]
    [InlineData("\"poolMaximum\":1001")]
    [InlineData("\"connectionTimeout\":\"999ms\"")]
    [InlineData("\"connectionTimeout\":\"301s\"")]
    [InlineData("\"statementTimeout\":\"999ms\"")]
    [InlineData("\"statementTimeout\":\"3601s\"")]
    [InlineData("\"healthProbeTimeout\":\"999ms\"")]
    [InlineData("\"healthProbeTimeout\":\"61s\"")]
    [InlineData("\"migration\":{\"lockWait\":\"3601s\"}")]
    public void RejectsEveryValueJustOutsideItsRange(string field)
    {
        Assert.Equal("config.field.out_of_range", OnlyError(Doc(field)));
    }

    [Theory]
    [InlineData("host")]
    [InlineData("database")]
    [InlineData("username")]
    [InlineData("password")]
    public void EachRuntimeFieldIsRequired(string field)
    {
        var withoutField = string.Join(',', Required.Split(',').Where(part => !part.StartsWith($"\"{field}\"", StringComparison.Ordinal)));

        Assert.Equal("config.field.required", OnlyError("{\"iskender\":{\"persistence\":{" + withoutField + "}}}"));
    }

    [Fact]
    public void TlsDisableIsAcceptedInDevelopment()
    {
        Assert.Equal(PostgreSqlTlsMode.Disable, TestSettings.Read(Doc("\"tlsMode\":\"disable\"")).TlsMode);
    }

    [Theory]
    [InlineData("production")]
    [InlineData(null)]
    public void TlsDisableIsUnsafeOutsideDevelopment(string? environment)
    {
        Assert.Equal("config.value.unsafe_for_environment", OnlyError(Doc("\"tlsMode\":\"disable\""), environment));
    }

    [Fact]
    public void AnUnknownTlsModeIsInvalid()
    {
        Assert.Equal("config.field.invalid_format", OnlyError(Doc("\"tlsMode\":\"prefer\"")));
    }

    [Fact]
    public void APlaintextPasswordIsRejectedByIsk0003()
    {
        var document = "{\"iskender\":{\"persistence\":{\"host\":\"h\",\"database\":\"d\",\"username\":\"u\",\"password\":\"hunter2-plaintext\"}}}";

        var report = TestSettings.Validate(document);

        Assert.Equal("config.secret.plaintext", Assert.Single(report.Errors).Code.Id);
        Assert.DoesNotContain("hunter2-plaintext", report.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void APlaintextMigrationPasswordIsRejected()
    {
        Assert.Equal("config.secret.plaintext", OnlyError(Doc("\"migration\":{\"username\":\"m\",\"password\":\"plain\"}")));
    }

    [Fact]
    public void AConnectionStringFieldIsUnknown()
    {
        Assert.Equal("config.field.unknown", OnlyError(Doc("\"connectionString\":\"Host=db;Password=x\"")));
    }

    [Fact]
    public void PoolMinimumAbovePoolMaximumFailsNamingPoolMinimum()
    {
        var report = TestSettings.Validate(Doc("\"poolMinimum\":30,\"poolMaximum\":10"));
        Assert.True(report.IsValid);

        var failure = Assert.Throws<GatewayException>(() => PostgreSqlSettings.From(report.Snapshot!.GetSection(PostgreSqlSectionSchema.Schema)));

        Assert.Same(ErrorCodes.ConfigFieldOutOfRange, failure.Code);
        Assert.Contains(failure.Error!.Diagnostics.Values, value => value.Key == "field" && value.Value == "iskender.persistence.poolMinimum");
    }

    [Fact]
    public void TheSchemaHasExactlyTheContractsFields()
    {
        Assert.Equal(
            ["host", "port", "database", "username", "password", "tlsMode", "poolMinimum", "poolMaximum", "connectionTimeout", "statementTimeout", "healthProbeTimeout", "migration"],
            PostgreSqlSectionSchema.Schema.Fields.Select(field => field.Name));
        var migration = PostgreSqlSectionSchema.Schema.Fields.Single(field => field.Name == "migration");
        Assert.Equal(["username", "password", "lockWait"], migration.Fields.Select(field => field.Name));
        Assert.Equal(ConfigurationFieldKind.SecretReference, PostgreSqlSectionSchema.Schema.Fields.Single(field => field.Name == "password").Kind);
        Assert.Equal(ConfigurationFieldKind.SecretReference, migration.Fields.Single(field => field.Name == "password").Kind);
        Assert.True(PostgreSqlSectionSchema.Schema.Fields.Single(field => field.Name == "tlsMode").IsDevelopmentOnly);
    }
}
