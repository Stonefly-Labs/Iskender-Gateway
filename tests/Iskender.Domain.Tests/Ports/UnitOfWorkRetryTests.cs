using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;

namespace Iskender.Domain.Tests.Ports;

public class UnitOfWorkRetryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PersistenceFailure Transient => new(ErrorCodes.PersistenceConcurrencyConflict, PersistenceFailureClass.TransientConflict);

    [Fact]
    public async Task ATransientConflictMarkedSafeRunsTheWholeUnitAgain()
    {
        var factory = new FakeFactory(Fail(Transient), Succeed());
        var logger = new Logger();

        var result = await Run(factory, safe: true, logger: logger);

        Assert.Equal(2, result);
        Assert.Equal(2, factory.Opened);
        Assert.Equal(2, factory.Disposed);
        Assert.Equal(1, factory.Committed);
        Assert.Equal([4005], logger.Events.Select(entry => entry.Id));
        Assert.Contains(("FailureClass", "transient_conflict"), logger.Events[0].Fields);
    }

    [Fact]
    public async Task ATransientConflictAtCommitIsRetriedToo()
    {
        var factory = new FakeFactory(FailCommit(Transient), Succeed());

        Assert.Equal(2, await Run(factory, safe: true));
        Assert.Equal(2, factory.Opened);
    }

    public static TheoryData<PersistenceFailureClass> OtherClasses() =>
        [.. Enum.GetValues<PersistenceFailureClass>().Where(failureClass => failureClass != PersistenceFailureClass.TransientConflict)];

    [Theory]
    [MemberData(nameof(OtherClasses))]
    public async Task NoOtherClassIsRetried(PersistenceFailureClass failureClass)
    {
        var factory = new FakeFactory(Fail(new PersistenceFailure(ErrorCodes.PersistenceUnavailable, failureClass)), Succeed());

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await Run(factory, safe: true));

        Assert.Same(ErrorCodes.PersistenceUnavailable, failure.Code);
        Assert.Equal(1, factory.Opened);
        Assert.Equal(1, factory.Disposed);
    }

    [Fact]
    public async Task AUnitNotMarkedSafeIsNotRetried()
    {
        var factory = new FakeFactory(Fail(Transient), Succeed());

        Assert.Same(ErrorCodes.PersistenceConcurrencyConflict, (await Assert.ThrowsAsync<GatewayException>(async () => await Run(factory, safe: false))).Code);
        Assert.Equal(1, factory.Opened);
    }

    [Fact]
    public async Task AnUnknownCommitOutcomeIsNeverRetried()
    {
        var factory = new FakeFactory(FailCommit(new PersistenceFailure(ErrorCodes.PersistenceUnavailable, PersistenceFailureClass.TransientConflict, commitOutcomeUnknown: true)), Succeed());

        Assert.Same(ErrorCodes.PersistenceUnavailable, (await Assert.ThrowsAsync<GatewayException>(async () => await Run(factory, safe: true))).Code);
        Assert.Equal(1, factory.Opened);
    }

    [Fact]
    public async Task AFailingThirdAttemptEndsWithAConcurrencyConflict()
    {
        var factory = new FakeFactory(Fail(Transient), Fail(Transient), Fail(Transient), Succeed());
        var logger = new Logger();

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await Run(factory, safe: true, logger: logger));

        Assert.Same(ErrorCodes.PersistenceConcurrencyConflict, failure.Code);
        Assert.Null(failure.InnerException);
        Assert.Equal(UnitOfWorkRetry.MaxAttempts, factory.Opened);
        Assert.Equal(UnitOfWorkRetry.MaxAttempts, factory.Disposed);
        Assert.Equal(2, logger.Events.Count);
    }

    [Fact]
    public async Task AnExceptionWithNoRecordedFailureIsNeverRetried()
    {
        var factory = new FakeFactory((_, _) => throw new InvalidOperationException("the work's own"), Succeed());

        Assert.Equal("the work's own", (await Assert.ThrowsAsync<InvalidOperationException>(async () => await Run(factory, safe: true))).Message);
        Assert.Equal(1, factory.Opened);
        Assert.Equal(1, factory.Disposed);
    }

    [Fact]
    public async Task AFailedOpenIsNotRetried()
    {
        var factory = new FakeFactory(Succeed()) { OpenFailure = new GatewayException(ErrorCodes.PersistenceTimeout) };

        Assert.Same(ErrorCodes.PersistenceTimeout, (await Assert.ThrowsAsync<GatewayException>(async () => await Run(factory, safe: true))).Code);
        Assert.Equal(1, factory.Opened);
    }

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(25L, 25)]
    [InlineData(50L, 50)]
    [InlineData(51L, 0)]
    [InlineData(152L, 50)]
    public async Task TheJitterIsAtMostFiftyMillisecondsFromTheInjectedClock(long timestamp, int milliseconds)
    {
        var clock = new Clock(timestamp);

        Assert.Equal(3, await Run(new FakeFactory(Fail(Transient), Fail(Transient), Succeed()), safe: true, clock: clock));

        Assert.Equal(milliseconds == 0 ? [] : [TimeSpan.FromMilliseconds(milliseconds), TimeSpan.FromMilliseconds(milliseconds)], clock.Delays);
        Assert.All(clock.Delays, delay => Assert.InRange(delay, TimeSpan.Zero, UnitOfWorkRetry.MaxJitter));
    }

    [Fact]
    public async Task EachRetryIsCounted()
    {
        var module = "retry_" + Guid.NewGuid().ToString("N")[..8];
        var counted = new ConcurrentQueue<(long Value, string? Module, string? FailureClass)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "Iskender.Persistence" && instrument.Name == "iskender.persistence.retries")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var labels = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString());
            counted.Enqueue((value, labels.GetValueOrDefault("iskender.module"), labels.GetValueOrDefault("iskender.failure_class")));
        });
        listener.Start();

        await Run(new FakeFactory(Fail(Transient), Fail(Transient), Succeed()), safe: true, module: module);

        Assert.Equal([(1L, module, "transient_conflict"), (1L, module, "transient_conflict")], counted.Where(entry => entry.Module == module));
    }

    private static async Task<int> Run(FakeFactory factory, bool safe, Logger? logger = null, Clock? clock = null, string module = "alpha") =>
        await UnitOfWorkRetry.RunAsync(
            factory,
            UnitOfWorkScope.Operator,
            new UnitOfWorkOptions(module, "test.retry") { SafeToRerun = safe },
            async (unit, token) =>
            {
                await ((FakeUnit)unit).Work(token);
                return factory.Opened;
            },
            Token,
            clock ?? new Clock(0),
            logger);

    private static Func<FakeUnit, CancellationToken, ValueTask> Succeed() => (_, _) => ValueTask.CompletedTask;

    private static Func<FakeUnit, CancellationToken, ValueTask> Fail(PersistenceFailure failure) => (unit, _) =>
    {
        unit.Failure = failure;
        throw new GatewayException(failure.Code);
    };

    private static Func<FakeUnit, CancellationToken, ValueTask> FailCommit(PersistenceFailure failure) => (unit, _) =>
    {
        unit.CommitFailure = failure;
        return ValueTask.CompletedTask;
    };

    private sealed class FakeFactory(params Func<FakeUnit, CancellationToken, ValueTask>[] attempts) : IUnitOfWorkFactory
    {
        public int Opened { get; private set; }

        public int Disposed { get; set; }

        public int Committed { get; set; }

        public GatewayException? OpenFailure { get; init; }

        public ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkScope scope, UnitOfWorkOptions options, CancellationToken cancellationToken)
        {
            Opened++;
            if (OpenFailure is not null)
            {
                throw OpenFailure;
            }

            return ValueTask.FromResult<IUnitOfWork>(new FakeUnit(this, scope, attempts[Opened - 1]));
        }
    }

    private sealed class FakeUnit(FakeFactory factory, UnitOfWorkScope scope, Func<FakeUnit, CancellationToken, ValueTask> attempt) : IUnitOfWork
    {
        public UnitOfWorkScope Scope => scope;

        public UnitOfWorkIsolation Isolation => UnitOfWorkIsolation.ReadCommitted;

        public UnitOfWorkState State { get; private set; } = UnitOfWorkState.Open;

        public PersistenceFailure? Failure { get; set; }

        public PersistenceFailure? CommitFailure { get; set; }

        public ValueTask Work(CancellationToken cancellationToken) => attempt(this, cancellationToken);

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            if (CommitFailure is not null)
            {
                Failure = CommitFailure;
                State = UnitOfWorkState.Faulted;
                throw new GatewayException(CommitFailure.Code);
            }

            factory.Committed++;
            State = UnitOfWorkState.Committed;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> AcquireAdvisoryLockAsync(AdvisoryLockName name, AdvisoryLockWait wait, CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public ValueTask DisposeAsync()
        {
            factory.Disposed++;
            State = UnitOfWorkState.Closed;
            return ValueTask.CompletedTask;
        }
    }

    // A clock whose timestamp is fixed, and whose timers fire at once, recording each delay asked for.
    private sealed class Clock(long timestamp) : TimeProvider
    {
        public List<TimeSpan> Delays { get; } = [];

        public override long GetTimestamp() => timestamp;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Add(dueTime);
            callback(state);
            return new NoTimer();
        }

        private sealed class NoTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Logger : ILogger
    {
        public List<(int Id, List<(string, string?)> Fields)> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Events.Add((eventId.Id, [.. (state as IReadOnlyList<KeyValuePair<string, object?>> ?? []).Select(field => (field.Key, field.Value?.ToString()))]));
    }
}
