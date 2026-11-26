using System.Net;
using System.Net.Sockets;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Audit;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Identity;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Management.Security;

namespace Iskender.Identity.Tests.Fixture;

/// <summary>
/// The operator fixture at the store level (operator-security contract, Test fixture rule 1; ISK-0010 research R21): one
/// database per test class, built on ISK-0004's PostgreSQL fixture, with <c>coordination</c>, <c>directory</c>,
/// <c>work</c>, <c>audit</c>, and <c>identity</c> applied; operator-scope units; a validated <c>operator</c> section; a unit
/// factory whose store is unreachable; and readers of every row of <c>isk_identity</c> and <c>isk_audit</c>. A test class
/// takes it as its class fixture.
/// </summary>
public sealed class OperatorFixture(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private ScratchDatabase? _database;

    public PostgreSqlFixture Postgres { get; } = fixture;

    /// <summary>The class database. Valid only after <see cref="RequirePostgreSql"/>.</summary>
    public ClassDatabase Database => _database ?? throw new InvalidOperationException("The operator fixture has no database.");

    public string DatabaseName => Database.Name;

    internal PostgreSqlDataSources Sources { get; private set; } = null!;

    internal PostgreSqlUnitOfWorkFactory Factory { get; private set; } = null!;

    /// <summary>A factory for the same database through a closed local port, so every open fails with <c>persistence.unavailable</c>.</summary>
    internal PostgreSqlUnitOfWorkFactory UnreachableFactory { get; private set; } = null!;

    internal CapturingDiagnostics.Logger Logger { get; } = new();

    /// <summary>The release under test, in its forward order.</summary>
    internal static MigrationSet Migrations { get; } = new(
        [CoordinationMigrations.Module, DirectoryMigrations.Module, WorkMigrations.Module, AuditMigrations.Module, IdentityMigrations.Module]);

    public static UnitOfWorkScope Operator => UnitOfWorkScope.Operator;

    public async ValueTask InitializeAsync()
    {
        if (Postgres.UnavailableReason is not null)
        {
            return;
        }

        _database = await ScratchDatabase.CreateAsync(Postgres);
        Sources = new PostgreSqlDataSources(TestSettings.For(_database), TestCredentialProvider.For(Postgres));
        Factory = new PostgreSqlUnitOfWorkFactory(Sources, TimeProvider.System, Logger);
        _unreachableSources = new PostgreSqlDataSources(TestSettings.For(_database, new Dictionary<string, object> { ["port"] = ClosedPort() }), TestCredentialProvider.For(Postgres));
        UnreachableFactory = new PostgreSqlUnitOfWorkFactory(_unreachableSources, TimeProvider.System, Logger);
        await new PostgreSqlMigrationRunner(Sources, Migrations, TimeProvider.System, Logger).ApplyAsync(new MigrationApplyRequest(), CancellationToken.None);
    }

    private PostgreSqlDataSources? _unreachableSources;

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await Sources.DisposeAsync();
            await _unreachableSources!.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

    /// <summary>Skips or fails the calling test unless the container is running.</summary>
    public void RequirePostgreSql() => Postgres.RequirePostgreSql();

    /// <summary>A control-plane test host on this database (T017). Each has its own service provider.</summary>
    internal Task<OperatorHost> HostAsync(OperatorHostOptions? options = null) => OperatorHost.CreateAsync(this, options ?? new OperatorHostOptions());

    /// <summary>Marks the installation initialized, as the superuser, for tests past the gate.</summary>
    public Task InitializeInstallationAsync() =>
        ExecuteAsync("INSERT INTO isk_identity.installation (state, initialized_at, initialized_by) VALUES ('initialized', '2026-11-21T10:00:00.5Z', 'test') ON CONFLICT DO NOTHING");

    /// <summary>Adds a principal with the <c>system_operator</c> grant, or none, as the superuser, and returns its ID.</summary>
    public async Task<Iskender.Core.Contracts.Identifiers.ResourceId> PrincipalAsync(bool granted = true)
    {
        var principal = Iskender.Core.Contracts.Identifiers.ResourceId.New(Iskender.Core.Contracts.Identifiers.ResourceTypes.Principal);
        await ExecuteAsync($"INSERT INTO isk_identity.principals (principal_id, kind, created_at, created_through) VALUES ('{principal.Value}', 'operator', now(), 'bootstrap')");
        if (granted)
        {
            await ExecuteAsync($"INSERT INTO isk_identity.operator_grants (principal_id, role, granted_at) VALUES ('{principal.Value}', 'system_operator', now())");
        }

        return principal;
    }

    /// <summary>
    /// Moves token <paramref name="tokenId"/>'s <c>issued_at</c> and <c>expires_at</c> two days into the past, as the
    /// superuser, keeping its lifetime, so a redemption's <c>statement_timestamp()</c> falls after its expiry. Never a sleep.
    /// </summary>
    public Task ExpireAsync(string tokenId) =>
        ExecuteAsync($"UPDATE isk_identity.tokens SET issued_at = issued_at - interval '2 days', expires_at = expires_at - interval '2 days' WHERE token_id = '{tokenId}'");

    /// <summary>Token <paramref name="text"/> with its secret replaced by another valid one, keeping its purpose and ID.</summary>
    public static string WithOtherSecret(string text)
    {
        var other = new Domain.Identity.IssuedOperatorToken(Domain.Identity.OperatorTokenText.Generate(Domain.Identity.OperatorTokenPurpose.Bootstrap), DateTimeOffset.UtcNow).Reveal();
        return text[..SecretStart(text)] + other[SecretStart(other)..];
    }

    /// <summary>Token <paramref name="text"/> with its purpose letter replaced, so <c>isk&lt;p&gt;_</c> names another purpose.</summary>
    public static string Reprefix(string text, char purpose) => "isk" + purpose + text[4..];

    /// <summary>The secret of token <paramref name="text"/>, after its second underscore, since a secret may hold one.</summary>
    public static string Secret(string text) => text[SecretStart(text)..];

    private static int SecretStart(string text) => text.IndexOf('_', text.IndexOf('_', StringComparison.Ordinal) + 1) + 1;

    /// <summary>Token <paramref name="text"/> parsed, failing the test if it is malformed.</summary>
    public static Domain.Identity.OperatorTokenText Parse(string text)
    {
        Assert.True(Domain.Identity.OperatorTokenText.TryParse(text, out var token));
        return token;
    }

    /// <summary>Opens an operator-scope unit.</summary>
    public ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkOptions? options = null) =>
        Factory.OpenAsync(Operator, options ?? new UnitOfWorkOptions("identity", "test.op"), TestContext.Current.CancellationToken);

    /// <summary>Runs <paramref name="sql"/> as the superuser and returns the first column of the first row.</summary>
    public Task<T> ScalarAsync<T>(string sql) => Postgres.ScalarAsync<T>(DatabaseName, sql);

    /// <summary>Runs <paramref name="sql"/> as the superuser.</summary>
    public Task ExecuteAsync(string sql) => Postgres.ExecuteAsync(DatabaseName, sql);

    /// <summary>Empties every table of <c>isk_identity</c> and <c>isk_audit</c>, as the superuser, for a fresh run.</summary>
    public Task ResetAsync() => ExecuteAsync("TRUNCATE isk_identity.tokens, isk_identity.operator_grants, isk_identity.principals, isk_identity.installation, isk_audit.records");

    /// <summary>Every row of <paramref name="table"/>, a table of <c>isk_identity</c> or <c>isk_audit</c>, as JSON text in a stable order.</summary>
    public Task<string> RowsAsync(string table) =>
        ScalarAsync<string>($"SELECT coalesce(json_agg(t ORDER BY t::text)::text, '[]') FROM {table} t");

    /// <summary>Every row of every table of <c>isk_identity</c> and <c>isk_audit</c>, for an unchanged-state check.</summary>
    public async Task<string> AllRowsAsync() =>
        string.Join("\n", await Task.WhenAll(IdentityTableNames.Select(RowsAsync)));

    public static IReadOnlyList<string> IdentityTableNames { get; } =
        ["isk_identity.installation", "isk_identity.principals", "isk_identity.operator_grants", "isk_identity.tokens", "isk_audit.records"];

    /// <summary>The number of audit records, optionally only those matching <paramref name="where"/>.</summary>
    public Task<long> AuditCountAsync(string where = "true") => ScalarAsync<long>($"SELECT count(*) FROM isk_audit.records WHERE {where}");

    /// <summary>
    /// A validated configuration whose <c>operator</c> section holds <paramref name="operatorFields"/>, the fields as JSON
    /// text without braces, with its snapshot, settings, and the stores' lifetimes.
    /// </summary>
    internal static OperatorConfiguration Configuration(string operatorFields = "")
    {
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([OperatorSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("operator.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"operator\":{" + operatorFields + "}}}"),
            environmentName: "development"));
        Assert.True(report.IsValid, report.ToString());
        var settings = OperatorSettings.From(report.Snapshot!);
        return new(report.Snapshot!, settings, new IdentityLifetimes(settings.TokenLifetime, settings.EnrollmentCodeLifetime));
    }

    /// <summary>A request context for an audited operation: a new correlation ID, revision <c>test-rev</c>, and <paramref name="actor"/>.</summary>
    public static OperatorRequestContext Context(OperatorActor? actor = null) =>
        new(RequestCorrelationId.New(), new ConfigurationRevision("test-rev"), actor ?? OperatorActor.Anonymous);

    /// <summary>A local port nothing listens on.</summary>
    internal static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>A validated operator configuration and what it reads as.</summary>
internal sealed record OperatorConfiguration(ConfigurationSnapshot Snapshot, OperatorSettings Settings, IdentityLifetimes Lifetimes);
