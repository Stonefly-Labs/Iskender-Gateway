using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Crash;

/// <summary>
/// The process-kill suite (ISK-0009 T025, SC-001, FR-040): at each injection point, 20 cycles that start the crash host,
/// wait for its line, kill it with <c>SIGKILL</c>, and restart it, then a drain. Every item a committed unit enqueued
/// ends <c>completed</c> with exactly one <c>applies</c> row, and no item exists for a rolled-back producer.
/// </summary>
public class ProcessKillTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private const int Cycles = 20;

    // High enough that no item is poisoned by the attempts the kills count; T034 covers the maximum.
    private const int MaximumAttempts = 100;

    [Theory]
    [InlineData("BeforeAttemptCommit", false)]
    [InlineData("AfterAttemptCommit", false)]
    [InlineData("DuringHandler", false)]
    [InlineData("AfterApplyCommit", false)]
    [InlineData("BeforeFailureCommit", true)]
    [InlineData("AfterFailureCommit", true)]
    public async Task KillsAtADispatchPointLoseNothingAndApplyNothingTwice(string point, bool failOnce)
    {
        work.RequirePostgreSql();
        var kind = failOnce ? TestKinds.FailOnce : TestKinds.Record;
        var items = new List<Guid>();
        for (var cycle = 0; cycle < Cycles; cycle++)
        {
            // A fresh scope per cycle, so the restarted host is not held off by the killed one's shard leases.
            items.Add(await work.EnqueueAsync(FreshScope(out _), kind, $"{point}-{cycle}"));
            await using var host = CrashHostProcess.Start(work, [], point, MaximumAttempts);
            await host.ReachedAsync(point);
            await host.KillAsync();
        }

        await DrainAsync(items);
    }

    [Fact]
    public async Task KillsAfterTheProducersCommitKeepTheCommittedItemAndNoneOfTheRolledBackOne()
    {
        work.RequirePostgreSql();
        var scopes = new List<UnitOfWorkScope>();
        for (var cycle = 0; cycle < Cycles; cycle++)
        {
            var scope = FreshScope(out var listed);
            scopes.Add(scope);
            await using var host = CrashHostProcess.Start(work, [listed], "AfterProducerCommit", MaximumAttempts);
            await host.ReachedAsync("BeforeProducerCommit");
            await host.KillAsync();
        }

        var items = new List<Guid>();
        foreach (var scope in scopes)
        {
            await using var unit = await work.OpenAsync(scope, new UnitOfWorkOptions("work", "test.items"));
            var found = await PostgreSqlUnitOfWork.From(unit).Command(WorkTables.Items, "SELECT item_id FROM isk_work.items WHERE {scope}")
                .QueryAsync(reader => reader.GetGuid(0), TestContext.Current.CancellationToken);
            await unit.CommitAsync(TestContext.Current.CancellationToken);
            items.Add(Assert.Single(found));
        }

        await DrainAsync(items);
    }

    // Runs the host holding nowhere until every item is completed, then checks each has exactly one apply.
    private async Task DrainAsync(IReadOnlyList<Guid> items)
    {
        await using (var host = CrashHostProcess.Start(work, [], "None", MaximumAttempts))
        {
            foreach (var item in items)
            {
                await Eventually.StateAsync(work, item, "completed");
            }
        }

        foreach (var item in items)
        {
            Assert.Equal(1L, await work.AppliesAsync(item));
        }
    }

    private static UnitOfWorkScope FreshScope(out string listed)
    {
        var organization = ResourceId.New(ResourceTypes.Organization);
        listed = "org:" + organization.Value;
        return Owners.Organization(organization);
    }
}
