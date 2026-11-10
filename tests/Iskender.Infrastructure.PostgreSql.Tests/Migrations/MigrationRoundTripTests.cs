using System.Runtime.CompilerServices;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

/// <summary>
/// User Story 1 scenarios 1 to 3 and SC-002: 7 migrations across 2 modules, one with a data-losing reverse, applied one
/// at a time against committed snapshots, reversed one step at a time, and applied again. Set
/// <c>ISKENDER_WRITE_SNAPSHOTS=1</c> to rewrite the expected snapshots; the test then fails, so a rewrite is never a pass.
/// </summary>
public class MigrationRoundTripTests(EmptyDatabase database) : IClassFixture<EmptyDatabase>
{
    private const string WriteSwitch = "ISKENDER_WRITE_SNAPSHOTS";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ForwardReverseAndForwardAgainMatchTheSnapshots()
    {
        database.RequirePostgreSql();
        await using var store = TestStore.For(database);
        var release = FixtureModules.Release2;
        var steps = release.Migrations;
        Assert.Equal(7, steps.Count);
        Assert.Contains(steps, script => script.DataLosing);
        var writing = Environment.GetEnvironmentVariable(WriteSwitch) == "1";

        // Scenario 1: each forward step, in dependency then version order, matches its committed snapshot.
        var before = new List<string> { await CatalogSnapshot.RenderAsync(database.Fixture, database.Name) };
        Assert.Equal("", before[0]);
        for (var i = 0; i < steps.Count; i++)
        {
            var result = await store.Runner(FixtureModules.Release2First(i + 1)).ApplyAsync(new MigrationApplyRequest(), Token);
            Assert.Equal([steps[i].Identity], result.Applied);
            var snapshot = await CatalogSnapshot.RenderAsync(database.Fixture, database.Name);
            if (writing)
            {
                await File.WriteAllTextAsync(Path.Combine(SourceSnapshots(), SnapshotName(i, steps[i].Identity)), snapshot, Token);
            }
            else
            {
                Assert.Equal(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Snapshots", SnapshotName(i, steps[i].Identity)), Token), snapshot);
            }

            before.Add(snapshot);
        }

        Assert.False(writing, $"Snapshots were written to {SourceSnapshots()}. Review them, then run again without {WriteSwitch}.");
        await AssertHistoryAsync(steps.Select(script => (script.Identity.Module, script.Identity.Version, script.Checksum)));

        // Scenario 2: each reverse step returns the schema to the snapshot taken before that migration was applied.
        var runner = store.Runner(release);
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            var result = await runner.ReverseAsync(new MigrationReverseRequest { ConfirmDataLoss = steps[i].DataLosing }, Token);
            Assert.Equal([steps[i].Identity], result.Reversed);
            Assert.Equal(before[i], await CatalogSnapshot.RenderAsync(database.Fixture, database.Name));
        }

        Assert.Equal(0, await database.Fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname IN ('isk_alpha', 'isk_beta')"));
        Assert.Equal(0, await database.Fixture.ScalarAsync<long>(database.Name, "SELECT count(*) FROM isk_migrations.history"));

        // Scenario 3: applying again gives the same schema as the first forward run.
        Assert.Equal(steps.Select(script => script.Identity), (await runner.ApplyAsync(new MigrationApplyRequest(), Token)).Applied);
        Assert.Equal(before[^1], await CatalogSnapshot.RenderAsync(database.Fixture, database.Name));
        Assert.Empty((await runner.ApplyAsync(new MigrationApplyRequest(), Token)).Applied);
    }

    private async Task AssertHistoryAsync(IEnumerable<(string Module, int Version, string Checksum)> expected)
    {
        await using var connection = await database.Fixture.OpenSuperuserAsync(database.Name);
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT module, version, checksum, applied_by, applied_at > now() - interval '10 minutes' AND applied_at <= now() FROM isk_migrations.history ORDER BY applied_at, module, version",
            connection);
        await using var reader = await command.ExecuteReaderAsync(Token);
        var rows = new List<(string, int, string)>();
        while (await reader.ReadAsync(Token))
        {
            Assert.Equal(PostgreSqlFixture.MigratorLogin, reader.GetString(3));
            Assert.True(reader.GetBoolean(4));
            rows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetString(2)));
        }

        Assert.Equal(expected, rows);
    }

    private static string SnapshotName(int index, MigrationIdentity migration) => $"{index + 1:D2}_{migration.Module}_{migration.Version:D4}.txt";

    private static string SourceSnapshots([CallerFilePath] string path = "") => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path))!, "Snapshots");
}
