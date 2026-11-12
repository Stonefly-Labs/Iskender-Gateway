using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Infrastructure.PostgreSql.Coordination;

/// <summary>
/// The versioned cache with the PostgreSQL coordinator (FR-019a, research R13): values stay in this process's bounded
/// <see cref="InMemoryCacheStore"/> and are never written to PostgreSQL. Only each namespace's generation is shared, in
/// <c>isk_coordination.cache_generations</c>, and every read checks it with one indexed <c>SELECT</c> in its own unit, so
/// a bump on any replica makes this replica's next read a miss. An entry records the generation it was stored under, so
/// a bump that lands between a set's check and its store still makes that entry a miss. It declares <c>strict</c>.
/// Owned by ISK-0005.
/// </summary>
public sealed class PostgreSqlVersionedCache : IVersionedCache
{
    private const string GenerationSql =
        "SELECT generation FROM isk_coordination.cache_generations WHERE {scope} AND namespace_hash = $2";

    private const string BumpSql =
        "INSERT INTO isk_coordination.cache_generations AS g (scope_key, namespace_hash, generation) VALUES ($1, $2, 1) "
        + "ON CONFLICT (scope_key, namespace_hash) DO UPDATE SET generation = g.generation + 1";

    private readonly IUnitOfWorkFactory _units;
    private readonly CoordinationSettings _settings;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly InMemoryCacheStore _store;

    public PostgreSqlVersionedCache(IUnitOfWorkFactory units, CoordinationSettings settings, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        _units = units ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _settings = settings ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _store = new InMemoryCacheStore(settings.MaximumEntries, _timeProvider);
    }

    /// <summary>What this coordinator declares: <c>strict</c> for every primitive.</summary>
    public static CoordinatorCapabilities Capabilities => CoordinatorCapabilities.PostgreSql;

    public ValueTask<CacheLookup> TryGetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long expectedVersion, CancellationToken cancellationToken)
    {
        CachePolicy.RequireTarget(scope, cacheNamespace, name);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        return CachePolicy.ReadOrMissAsync(_logger, cacheNamespace.Module, CoordinatorKind.PostgreSql, () => Run(scope, cacheNamespace, "get", async (unit, key, token) =>
        {
            var generation = await GenerationAsync(unit, key, token);
            var entry = _store.Get(CachePolicy.EntryKey(scope, cacheNamespace, name));
            return entry is not null && entry.Version == expectedVersion && entry.Generation == generation
                ? CacheLookup.Hit(entry.Value)
                : CacheLookup.Miss(CacheStamp.Of(generation));
        }, cancellationToken), cancellationToken);
    }

    public async ValueTask<bool> SetAsync(CoordinationScope scope, CacheNamespace cacheNamespace, string name, long version, ReadOnlyMemory<byte> value, TimeSpan expiry, CacheStamp? stamp, CancellationToken cancellationToken)
    {
        CachePolicy.RequireTarget(scope, cacheNamespace, name);
        CachePolicy.RequireEntry(_settings, value, expiry);
        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        if (stamp is not { } observed)
        {
            return false;
        }

        var current = await Run(scope, cacheNamespace, "set", (unit, key, token) => GenerationAsync(unit, key, token), cancellationToken);
        if (current != observed.Generation)
        {
            return false;
        }

        _store.Set(CachePolicy.EntryKey(scope, cacheNamespace, name), version, observed.Generation, value, expiry);
        return true;
    }

    public async ValueTask InvalidateAsync(CoordinationScope scope, CacheNamespace cacheNamespace, CancellationToken cancellationToken)
    {
        if (scope is null)
        {
            throw CoordinationFailures.Raise(ErrorCodes.CoordinationScopeRequired, CoordinationFailureClass.ScopeViolation);
        }

        if (cacheNamespace is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        LeasePolicy.RequireTime(scope, _timeProvider, cancellationToken);
        await Run(scope, cacheNamespace, "invalidate", async (unit, key, token) =>
            await unit.Command(CoordinationTables.CacheGenerations, BumpSql).With(key).ExecuteAsync(token), cancellationToken);
        CoordinationLog.LogCacheInvalidated(_logger, cacheNamespace.Module, CoordinatorKind.PostgreSql);
    }

    private static async ValueTask<long> GenerationAsync(PostgreSqlUnitOfWork unit, string key, CancellationToken cancellationToken)
    {
        var rows = await unit.Command(CoordinationTables.CacheGenerations, GenerationSql).With(key)
            .QueryAsync(reader => reader.GetInt64(0), cancellationToken);
        return rows.Count > 0 ? rows[0] : 0;
    }

    private ValueTask<T> Run<T>(CoordinationScope scope, CacheNamespace cacheNamespace, string operation, Func<PostgreSqlUnitOfWork, string, CancellationToken, ValueTask<T>> work, CancellationToken cancellationToken)
    {
        var key = CachePolicy.NamespaceKey(scope, cacheNamespace);
        return CoordinationTelemetry.MeasureAsync(cacheNamespace.Module, CoordinationPrimitive.CacheInvalidation, operation, CoordinatorKind.PostgreSql, _timeProvider, () =>
            CoordinationUnits.RunAsync(
                _units,
                scope,
                "cache." + operation,
                _settings.OperationTimeout,
                (unit, token) => work(PostgreSqlUnitOfWork.From(unit), key, token),
                cancellationToken,
                new CoordinationReport(_logger, cacheNamespace.Module, CoordinationPrimitive.CacheInvalidation)));
    }
}
