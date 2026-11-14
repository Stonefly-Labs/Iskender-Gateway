using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Reflection;
using Iskender.Domain.Ports;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>
/// The collection for tests that observe process-wide meters, so no other host publishes or disposes an instrument
/// while they watch. Its tests run alone.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessMeters
{
    public const string Name = "Process meters";
}

/// <summary>
/// User Story 4, acceptance scenario 4, and FR-010: stopping and disposing a host disposes every disposable Iskender
/// singleton it created, stops the single-instance guard's timer, and disposes the held-connection meter, and a second
/// disposal is safe (hosting contract, Lifetimes).
/// </summary>
[Collection(ProcessMeters.Name)]
public class LifetimesDisposalTests
{
    private const string HeldConnectionsInstrument = "iskender.persistence.pool.connections";

    // A host name under the reserved .invalid domain, which never resolves. The leases select the PostgreSQL fake, so
    // the guard takes a lease and starts its timer, and the other primitives select the Redis fake.
    private const string Document = """
        {"iskender":{
          "persistence":{"host":"iskender-database.invalid","database":"iskender","username":"gateway","password":{"secretRef":"database/runtime"}},
          "coordination":{"topology":"single_instance","leases":{"coordinator":"postgresql"},"atomic":{"coordinator":"redis"},"cache":{"coordinator":"redis"}}}}
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StopAndDisposeReleaseEverythingTheHostCreatedAndASecondDisposeIsSafe()
    {
        var time = new RecordingTime();
        var fakes = new ConcurrentBag<FakePort>();
        using var meters = new HeldConnectionMeters();
        var host = await EmbeddedHost.CreateAsync(builder => Compose(builder, time, fakes), ct: Token);

        var disposables = IskenderDisposables(host);
        var guard = host.Services.GetRequiredService<ISingleInstanceStatus>();

        Assert.Equal(SingleInstanceState.Held, guard.State);
        Assert.NotEmpty(time.Timers);
        Assert.Contains(fakes, fake => fake.Kind == CoordinatorKind.PostgreSql && fake.Port == typeof(ILeaseCoordinator));
        Assert.Contains(fakes, fake => fake.Kind == CoordinatorKind.Redis && fake.Port == typeof(IAtomicOperations));
        Assert.Contains(fakes, fake => fake.Kind == CoordinatorKind.Redis && fake.Port == typeof(IVersionedCache));
        Assert.Equal(1, meters.Published);

        await host.StopAsync(Token);
        await host.DisposeAsync();

        // A registration's health probe is part of the adapter's own singleton, which its container disposes, so the
        // fakes that stand for ports the composition owns are the ones it must dispose.
        Assert.All(fakes.Where(fake => fake.Port != typeof(ICoordinationHealthProbe)), fake => Assert.True(fake.Disposed > 0, $"{fake.Kind} {fake.Port.Name} was not disposed"));
        Assert.Empty(disposables.Where(disposable => !IsDisposed(disposable, meters)).Select(disposable => disposable.GetType().FullName));
        Assert.All(time.Timers, timer => Assert.True(timer.Disposed, "a timer was not stopped"));
        Assert.Equal(1, meters.Completed);
        Assert.NotEmpty(disposables);
        Assert.Equal(
            ["ComposedCoordination", "IskenderConfiguration", "PostgreSqlDataSources", "SingleInstanceGuard"],
            disposables.Select(disposable => disposable.GetType().Name).Order(StringComparer.Ordinal));

        // A second disposal of the host, and of each disposable it created, is safe.
        await host.DisposeAsync();
        foreach (var disposable in disposables)
        {
            if (disposable is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                ((IDisposable)disposable).Dispose();
            }
        }

        Assert.All(fakes.Where(fake => fake.Port != typeof(ICoordinationHealthProbe)), fake => Assert.True(fake.Disposed > 0));
    }

    // Whether a disposable records that it was disposed: by its own disposed flag, or, for the two that hold none, by the
    // data sources' held-connection meter having completed and by the configuration's gate having been disposed.
    private static bool IsDisposed(object disposable, HeldConnectionMeters meters)
    {
        switch (disposable.GetType().Name)
        {
            case "PostgreSqlDataSources":
                return meters.Completed == meters.Published && meters.Published > 0;
            case "IskenderConfiguration":
                var gate = (SemaphoreSlim)disposable.GetType().GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(disposable)!;
                try
                {
                    _ = gate.AvailableWaitHandle;
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return true;
                }
        }

        var flag = disposable.GetType().GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{disposable.GetType().FullName} has no disposed flag for this test to read; add a probe for it.");
        return flag.GetValue(disposable) switch
        {
            bool value => value,
            int value => value != 0,
            _ => throw new InvalidOperationException($"{disposable.GetType().FullName}._disposed is neither a bool nor an int."),
        };
    }

    private static void Compose(WebApplicationBuilder builder, RecordingTime time, ConcurrentBag<FakePort> fakes)
    {
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddIskender(options => options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, Document));
        builder.Services.AddIskenderCoordination();
        builder.Services.AddIskenderPostgreSqlPersistence();
        CoordinatorRegistration.Add(builder.Services, new CompositionModule("fake-postgresql", [], ["coordination"]), new CoordinatorRegistration(
            CoordinatorKind.PostgreSql,
            CoordinatorCapabilities.PostgreSql,
            _ => FakePort.Create<ICoordinationHealthProbe>(CoordinatorKind.PostgreSql, fakes, time),
            atomic: _ => FakePort.Create<IAtomicOperations>(CoordinatorKind.PostgreSql, fakes, time),
            leases: _ => FakePort.Create<ILeaseCoordinator>(CoordinatorKind.PostgreSql, fakes, time),
            fencing: _ => FakePort.Create<IFencingGuard>(CoordinatorKind.PostgreSql, fakes, time),
            cache: _ => FakePort.Create<IVersionedCache>(CoordinatorKind.PostgreSql, fakes, time)));
        CoordinatorRegistration.Add(builder.Services, new CompositionModule("fake-redis", [], ["coordination"]), new CoordinatorRegistration(
            CoordinatorKind.Redis,
            CoordinatorCapabilities.Redis,
            _ => FakePort.Create<ICoordinationHealthProbe>(CoordinatorKind.Redis, fakes, time),
            atomic: _ => FakePort.Create<IAtomicOperations>(CoordinatorKind.Redis, fakes, time),
            leases: _ => FakePort.Create<ILeaseCoordinator>(CoordinatorKind.Redis, fakes, time),
            cache: _ => FakePort.Create<IVersionedCache>(CoordinatorKind.Redis, fakes, time)));
    }

    // Every disposable instance of an Iskender singleton, resolved once start has composed them.
    private static List<object> IskenderDisposables(EmbeddedHost host)
    {
        var registered = host.Services.GetRequiredService<IServiceProviderIsService>();
        var instances = new List<object>();
        foreach (var serviceType in IskenderServiceTypes().Where(registered.IsService))
        {
            foreach (var instance in host.Services.GetServices(serviceType))
            {
                if (instance is (IDisposable or IAsyncDisposable) and not FakePort && !instances.Contains(instance, ReferenceEqualityComparer.Instance))
                {
                    instances.Add(instance);
                }
            }
        }

        return instances;
    }

    // The service types the composition registers, other than the request scope's and the host's own.
    private static List<Type> IskenderServiceTypes()
    {
        var builder = WebApplication.CreateBuilder();
        var before = builder.Services.Count;
        Compose(builder, new RecordingTime(), []);
        return builder.Services.Skip(before)
            .Where(descriptor => descriptor.Lifetime == ServiceLifetime.Singleton && !descriptor.IsKeyedService && descriptor.ServiceType != typeof(TimeProvider))
            .Select(descriptor => descriptor.ServiceType)
            .Distinct()
            .ToList();
    }

    /// <summary>A coordinator port that grants every lease, refuses everything else, and records its disposal.</summary>
    public class FakePort : DispatchProxy, IAsyncDisposable
    {
        private int _disposed;

        public CoordinatorKind Kind { get; private set; }

        public Type Port { get; private set; } = typeof(object);

        public int Disposed => Volatile.Read(ref _disposed);

        private TimeProvider Time { get; set; } = TimeProvider.System;

        public static T Create<T>(CoordinatorKind kind, ConcurrentBag<FakePort> fakes, TimeProvider time)
            where T : class
        {
            var port = Create<T, FakePort>();
            var fake = (FakePort)(object)port;
            fake.Kind = kind;
            fake.Port = typeof(T);
            fake.Time = time;
            fakes.Add(fake);
            return port;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposed);
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(ILeaseCoordinator.AcquireAsync) => new ValueTask<LeaseAcquisition>(LeaseAcquisition.Acquired(Lease.Grant(
                (CoordinationScope)args![0]!, (CoordinationName)args[1]!, new FencingToken(1), Guid.NewGuid(), Kind, (TimeSpan)args[2]!, Time.GetTimestamp(), TimeSpan.Zero, Time))),
            nameof(ILeaseCoordinator.ReleaseAsync) => ValueTask.CompletedTask,
            _ => throw new NotSupportedException(targetMethod?.Name),
        };
    }

    // A clock whose timers never fire and record whether they were stopped.
    private sealed class RecordingTime : TimeProvider
    {
        private readonly ConcurrentBag<RecordingTimer> _timers = [];

        public IReadOnlyCollection<RecordingTimer> Timers => _timers;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new RecordingTimer();
            _timers.Add(timer);
            return timer;
        }
    }

    private sealed class RecordingTimer : ITimer
    {
        private int _disposed;

        public bool Disposed => Volatile.Read(ref _disposed) > 0;

        public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;

        public void Dispose() => Interlocked.Increment(ref _disposed);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    // Counts the held-connection instruments published, and those whose meter was disposed, while it listens. An
    // instrument already published when it starts, from a meter another test left alive, is not counted.
    private sealed class HeldConnectionMeters : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentDictionary<Instrument, bool> _instruments = new();
        private readonly HashSet<Instrument> _earlier;

        public HeldConnectionMeters()
        {
            _listener.InstrumentPublished = (instrument, listening) =>
            {
                if (instrument.Name == HeldConnectionsInstrument)
                {
                    _instruments.TryAdd(instrument, false);
                    listening.EnableMeasurementEvents(instrument);
                }
            };
            _listener.MeasurementsCompleted = (instrument, _) =>
            {
                if (_instruments.ContainsKey(instrument))
                {
                    _instruments[instrument] = true;
                }
            };
            _listener.Start();
            _earlier = [.. _instruments.Keys];
        }

        public int Published => _instruments.Keys.Count(instrument => !_earlier.Contains(instrument));

        public int Completed => _instruments.Count(pair => pair.Value && !_earlier.Contains(pair.Key));

        public void Dispose() => _listener.Dispose();
    }
}
