using System.Text;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.PostgreSql.Tests.Coordination;

/// <summary>
/// The PostgreSQL cache keeps values in memory (FR-019a, research R13): no table in <c>isk_coordination</c> ever holds a
/// cached value, and only the namespace's generation is written.
/// </summary>
public class PostgreSqlCacheTests(PostgreSqlCoordinatorUnderTest coordinator) : IClassFixture<PostgreSqlCoordinatorUnderTest>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NoTableInTheCoordinationSchemaHoldsACachedValue()
    {
        coordinator.Require();
        var scope = coordinator.Scopes.OrganizationA;
        var cacheNamespace = new CacheNamespace("cache_" + Guid.NewGuid().ToString("N")[..12], "entries");
        var needle = "cached-" + Guid.NewGuid().ToString("N");
        var value = Encoding.ASCII.GetBytes(needle);
        var (a, b) = (coordinator.Cache(0), coordinator.Cache(1));

        await b.InvalidateAsync(scope, cacheNamespace, Token);
        var miss = await a.TryGetAsync(scope, cacheNamespace, "item", 1, Token);
        Assert.True(await a.SetAsync(scope, cacheNamespace, "item", 1, value, TimeSpan.FromMinutes(5), miss.Stamp, Token));
        Assert.True((await a.TryGetAsync(scope, cacheNamespace, "item", 1, Token)).IsHit);

        Assert.True(await CountAsync("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'isk_coordination'") >= 4);
        // The search finds what is there: the generation row's namespace hash.
        Assert.Equal(1, await RowsMatchingAsync(CachePolicy.NamespaceKey(scope, cacheNamespace)));
        Assert.Equal(0, await RowsMatchingAsync(needle));
        Assert.Equal(0, await RowsMatchingAsync(Convert.ToHexStringLower(value)));
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM isk_coordination.cache_generations WHERE namespace_hash = '{CachePolicy.NamespaceKey(scope, cacheNamespace)}' AND generation = 1"));
    }

    [Fact]
    public async Task ABumpOnOneClientMakesTheOthersEntryAMissWithANewStamp()
    {
        coordinator.Require();
        var scope = coordinator.Scopes.OrganizationA;
        var cacheNamespace = new CacheNamespace("cache_" + Guid.NewGuid().ToString("N")[..12], "entries");
        var (a, b) = (coordinator.Cache(0), coordinator.Cache(1));
        var first = await a.TryGetAsync(scope, cacheNamespace, "item", 1, Token);
        Assert.True(await a.SetAsync(scope, cacheNamespace, "item", 1, new byte[] { 1 }, TimeSpan.FromMinutes(5), first.Stamp, Token));

        await b.InvalidateAsync(scope, cacheNamespace, Token);
        var second = await a.TryGetAsync(scope, cacheNamespace, "item", 1, Token);

        Assert.False(second.IsHit);
        Assert.NotNull(first.Stamp);
        Assert.NotNull(second.Stamp);
        Assert.Equal(first.Stamp.Value.Generation + 1, second.Stamp.Value.Generation);
        Assert.True(await a.SetAsync(scope, cacheNamespace, "item", 1, new byte[] { 2 }, TimeSpan.FromMinutes(5), second.Stamp, Token));
        Assert.Equal([2], (await a.TryGetAsync(scope, cacheNamespace, "item", 1, Token)).Value.ToArray());
    }

    private Task<long> CountAsync(string sql) => coordinator.Fixture.ScalarAsync<long>(coordinator.Database.Name, sql);

    // As the superuser, so row-level security hides nothing: the rows of every coordination table whose text holds the needle.
    private Task<long> RowsMatchingAsync(string needle) => CountAsync(
        "SELECT coalesce(sum((xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM isk_coordination.%I AS r WHERE r::text LIKE %L', "
        + $"table_name, '%{needle}%'), false, true, '')))[1]::text::bigint), 0)::bigint FROM information_schema.tables WHERE table_schema = 'isk_coordination'");
}
