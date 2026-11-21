using Iskender.Domain.Ports;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Crash;

/// <summary>
/// Poison on crash (ISK-0009 T034, US3.3, SC-003, research R7): an item that crashes its worker on every attempt, killed
/// with <c>SIGKILL</c> each time, is poisoned by the claim that finds its count at the maximum, keeps
/// <c>abandoned</c> / <c>work.attempt_abandoned</c>, and is never dispatched again.
/// </summary>
public class PoisonOnCrashTests(WorkFixture work) : IClassFixture<WorkFixture>
{
    private const int MaximumAttempts = 3;

    private static readonly WorkKind Crash = new("applies", "crash");

    [Fact]
    public async Task AnItemThatCrashesEveryAttemptIsPoisonedAfterExactlyTheMaximumAndNeverDispatchedAgain()
    {
        work.RequirePostgreSql();
        var item = await work.EnqueueAsync(WorkFixture.FreshOrganization(), Crash, "crash");

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            await using var host = CrashHostProcess.Start(work, [], "None", MaximumAttempts);
            await host.ReachedAsync("CrashHandler");
            await host.KillAsync();
            Assert.Equal($"pending|{attempt}", await work.ItemAsync(item));
        }

        await using (var host = CrashHostProcess.Start(work, [], "None", MaximumAttempts))
        {
            await Eventually.StateAsync(work, item, "poisoned");
            Assert.False(host.HasPrinted("CrashHandler"), "The item was dispatched after its maximum.");
        }

        Assert.Equal("poisoned|3", await work.ItemAsync(item));
        Assert.Equal("abandoned|work.attempt_abandoned", await work.ScalarAsync<string>($"SELECT last_failure_class || '|' || last_failure_code FROM isk_work.items WHERE item_id = '{item}'"));
    }
}
