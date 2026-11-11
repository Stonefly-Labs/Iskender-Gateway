using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Health;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Iskender.Infrastructure.PostgreSql.Tests.Modules;
using Iskender.Infrastructure.PostgreSql.Tests.Modules.Alpha;

namespace Iskender.Infrastructure.PostgreSql.Tests.Diagnostics;

/// <summary>
/// The canary check (SC-005, FR-026, FR-031). One value is the database name, both login names' prefix, and both
/// passwords in a dedicated class database, and the host in the unreachable and timeout probe tests. It must appear 0
/// times in any probe result, exception message, log message or field, metric label, or activity tag. This class checks
/// what it drives; <see cref="PostgreSqlFixture"/> checks everything the assembly captured when the run ends.
/// </summary>
public class CanaryTests(CanaryTests.CanaryDatabase database) : IClassFixture<CanaryTests.CanaryDatabase>
{
    private const string Canary = CapturingDiagnostics.Canary;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheCanaryNeverAppearsInWhatTheAdapterReports()
    {
        database.Fixture.RequirePostgreSql();
        var reported = new List<string>();
        var credentials = new TestCredentialProvider(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TestSettings.RuntimeSecret.Name] = Canary,
            [TestSettings.MigrationSecret.Name] = Canary,
        });
        var logger = new CapturingDiagnostics.Logger();
        await using var sources = new PostgreSqlDataSources(TestSettings.Read(database.Document()), credentials);
        var factory = new PostgreSqlUnitOfWorkFactory(sources, TimeProvider.System, logger);
        var runner = new PostgreSqlMigrationRunner(sources, FixtureModules.Release1, TimeProvider.System, logger);
        var probe = new PostgreSqlHealthProbe(sources, FixtureModules.Release1, TimeProvider.System, logger);
        var org = Owners.Organization(Owners.OrgA);

        reported.Add((await probe.CheckAsync(Token)).ToString());
        reported.AddRange((await runner.ApplyAsync(new MigrationApplyRequest(), Token)).Applied.Select(migration => migration.ToString()));
        reported.Add((await probe.CheckAsync(Token)).ToString());
        await using (var unit = await factory.OpenAsync(org, TestStore.Options(), Token))
        {
            // A positive control: the canary login really connected.
            Assert.Equal(Canary + "_runtime", await PostgreSqlUnitOfWork.From(unit).FrameworkCommand("SELECT current_user::text").ScalarAsync<string>(Token));
            await WidgetRepository.InsertAsync(unit, "w-canary", "canary", Token);
            await unit.CommitAsync(Token);
        }

        await using (var unit = await factory.OpenAsync(org, TestStore.Options(), Token))
        {
            reported.Add(Caught(await Assert.ThrowsAsync<GatewayException>(() => WidgetRepository.InsertAsync(unit, "w-canary", "again", Token))));
        }

        await using (var unit = await factory.OpenAsync(org, TestStore.Options(), Token))
        {
            reported.Add(Caught(await Assert.ThrowsAsync<GatewayException>(async () => await PostgreSqlUnitOfWork.From(unit).FrameworkCommand("SELECT 1 / 0").ExecuteAsync(Token))));
        }

        // Four steps reach Alpha 3, whose reverse is data-losing, so the request is refused (event 4012) and nothing reverses.
        reported.Add(Caught(await Assert.ThrowsAsync<GatewayException>(async () => await runner.ReverseAsync(new MigrationReverseRequest { Steps = 4 }, Token))));
        credentials.Failure = new InvalidOperationException($"The vault refused {Canary}_runtime with password {Canary}.");
        sources.Runtime.Clear();
        reported.Add(Caught(await Assert.ThrowsAsync<GatewayException>(async () => await factory.OpenAsync(org, TestStore.Options(), Token))));
        reported.Add((await probe.CheckAsync(Token)).ToString());

        Assert.Contains(PersistenceHealthResult.Unhealthy(PersistenceHealthReason.AuthenticationFailed).ToString(), reported);
        Assert.DoesNotContain(reported, text => text.Contains(Canary, StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(logger.Events);
        Assert.DoesNotContain(logger.Events, entry => Leaks(entry));
        Assert.Empty(CapturingDiagnostics.Shared.CanaryHits());
    }

    private static string Caught(GatewayException exception)
    {
        CapturingDiagnostics.Shared.Report(exception);
        return exception.ToString();
    }

    private static bool Leaks(CapturedLog entry) =>
        entry.Message.Contains(Canary, StringComparison.OrdinalIgnoreCase)
        || (entry.Exception?.ToString().Contains(Canary, StringComparison.OrdinalIgnoreCase) ?? false)
        || entry.Fields.Any(field => $"{field.Key}={field.Value}".Contains(Canary, StringComparison.OrdinalIgnoreCase));

    /// <summary>A database named after the canary, with canary-prefixed runtime and migration logins whose passwords are the canary.</summary>
    public sealed class CanaryDatabase(PostgreSqlFixture fixture) : IAsyncLifetime
    {
        public PostgreSqlFixture Fixture { get; } = fixture;

        public async ValueTask InitializeAsync()
        {
            if (Fixture.UnavailableReason is not null)
            {
                return;
            }

            await Fixture.ExecuteAsync("postgres", $"CREATE ROLE {Canary}_runtime LOGIN PASSWORD '{Canary}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS IN ROLE {PostgreSqlFixture.RuntimeGroup}");
            await Fixture.ExecuteAsync("postgres", $"CREATE ROLE {Canary}_migrator LOGIN PASSWORD '{Canary}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS");
            await Fixture.ExecuteAsync("postgres", $"CREATE DATABASE {Canary} TEMPLATE {PostgreSqlFixture.EmptyTemplate}");
            await Fixture.ExecuteAsync("postgres", $"GRANT CREATE ON DATABASE {Canary} TO {Canary}_migrator");
        }

        public async ValueTask DisposeAsync()
        {
            if (Fixture.UnavailableReason is not null)
            {
                return;
            }

            await Fixture.DropDatabaseAsync(Canary);
            await Fixture.ExecuteAsync("postgres", $"DROP ROLE IF EXISTS {Canary}_runtime; DROP ROLE IF EXISTS {Canary}_migrator");
        }

        /// <summary>The settings document for the canary database and logins.</summary>
        public string Document() => TestSettings.Document(Fixture, Canary, new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["username"] = Canary + "_runtime",
            ["migration"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["username"] = Canary + "_migrator",
                ["password"] = TestSettings.Placeholder(TestSettings.MigrationSecret),
            },
        });
    }
}
