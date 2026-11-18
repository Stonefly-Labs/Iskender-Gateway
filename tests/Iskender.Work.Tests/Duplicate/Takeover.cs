using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Duplicate;

/// <summary>
/// Two dispatchers holding one item (ISK-0009 T026, T027): A holds after its attempt record with its renewals paused,
/// so its shard lease expires, and B then takes the shard, claims the same item, and holds at the same point.
/// </summary>
internal sealed class Takeover : IAsyncDisposable
{
    private Takeover(TestDispatcher a, InjectionHook heldA, Task<bool> passA, TestDispatcher b, InjectionHook heldB)
    {
        A = a;
        HeldA = heldA;
        PassA = passA;
        B = b;
        HeldB = heldB;
    }

    public TestDispatcher A { get; }

    public InjectionHook HeldA { get; }

    public Task<bool> PassA { get; }

    public TestDispatcher B { get; }

    public InjectionHook HeldB { get; }

    public static async Task<Takeover> StartAsync(WorkFixture work)
    {
        var a = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], WorkTestSettings.Fast);
        var heldA = a.Injection.At(WorkInjectionPoint.AfterAttemptCommit);
        var passA = a.StartPass();
        await heldA.Reached.WaitAsync(Eventually.Bound, TestContext.Current.CancellationToken);
        a.Leases.Pause();

        var b = new TestDispatcher(work, [ApplyRecordingHandler.Registration()], WorkTestSettings.Fast);
        var heldB = b.Injection.At(WorkInjectionPoint.AfterAttemptCommit);
        b.Start();
        await heldB.Reached.WaitAsync(Eventually.Bound, TestContext.Current.CancellationToken);
        return new Takeover(a, heldA, passA, b, heldB);
    }

    public async ValueTask DisposeAsync()
    {
        HeldA.Release();
        HeldB.Release();
        await PassA;
        await A.DisposeAsync();
        await B.DisposeAsync();
    }
}
