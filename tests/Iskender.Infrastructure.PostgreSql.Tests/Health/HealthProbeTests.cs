using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Health;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules;

namespace Iskender.Infrastructure.PostgreSql.Tests.Health;

/// <summary>
/// The health probe's reasons (User Story 4 scenarios 1 to 3, FR-025, SC-006). Each scenario is a named row, and
/// <see cref="TheScenariosProduceEveryReason"/> proves the rows cover all 10 reasons. Every unhealthy result returns
/// within <c>healthProbeTimeout</c> plus the fixed 2-second allowance.
/// </summary>
public class HealthProbeTests(PostgreSqlFixture fixture)
{
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(2);

    private static readonly string CanaryHost = CapturingDiagnostics.Canary + ".localhost";

    private static readonly Dictionary<string, (PersistenceHealthReason Expected, Func<PostgreSqlFixture, Task<Probed>> Run)> Scenarios = new(StringComparer.Ordinal)
    {
        ["a migrated database"] = (PersistenceHealthReason.Ok, MigratedAsync),
        ["a closed local port"] = (PersistenceHealthReason.Unreachable, ClosedPortAsync),
        ["a wrong password"] = (PersistenceHealthReason.AuthenticationFailed, WrongPasswordAsync),
        ["no credential provider"] = (PersistenceHealthReason.AuthenticationFailed, NoProviderAsync),
        ["a provider that throws"] = (PersistenceHealthReason.AuthenticationFailed, ThrowingProviderAsync),
        ["a server that never answers"] = (PersistenceHealthReason.Timeout, SilentServerAsync),
        ["the migration lock held"] = (PersistenceHealthReason.MigrationInProgress, LockHeldAsync),
        ["an empty database"] = (PersistenceHealthReason.SchemaBehind, EmptyAsync),
        ["a module below Minimum"] = (PersistenceHealthReason.SchemaBehind, BelowMinimumAsync),
        ["a module beyond Maximum"] = (PersistenceHealthReason.SchemaAhead, BeyondMaximumAsync),
        ["an edited applied script"] = (PersistenceHealthReason.ChecksumMismatch, EditedScriptAsync),
        ["USAGE revoked on isk_alpha"] = (PersistenceHealthReason.InsufficientPrivilege, UsageRevokedAsync),
        ["SELECT revoked on the history"] = (PersistenceHealthReason.InsufficientPrivilege, HistoryRevokedAsync),
        ["an unclassified history failure"] = (PersistenceHealthReason.Unexpected, UnclassifiedFailureAsync),
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> ScenarioNames => [.. Scenarios.Keys];

    [Fact]
    public void TheScenariosProduceEveryReason() =>
        Assert.Equal(Enum.GetValues<PersistenceHealthReason>().Order(), Scenarios.Values.Select(scenario => scenario.Expected).Distinct().Order());

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public async Task EachScenarioReportsItsReasonWithinTheBound(string scenario)
    {
        fixture.RequirePostgreSql();
        var (expected, run) = Scenarios[scenario];

        var probed = await run(fixture);

        Assert.Equal(expected, probed.Result.Reason);
        Assert.Equal(expected == PersistenceHealthReason.Ok ? PersistenceHealthStatus.Healthy : PersistenceHealthStatus.Unhealthy, probed.Result.Status);
        Assert.True(probed.Elapsed <= probed.Timeout + Allowance, $"The probe took {probed.Elapsed}, over {probed.Timeout} plus {Allowance}.");
    }

    [Fact]
    public async Task ACallerTokenThatFiresDuringTheProbeThrowsCancelled()
    {
        fixture.RequirePostgreSql();
        using var server = SilentServer.Start();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database, Endpoint(server.Port, "30s"));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        caller.CancelAfter(TimeSpan.FromMilliseconds(300));
        var probe = new PostgreSqlHealthProbe(store.Sources, FixtureModules.Release1, TimeProvider.System, store.Logger);

        var failure = await Assert.ThrowsAsync<GatewayException>(async () => await probe.CheckAsync(caller.Token));
        CapturingDiagnostics.Shared.Report(failure);

        Assert.Equal(ErrorCodes.PersistenceCancelled, failure.Code);
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain(store.Logger.Events, logged => logged.Id.Id == 4013);
    }

    [Fact]
    public async Task AMigrationLockInAnotherDatabaseDoesNotCount()
    {
        fixture.RequirePostgreSql();
        await using var other = await ScratchDatabase.CreateAsync(fixture);
        await using var holder = await fixture.OpenSuperuserAsync(other.Name);
        await using (var take = new Npgsql.NpgsqlCommand($"SELECT pg_advisory_lock({MigrationLock.Key})", holder))
        {
            await take.ExecuteScalarAsync(Token);
        }

        var probed = await MigratedAsync(fixture);

        Assert.Equal(PersistenceHealthResult.Healthy, probed.Result);
    }

    [Fact]
    public async Task Event4013IsLoggedOnlyWhenTheReasonChanges()
    {
        fixture.RequirePostgreSql();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        var probe = new PostgreSqlHealthProbe(store.Sources, FixtureModules.Release1, TimeProvider.System, store.Logger);

        await probe.CheckAsync(Token);
        await probe.CheckAsync(Token);
        await store.Runner(FixtureModules.Release1).ApplyAsync(new MigrationApplyRequest(), Token);
        await probe.CheckAsync(Token);
        await probe.CheckAsync(Token);

        var changes = store.Logger.Events.Where(logged => logged.Id.Id == 4013).Select(logged => logged.Fields.ToDictionary(field => field.Key, field => (string?)field.Value)).ToList();
        Assert.Equal(2, changes.Count);
        Assert.Equal(("unhealthy", "schema_behind", "(none)"), (changes[0]["Outcome"], changes[0]["Reason"], changes[0]["PreviousReason"]));
        Assert.Equal(("healthy", "ok", "schema_behind"), (changes[1]["Outcome"], changes[1]["Reason"], changes[1]["PreviousReason"]));
    }

    private static async Task<Probed> MigratedAsync(PostgreSqlFixture fixture)
    {
        await using var database = await ScratchDatabase.CreateAsync(fixture, PostgreSqlFixture.MigratedTemplate);
        return await ProbeAsync(database, FixtureModules.Release1);
    }

    private static async Task<Probed> ClosedPortAsync(PostgreSqlFixture fixture)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        return await ProbeAsync(database, FixtureModules.Release1, Endpoint(port));
    }

    private static async Task<Probed> WrongPasswordAsync(PostgreSqlFixture fixture)
    {
        await using var database = await ScratchDatabase.CreateAsync(fixture, PostgreSqlFixture.MigratedTemplate);
        var wrong = new TestCredentialProvider(new Dictionary<string, string>(StringComparer.Ordinal) { [TestSettings.RuntimeSecret.Name] = "not-the-password" });
        await using var sources = new PostgreSqlDataSources(TestSettings.For(database), wrong);
        return await ProbeAsync(sources, FixtureModules.Release1);
    }

    private static async Task<Probed> NoProviderAsync(PostgreSqlFixture fixture)
    {
        await using var database = await ScratchDatabase.CreateAsync(fixture, PostgreSqlFixture.MigratedTemplate);
        await using var sources = new PostgreSqlDataSources(TestSettings.For(database), credentials: null);
        return await ProbeAsync(sources, FixtureModules.Release1);
    }

    private static async Task<Probed> ThrowingProviderAsync(PostgreSqlFixture fixture)
    {
        await using var database = await ScratchDatabase.CreateAsync(fixture, PostgreSqlFixture.MigratedTemplate);
        await using var store = TestStore.For(database);
        store.Credentials.Failure = new InvalidOperationException("The vault is sealed.");
        return await ProbeAsync(store.Sources, FixtureModules.Release1);
    }

    private static async Task<Probed> SilentServerAsync(PostgreSqlFixture fixture)
    {
        using var server = SilentServer.Start();
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        return await ProbeAsync(database, FixtureModules.Release1, Endpoint(server.Port, "1s"));
    }

    private static async Task<Probed> LockHeldAsync(PostgreSqlFixture fixture)
    {
        await using var database = await ScratchDatabase.CreateAsync(fixture, PostgreSqlFixture.MigratedTemplate);
        await using var holder = await fixture.OpenSuperuserAsync(database.Name);
        await using (var take = new Npgsql.NpgsqlCommand($"SELECT pg_advisory_lock({MigrationLock.Key})", holder))
        {
            await take.ExecuteScalarAsync(Token);
        }

        return await ProbeAsync(database, FixtureModules.Release1);
    }

    private static async Task<Probed> EmptyAsync(PostgreSqlFixture fixture)
    {
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        return await ProbeAsync(database, FixtureModules.Release1);
    }

    // Alpha is applied and beta is not, so beta's schema version is below its Minimum of 1 while the history exists.
    private static async Task<Probed> BelowMinimumAsync(PostgreSqlFixture fixture)
    {
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([FixtureModules.AlphaRelease1])).ApplyAsync(new MigrationApplyRequest(), Token);
        return await ProbeAsync(store.Sources, FixtureModules.Release1);
    }

    // Alpha 6 is above release 1's Maximum of 5. Alpha 4, which release 1 does not know, is inside the range and ignored.
    private static async Task<Probed> BeyondMaximumAsync(PostgreSqlFixture fixture)
    {
        await using var database = await ScratchDatabase.CreateAsync(fixture);
        await using var store = TestStore.For(database);
        await store.Runner(new MigrationSet([FixtureModules.AlphaNext, FixtureModules.Beta])).ApplyAsync(new MigrationApplyRequest(), Token);
        return await ProbeAsync(store.Sources, FixtureModules.Release1);
    }

    private static async Task<Probed> EditedScriptAsync(PostgreSqlFixture fixture)
    {
        await using var database = await ScratchDatabase.CreateAsync(fixture, PostgreSqlFixture.MigratedTemplate);
        return await ProbeAsync(database, new MigrationSet([FixtureModules.AlphaEdited, FixtureModules.Beta]));
    }

    private static async Task<Probed> UsageRevokedAsync(PostgreSqlFixture fixture)
    {
        await using var database = await ScratchDatabase.CreateAsync(fixture, PostgreSqlFixture.MigratedTemplate);
        await fixture.ExecuteAsync(database.Name, $"REVOKE USAGE ON SCHEMA isk_alpha FROM {PostgreSqlFixture.RuntimeGroup}");
        return await ProbeAsync(database, FixtureModules.Release1);
    }

    private static async Task<Probed> HistoryRevokedAsync(PostgreSqlFixture fixture)
    {
        await using var database = await ScratchDatabase.CreateAsync(fixture, PostgreSqlFixture.MigratedTemplate);
        await fixture.ExecuteAsync(database.Name, $"REVOKE SELECT ON isk_migrations.history FROM {PostgreSqlFixture.RuntimeGroup}");
        return await ProbeAsync(database, FixtureModules.Release1);
    }

    // Division by zero (22012) is a failure no other reason describes.
    private static async Task<Probed> UnclassifiedFailureAsync(PostgreSqlFixture fixture)
    {
        await using var database = await ScratchDatabase.CreateAsync(fixture, PostgreSqlFixture.MigratedTemplate);
        await using var store = TestStore.For(database);
        var probe = new PostgreSqlHealthProbe(store.Sources, FixtureModules.Release1, TimeProvider.System, store.Logger)
        {
            HistoryQuery = "SELECT module, version, checksum FROM isk_migrations.history WHERE version / 0 = 1",
        };
        return await ProbeAsync(probe, store.Sources.Settings.HealthProbeTimeout);
    }

    private static async Task<Probed> ProbeAsync(ClassDatabase database, MigrationSet migrations, IReadOnlyDictionary<string, object>? overrides = null)
    {
        await using var store = TestStore.For(database, overrides);
        return await ProbeAsync(store.Sources, migrations);
    }

    private static Task<Probed> ProbeAsync(PostgreSqlDataSources sources, MigrationSet migrations) =>
        ProbeAsync(new PostgreSqlHealthProbe(sources, migrations, TimeProvider.System, new CapturingDiagnostics.Logger()), sources.Settings.HealthProbeTimeout);

    private static async Task<Probed> ProbeAsync(PostgreSqlHealthProbe probe, TimeSpan timeout)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await probe.CheckAsync(Token);
        CapturingDiagnostics.Shared.Reported.Enqueue(result.ToString());
        return new Probed(result, Stopwatch.GetElapsedTime(started), timeout);
    }

    private static Dictionary<string, object> Endpoint(int port, string? healthProbeTimeout = null)
    {
        // The canary as the host (SC-005): a .localhost name resolves to loopback, and must never be reported.
        var overrides = new Dictionary<string, object>(StringComparer.Ordinal) { ["host"] = CanaryHost, ["port"] = port };
        if (healthProbeTimeout is not null)
        {
            overrides["healthProbeTimeout"] = healthProbeTimeout;
        }

        return overrides;
    }

    private sealed record Probed(PersistenceHealthResult Result, TimeSpan Elapsed, TimeSpan Timeout);

    /// <summary>A local listener that accepts connections and never answers, so the probe's own timeout must fire.</summary>
    private sealed class SilentServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<TcpClient> _clients = [];
        private readonly CancellationTokenSource _stop = new();

        private SilentServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptAsync();
        }

        public int Port { get; }

        public static SilentServer Start() => new();

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            lock (_clients)
            {
                _clients.ForEach(client => client.Dispose());
            }

            _stop.Dispose();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    lock (_clients)
                    {
                        _clients.Add(client);
                    }
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // The server is stopping.
            }
        }
    }
}
