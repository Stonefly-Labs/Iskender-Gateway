using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.Coordination;

/// <summary>How the Redis adapter protects its link. <c>Require</c> verifies the certificate chain and host name. Owned by ISK-0005.</summary>
public enum RedisTlsMode
{
    Require,
    Disable,
}

/// <summary>
/// The validated <c>iskender.coordination</c> section, read once. It holds a secret reference, never a password
/// (research R16). Owned by ISK-0005.
/// </summary>
public sealed class CoordinationSettings
{
    private CoordinationSettings(ConfigurationSection section)
    {
        var leases = section.GetSection("leases");
        var cache = section.GetSection("cache");
        var redis = section.GetSection("redis");

        Topology = section.GetString("topology") == "single_instance" ? CoordinationTopology.SingleInstance : CoordinationTopology.MultiReplica;
        AtomicCoordinator = Kind(section.GetSection("atomic"));
        LeaseCoordinator = Kind(leases);
        CacheCoordinator = Kind(cache);
        MinimumLeaseDuration = leases.GetDuration("minimumDuration")!.Value;
        MaximumLeaseDuration = leases.GetDuration("maximumDuration")!.Value;
        SafetyMargin = leases.GetDuration("safetyMargin")!.Value;
        MaximumWait = leases.GetDuration("maximumWait")!.Value;
        MaximumExpiry = section.GetSection("keys").GetDuration("maximumExpiry")!.Value;
        MaximumValueBytes = (int)cache.GetInt64("maximumValueBytes")!.Value;
        MaximumEntries = (int)cache.GetInt64("maximumEntries")!.Value;
        OperationTimeout = section.GetDuration("operationTimeout")!.Value;
        HealthProbeTimeout = section.GetDuration("healthProbeTimeout")!.Value;
        InstanceLeaseDuration = section.GetSection("instanceLease").GetDuration("duration")!.Value;
        RedisEndpoints = redis.GetStrings("endpoints") ?? [];
        RedisUsername = redis.GetString("username");
        RedisPassword = redis.GetSecretReference("password");
        RedisTlsMode = redis.GetString("tlsMode") == "disable" ? RedisTlsMode.Disable : RedisTlsMode.Require;
        RedisConnectTimeout = redis.GetDuration("connectTimeout")!.Value;
    }

    public CoordinationTopology Topology { get; }

    /// <summary>The coordinator for atomic operations.</summary>
    public CoordinatorKind AtomicCoordinator { get; }

    /// <summary>The coordinator for leases, which also issues the fencing tokens.</summary>
    public CoordinatorKind LeaseCoordinator { get; }

    /// <summary>The coordinator for cache invalidation.</summary>
    public CoordinatorKind CacheCoordinator { get; }

    public TimeSpan MinimumLeaseDuration { get; }

    public TimeSpan MaximumLeaseDuration { get; }

    public TimeSpan SafetyMargin { get; }

    public TimeSpan MaximumWait { get; }

    public TimeSpan MaximumExpiry { get; }

    public int MaximumValueBytes { get; }

    /// <summary>The in-memory store's entry bound.</summary>
    public int MaximumEntries { get; }

    public TimeSpan OperationTimeout { get; }

    public TimeSpan HealthProbeTimeout { get; }

    public TimeSpan InstanceLeaseDuration { get; }

    /// <summary>The Redis <c>host:port</c> endpoints, empty when Redis is not configured.</summary>
    public IReadOnlyList<string> RedisEndpoints { get; }

    /// <summary>The ACL user, or <c>null</c> for the default user.</summary>
    public string? RedisUsername { get; }

    /// <summary>The ACL user's secret reference, or <c>null</c>.</summary>
    public SecretReference? RedisPassword { get; }

    public RedisTlsMode RedisTlsMode { get; }

    public TimeSpan RedisConnectTimeout { get; }

    /// <summary>True when at least one Redis endpoint is configured.</summary>
    public bool HasRedis => RedisEndpoints.Count > 0;

    /// <summary>The configured coordinator for <paramref name="primitive"/>. Fencing follows the lease coordinator, whose tokens it checks.</summary>
    public CoordinatorKind CoordinatorFor(CoordinationPrimitive primitive) => primitive switch
    {
        CoordinationPrimitive.AtomicOperations => AtomicCoordinator,
        CoordinationPrimitive.Leases or CoordinationPrimitive.Fencing => LeaseCoordinator,
        CoordinationPrimitive.CacheInvalidation => CacheCoordinator,
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };

    /// <summary>
    /// Whether the coordinator configured for <paramref name="primitive"/> has its settings, which capability check rule 3
    /// consumes: always for <c>local</c>, at least one endpoint for <c>redis</c>, and <paramref name="postgreSqlConfigured"/>
    /// for <c>postgresql</c>, since <c>persistence</c> is a separate section the caller reads.
    /// </summary>
    public bool IsConfigured(CoordinationPrimitive primitive, bool postgreSqlConfigured) => CoordinatorFor(primitive) switch
    {
        CoordinatorKind.Local => true,
        CoordinatorKind.Redis => HasRedis,
        CoordinatorKind.PostgreSql => postgreSqlConfigured,
        _ => false,
    };

    /// <summary>
    /// Reads the section from a validated snapshot, with the snapshot's environment. <c>leases.maximumDuration</c> below
    /// <c>leases.minimumDuration</c> fails with <c>config.field.out_of_range</c>, naming <c>leases.maximumDuration</c> in
    /// the error's diagnostics. Outside <c>development</c>, Redis endpoints without <c>redis.password</c> or
    /// <c>redis.username</c> fail with <c>config.field.required</c>, naming the missing field, so a production gateway never
    /// connects to Redis without credentials (contract Credentials rule 2).
    /// </summary>
    public static CoordinationSettings From(ConfigurationSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var section = snapshot.GetSection(CoordinationSectionSchema.Schema);
        var settings = new CoordinationSettings(section);
        if (settings.MaximumLeaseDuration < settings.MinimumLeaseDuration)
        {
            throw FieldFailure(ErrorCodes.ConfigFieldOutOfRange, $"{section.Path}.leases.maximumDuration");
        }

        if (settings.HasRedis && !string.Equals(snapshot.EnvironmentName, "development", StringComparison.Ordinal))
        {
            if (settings.RedisPassword is null)
            {
                throw FieldFailure(ErrorCodes.ConfigFieldRequired, $"{section.Path}.redis.password");
            }

            if (settings.RedisUsername is null)
            {
                throw FieldFailure(ErrorCodes.ConfigFieldRequired, $"{section.Path}.redis.username");
            }
        }

        return settings;
    }

    private static GatewayException FieldFailure(ErrorCode code, string field) => new(GatewayError.Create(
        code,
        RequestCorrelationId.New(),
        OutputVisibility.None,
        diagnostics: new ErrorDiagnostics(values: [new("field", field)])));

    private static CoordinatorKind Kind(ConfigurationSection primitive) => primitive.GetString("coordinator") switch
    {
        "redis" => CoordinatorKind.Redis,
        "postgresql" => CoordinatorKind.PostgreSql,
        _ => CoordinatorKind.Local,
    };
}
