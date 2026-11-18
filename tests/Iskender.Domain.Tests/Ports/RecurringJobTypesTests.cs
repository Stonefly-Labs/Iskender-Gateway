using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class RecurringJobTypesTests
{
    public static TheoryData<string?, TimeSpan, int, bool> Registrations => new()
    {
        { "coordination.purge_expired", TimeSpan.FromMinutes(1), 1, true },
        { "work.retention", TimeSpan.FromHours(24), 10_000, true },
        { "work", TimeSpan.FromMinutes(5), 500, false },
        { "Work.retention", TimeSpan.FromMinutes(5), 500, false },
        { null, TimeSpan.FromMinutes(5), 500, false },
        { "work.retention", TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1), 500, false },
        { "work.retention", TimeSpan.FromHours(24) + TimeSpan.FromTicks(1), 500, false },
        { "work.retention", TimeSpan.FromMinutes(5), 0, false },
        { "work.retention", TimeSpan.FromMinutes(5), 10_001, false },
    };

    [Theory]
    [MemberData(nameof(Registrations))]
    public void RegistrationBoundsAreChecked(string? name, TimeSpan interval, int batch, bool accepted)
    {
        RecurringJobRegistration Build() => new(name!, interval, batch, typeof(NoopJob));

        if (accepted)
        {
            Assert.Equal((name, interval, batch, typeof(NoopJob)), (Build().Name, Build().DefaultInterval, Build().DefaultBatch, Build().JobType));
        }
        else
        {
            Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(Build).Code);
        }
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(IRecurringJob))]
    [InlineData(null)]
    public void TheJobTypeMustBeAConcreteJob(Type? jobType)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new RecurringJobRegistration("work.retention", TimeSpan.FromHours(1), 500, jobType!)).Code);
    }

    [Fact]
    public async Task SavingACursorCallsTheRunnersWriteAndMovesTheCursor()
    {
        var saved = new List<string?>();
        var context = new RecurringJobContext(500, "start", (cursor, _) =>
        {
            saved.Add(cursor);
            return ValueTask.CompletedTask;
        });

        await context.SaveCursorAsync(new string('c', 512), TestContext.Current.CancellationToken);
        await context.SaveCursorAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal([new string('c', 512), null], saved);
        Assert.Null(context.Cursor);
        Assert.Equal(500, context.Batch);
    }

    [Fact]
    public async Task ACursorOverFiveHundredTwelveCharactersIsRejectedWithoutAWrite()
    {
        var writes = 0;
        var context = new RecurringJobContext(1, null, (_, _) =>
        {
            writes++;
            return ValueTask.CompletedTask;
        });

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await context.SaveCursorAsync(new string('c', 513), TestContext.Current.CancellationToken));

        Assert.Same(ErrorCodes.ValueInvalid, failure.Code);
        Assert.Equal(0, writes);
        Assert.Null(context.Cursor);
    }

    [Fact]
    public async Task AFailedWriteLeavesTheCursorUnchanged()
    {
        var context = new RecurringJobContext(1, "kept", (_, _) => throw new GatewayException(ErrorCodes.CoordinationFencingRejected));

        await Assert.ThrowsAsync<GatewayException>(async () => await context.SaveCursorAsync("next", TestContext.Current.CancellationToken));

        Assert.Equal("kept", context.Cursor);
    }

    [Fact]
    public void AContextChecksItsArguments()
    {
        Assert.Throws<GatewayException>(() => new RecurringJobContext(0, null, (_, _) => ValueTask.CompletedTask));
        Assert.Throws<GatewayException>(() => new RecurringJobContext(1, new string('c', 513), (_, _) => ValueTask.CompletedTask));
        Assert.Throws<GatewayException>(() => new RecurringJobContext(1, null, null!));
    }

    [Fact]
    public void TheOutcomesAreCompletedAndPartial()
    {
        Assert.Equal([RecurringJobOutcome.Completed, RecurringJobOutcome.Partial], Enum.GetValues<RecurringJobOutcome>());
    }

    internal sealed class NoopJob : IRecurringJob
    {
        public string Name => "work.noop";

        public ValueTask<RecurringJobOutcome> RunAsync(RecurringJobContext context, CancellationToken cancellationToken) => ValueTask.FromResult(RecurringJobOutcome.Completed);
    }
}
