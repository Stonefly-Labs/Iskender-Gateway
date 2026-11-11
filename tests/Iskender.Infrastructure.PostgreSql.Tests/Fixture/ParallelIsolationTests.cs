using Iskender.Infrastructure.PostgreSql.Tests.Modules.Alpha;

namespace Iskender.Infrastructure.PostgreSql.Tests.Fixture;

/// <summary>
/// Two test classes in separate collections run in parallel, each on its own class database, and insert rows with
/// identical keys into <c>widgets</c> without seeing each other's (User Story 5 scenario 1, FR-033). The only network
/// access the suite makes is the one pull of the pinned PostgreSQL image; everything else is the local container
/// (scenario 3, SC-001).
/// </summary>
[Collection("parallel isolation left")]
public class ParallelIsolationLeftTests(MigratedDatabase database) : IClassFixture<MigratedDatabase>
{
    [Fact]
    public Task SeesOnlyItsOwnRowsUnderSharedKeys() => ParallelIsolation.InsertAndReadAsync(database, "left");
}

/// <summary>The other half of <see cref="ParallelIsolationLeftTests"/>.</summary>
[Collection("parallel isolation right")]
public class ParallelIsolationRightTests(MigratedDatabase database) : IClassFixture<MigratedDatabase>
{
    [Fact]
    public Task SeesOnlyItsOwnRowsUnderSharedKeys() => ParallelIsolation.InsertAndReadAsync(database, "right");
}

internal static class ParallelIsolation
{
    private static readonly string[] SharedKeys = ["w-parallel-1", "w-parallel-2", "w-parallel-3"];

    public static async Task InsertAndReadAsync(MigratedDatabase database, string side)
    {
        database.RequirePostgreSql();
        var token = TestContext.Current.CancellationToken;
        await using var store = TestStore.For(database);
        var org = Owners.Organization(Owners.OrgA);

        await using (var unit = await store.OpenAsync(org))
        {
            foreach (var key in SharedKeys)
            {
                await WidgetRepository.InsertAsync(unit, key, side, token);
            }

            await unit.CommitAsync(token);
        }

        await using var read = await store.OpenAsync(org);
        var rows = (await new WidgetRepository().ListAsync(read, token)).Where(widget => widget.Id.StartsWith("w-parallel-", StringComparison.Ordinal)).ToList();
        Assert.Equal(SharedKeys, rows.Select(widget => widget.Id));
        Assert.All(rows, widget => Assert.Equal(side, widget.Name));
    }
}
