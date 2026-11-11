using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Tests.Configuration;

public class CoordinationSectionTests
{
    private static string Doc(string fields = "") => "{\"iskender\":{\"coordination\":{" + fields + "}}}";

    private static ConfigurationValidationReport Validate(string document, string? environment = "development") =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([CoordinationSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("coordination.json", ConfigurationDocumentFormat.Json, document),
            environmentName: environment));

    private static CoordinationSettings Read(string document, string? environment = "development")
    {
        var report = Validate(document, environment);
        Assert.True(report.IsValid, report.ToString());
        return CoordinationSettings.From(report.Snapshot!.GetSection(CoordinationSectionSchema.Schema));
    }

    private static string OnlyError(string document, string? environment = "development")
    {
        var report = Validate(document, environment);
        Assert.False(report.IsValid);
        return Assert.Single(report.Errors).Code.Id;
    }

    [Theory]
    [InlineData("{\"iskender\":{}}")]
    [InlineData("{\"iskender\":{\"coordination\":{}}}")]
    public void WithNoSectionEveryPrimitiveIsLocalAndTheDefaultsAreTheSafetyBounds(string document)
    {
        var settings = Read(document, environment: "production");

        Assert.Equal(CoordinationTopology.MultiReplica, settings.Topology);
        Assert.All(CoordinationPrimitives.All, primitive => Assert.Equal(CoordinatorKind.Local, settings.CoordinatorFor(primitive)));
        Assert.All(CoordinationPrimitives.All, primitive => Assert.True(settings.IsConfigured(primitive, postgreSqlConfigured: false)));
        Assert.Equal(TimeSpan.FromSeconds(1), settings.MinimumLeaseDuration);
        Assert.Equal(TimeSpan.FromMinutes(10), settings.MaximumLeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.SafetyMargin);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.MaximumWait);
        Assert.Equal(TimeSpan.FromDays(35), settings.MaximumExpiry);
        Assert.Equal(65_536, settings.MaximumValueBytes);
        Assert.Equal(10_000, settings.MaximumEntries);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.OperationTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.HealthProbeTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.InstanceLeaseDuration);
        Assert.Empty(settings.RedisEndpoints);
        Assert.False(settings.HasRedis);
        Assert.Equal(0, settings.RedisDatabase);
        Assert.Null(settings.RedisUsername);
        Assert.Null(settings.RedisPassword);
        Assert.Equal(RedisTlsMode.Require, settings.RedisTlsMode);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.RedisConnectTimeout);
    }

    [Fact]
    public void EachPrimitiveReadsItsOwnCoordinatorAndFencingFollowsLeases()
    {
        var settings = Read(Doc("\"topology\":\"single_instance\",\"atomic\":{\"coordinator\":\"redis\"},\"leases\":{\"coordinator\":\"postgresql\"},\"cache\":{\"coordinator\":\"local\"}"));

        Assert.Equal(CoordinationTopology.SingleInstance, settings.Topology);
        Assert.Equal(CoordinatorKind.Redis, settings.CoordinatorFor(CoordinationPrimitive.AtomicOperations));
        Assert.Equal(CoordinatorKind.PostgreSql, settings.CoordinatorFor(CoordinationPrimitive.Leases));
        Assert.Equal(CoordinatorKind.PostgreSql, settings.CoordinatorFor(CoordinationPrimitive.Fencing));
        Assert.Equal(CoordinatorKind.Local, settings.CoordinatorFor(CoordinationPrimitive.CacheInvalidation));
    }

    [Fact]
    public void TheRedisFieldsAreReadAndThePasswordStaysAReference()
    {
        var settings = Read(Doc("\"redis\":{\"endpoints\":[\"cache-1.internal:6379\",\"[::1]:6380\"],\"database\":15,\"username\":\"isk_coordination\",\"password\":{\"secretRef\":\"redis/coordination\"},\"connectTimeout\":\"1m\"}"));

        Assert.Equal(["cache-1.internal:6379", "[::1]:6380"], settings.RedisEndpoints);
        Assert.True(settings.HasRedis);
        Assert.Equal(15, settings.RedisDatabase);
        Assert.Equal("isk_coordination", settings.RedisUsername);
        Assert.Equal("redis/coordination", settings.RedisPassword!.Name);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.RedisConnectTimeout);
    }

    [Theory]
    [InlineData("\"leases\":{\"minimumDuration\":\"100ms\"}")]
    [InlineData("\"leases\":{\"minimumDuration\":\"1h\",\"maximumDuration\":\"1h\"}")]
    [InlineData("\"leases\":{\"maximumDuration\":\"1s\"}")]
    [InlineData("\"leases\":{\"maximumDuration\":\"24h\"}")]
    [InlineData("\"leases\":{\"safetyMargin\":\"0s\"}")]
    [InlineData("\"leases\":{\"safetyMargin\":\"1m\"}")]
    [InlineData("\"leases\":{\"maximumWait\":\"0s\"}")]
    [InlineData("\"leases\":{\"maximumWait\":\"5m\"}")]
    [InlineData("\"keys\":{\"maximumExpiry\":\"1s\"}")]
    [InlineData("\"keys\":{\"maximumExpiry\":\"9600h\"}")]
    [InlineData("\"cache\":{\"maximumValueBytes\":1}")]
    [InlineData("\"cache\":{\"maximumValueBytes\":1048576}")]
    [InlineData("\"cache\":{\"maximumEntries\":1}")]
    [InlineData("\"cache\":{\"maximumEntries\":1000000}")]
    [InlineData("\"operationTimeout\":\"100ms\"")]
    [InlineData("\"operationTimeout\":\"1m\"")]
    [InlineData("\"healthProbeTimeout\":\"1s\"")]
    [InlineData("\"healthProbeTimeout\":\"1m\"")]
    [InlineData("\"instanceLease\":{\"duration\":\"5s\"}")]
    [InlineData("\"instanceLease\":{\"duration\":\"5m\"}")]
    [InlineData("\"redis\":{\"database\":0}")]
    [InlineData("\"redis\":{\"database\":15}")]
    [InlineData("\"redis\":{\"connectTimeout\":\"1s\"}")]
    [InlineData("\"redis\":{\"connectTimeout\":\"1m\"}")]
    [InlineData("\"redis\":{\"endpoints\":[]}")]
    [InlineData("\"redis\":{\"endpoints\":[\"h0:1\",\"h1:1\",\"h2:1\",\"h3:1\",\"h4:1\",\"h5:1\",\"h6:1\",\"h7:1\",\"h8:1\",\"h9:1\",\"ha:1\",\"hb:1\",\"hc:1\",\"hd:1\",\"he:1\",\"hf:1\"]}")]
    public void AcceptsEveryRangeBoundary(string field)
    {
        Assert.True(Validate(Doc(field)).IsValid, Validate(Doc(field)).ToString());
    }

    [Theory]
    [InlineData("\"leases\":{\"minimumDuration\":\"99ms\"}")]
    [InlineData("\"leases\":{\"minimumDuration\":\"3601s\"}")]
    [InlineData("\"leases\":{\"maximumDuration\":\"999ms\"}")]
    [InlineData("\"leases\":{\"maximumDuration\":\"86401s\"}")]
    [InlineData("\"leases\":{\"safetyMargin\":\"61s\"}")]
    [InlineData("\"leases\":{\"maximumWait\":\"301s\"}")]
    [InlineData("\"keys\":{\"maximumExpiry\":\"999ms\"}")]
    [InlineData("\"keys\":{\"maximumExpiry\":\"34560001s\"}")]
    [InlineData("\"cache\":{\"maximumValueBytes\":0}")]
    [InlineData("\"cache\":{\"maximumValueBytes\":1048577}")]
    [InlineData("\"cache\":{\"maximumEntries\":0}")]
    [InlineData("\"cache\":{\"maximumEntries\":1000001}")]
    [InlineData("\"operationTimeout\":\"99ms\"")]
    [InlineData("\"operationTimeout\":\"61s\"")]
    [InlineData("\"healthProbeTimeout\":\"999ms\"")]
    [InlineData("\"healthProbeTimeout\":\"61s\"")]
    [InlineData("\"instanceLease\":{\"duration\":\"4999ms\"}")]
    [InlineData("\"instanceLease\":{\"duration\":\"301s\"}")]
    [InlineData("\"redis\":{\"database\":-1}")]
    [InlineData("\"redis\":{\"database\":16}")]
    [InlineData("\"redis\":{\"connectTimeout\":\"999ms\"}")]
    [InlineData("\"redis\":{\"connectTimeout\":\"61s\"}")]
    public void RejectsEveryValueJustOutsideItsRange(string field)
    {
        Assert.Equal("config.field.out_of_range", OnlyError(Doc(field)));
    }

    [Fact]
    public void RejectsASeventeenthEndpoint()
    {
        var endpoints = string.Join(',', Enumerable.Range(0, 17).Select(index => $"\"h{index}:6379\""));

        Assert.False(Validate(Doc("\"redis\":{\"endpoints\":[" + endpoints + "]}")).IsValid);
    }

    [Theory]
    [InlineData("\"topology\":\"cluster\"")]
    [InlineData("\"atomic\":{\"coordinator\":\"etcd\"}")]
    [InlineData("\"redis\":{\"tlsMode\":\"prefer\"}")]
    public void AnUnknownEnumerationMemberIsInvalid(string field)
    {
        Assert.Equal("config.field.invalid_format", OnlyError(Doc(field)));
    }

    [Fact]
    public void TlsDisableIsAcceptedInDevelopment()
    {
        Assert.Equal(RedisTlsMode.Disable, Read(Doc("\"redis\":{\"tlsMode\":\"disable\"}")).RedisTlsMode);
    }

    [Theory]
    [InlineData("production")]
    [InlineData(null)]
    public void TlsDisableIsUnsafeOutsideDevelopment(string? environment)
    {
        Assert.Equal("config.value.unsafe_for_environment", OnlyError(Doc("\"redis\":{\"tlsMode\":\"disable\"}"), environment));
    }

    [Fact]
    public void APlaintextPasswordIsRejectedByIsk0003()
    {
        var report = Validate(Doc("\"redis\":{\"password\":\"hunter2-plaintext\"}"));

        Assert.Equal("config.secret.plaintext", Assert.Single(report.Errors).Code.Id);
        Assert.DoesNotContain("hunter2-plaintext", report.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"connectionString\":\"redis://:x@cache:6379\"")]
    [InlineData("\"redis\":{\"connectionString\":\"cache:6379,password=x\"}")]
    public void AConnectionStringFieldIsUnknown(string field)
    {
        Assert.Equal("config.field.unknown", OnlyError(Doc(field)));
    }

    [Fact]
    public void ARedisPrimitiveWithNoEndpointsIsNotConfigured()
    {
        var settings = Read(Doc("\"atomic\":{\"coordinator\":\"redis\"},\"cache\":{\"coordinator\":\"redis\"}"));

        Assert.False(settings.IsConfigured(CoordinationPrimitive.AtomicOperations, postgreSqlConfigured: true));
        Assert.False(settings.IsConfigured(CoordinationPrimitive.CacheInvalidation, postgreSqlConfigured: true));
        Assert.True(settings.IsConfigured(CoordinationPrimitive.Leases, postgreSqlConfigured: false));
    }

    [Fact]
    public void ARedisPrimitiveWithAnEndpointIsConfigured()
    {
        var settings = Read(Doc("\"atomic\":{\"coordinator\":\"redis\"},\"redis\":{\"endpoints\":[\"cache:6379\"]}"));

        Assert.True(settings.IsConfigured(CoordinationPrimitive.AtomicOperations, postgreSqlConfigured: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APostgreSqlPrimitiveIsConfiguredExactlyWhenTheCallerSaysSo(bool postgreSqlConfigured)
    {
        var settings = Read(Doc("\"leases\":{\"coordinator\":\"postgresql\"}"));

        Assert.Equal(postgreSqlConfigured, settings.IsConfigured(CoordinationPrimitive.Leases, postgreSqlConfigured));
        Assert.Equal(postgreSqlConfigured, settings.IsConfigured(CoordinationPrimitive.Fencing, postgreSqlConfigured));
    }

    [Fact]
    public void MaximumDurationBelowMinimumDurationFailsNamingMaximumDuration()
    {
        var report = Validate(Doc("\"leases\":{\"minimumDuration\":\"30s\",\"maximumDuration\":\"10s\"}"));
        Assert.True(report.IsValid);

        var failure = Assert.Throws<GatewayException>(() => CoordinationSettings.From(report.Snapshot!.GetSection(CoordinationSectionSchema.Schema)));

        Assert.Same(ErrorCodes.ConfigFieldOutOfRange, failure.Code);
        Assert.Contains(failure.Error!.Diagnostics.Values, value => value.Key == "field" && value.Value == "iskender.coordination.leases.maximumDuration");
    }

    [Fact]
    public void TheSchemaHasExactlyTheDataModelsFields()
    {
        var schema = CoordinationSectionSchema.Schema;
        FieldSchema Field(string name) => schema.Fields.Single(field => field.Name == name);

        Assert.Equal("coordination", schema.Namespace);
        Assert.Equal(["topology", "atomic", "leases", "cache", "keys", "operationTimeout", "healthProbeTimeout", "instanceLease", "redis"], schema.Fields.Select(field => field.Name));
        Assert.Equal(["coordinator"], Field("atomic").Fields.Select(field => field.Name));
        Assert.Equal(["coordinator", "minimumDuration", "maximumDuration", "safetyMargin", "maximumWait"], Field("leases").Fields.Select(field => field.Name));
        Assert.Equal(["coordinator", "maximumValueBytes", "maximumEntries"], Field("cache").Fields.Select(field => field.Name));
        Assert.Equal(["maximumExpiry"], Field("keys").Fields.Select(field => field.Name));
        Assert.Equal(["duration"], Field("instanceLease").Fields.Select(field => field.Name));
        Assert.Equal(["endpoints", "database", "username", "password", "tlsMode", "connectTimeout"], Field("redis").Fields.Select(field => field.Name));
        Assert.Equal(ConfigurationFieldKind.SecretReference, Field("redis").Fields.Single(field => field.Name == "password").Kind);
        Assert.True(Field("redis").Fields.Single(field => field.Name == "tlsMode").IsDevelopmentOnly);
    }
}
