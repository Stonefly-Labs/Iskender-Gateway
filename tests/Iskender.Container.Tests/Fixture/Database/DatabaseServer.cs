using System.Security.Cryptography;
using DotNet.Testcontainers.Configurations;
using Iskender.Container.Tests.Fixture.Database;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(DatabaseServer))]

namespace Iskender.Container.Tests.Fixture.Database;

/// <summary>
/// One PostgreSQL 17 container per test run, at the digest ISK-0004's fixture pins, with ISK-0004's <c>init-roles.sql</c>
/// roles and an empty template, built on the adapter's public surface only, so <c>Iskender.Infrastructure.PostgreSql</c>
/// grants its internals to its own test project alone (maintainer decision, 2026-11-15). Without a container runtime it
/// records why, and <see cref="RequirePostgreSql"/> skips each container-backed test, or fails it when <c>CI</c> is
/// <c>true</c>.
/// </summary>
public sealed class DatabaseServer : IAsyncLifetime
{
    /// <summary>ISK-0004's image, as <c>PostgreSqlFixture.Image</c> and the Compose topology pin it.</summary>
    public const string Image = "postgres:17-bookworm@sha256:665dc7b37ac52a17e298511414c21d23ffa4e6a6ef6225b3e5d59c7afb35e994";

    /// <summary>The runtime login <c>init-roles.sql</c> creates, a member of <c>iskender_runtime</c>.</summary>
    public const string RuntimeLogin = "isk_runtime_login";

    /// <summary>The migration login <c>init-roles.sql</c> creates.</summary>
    public const string MigratorLogin = "isk_migrator";

    private const string EmptyTemplate = "isk_template_empty";

    private readonly SemaphoreSlim _createLock = new(1, 1);

    private readonly string _superuserName = "isk_super_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    private readonly string _superuserPassword = NewSecret();

    private PostgreSqlContainer? _container;

    private int _databaseCount;

    public string Host { get; private set; } = "";

    public int Port { get; private set; }

    public string RuntimePassword { get; } = NewSecret();

    public string MigratorPassword { get; } = NewSecret();

    /// <summary>Why the container could not start, or <c>null</c> when it is running.</summary>
    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            TestcontainersSettings.ResourceReaperEnabled = false;
            _container = new PostgreSqlBuilder(Image).WithUsername(_superuserName).WithPassword(_superuserPassword).WithDatabase("postgres").Build();
            await _container.StartAsync();
            Host = _container.Hostname;
            Port = _container.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort);
            await ExecuteAsync("postgres", ReadResource("init-roles.sql"));
            await ExecuteAsync("postgres", $"ALTER ROLE {RuntimeLogin} PASSWORD '{RuntimePassword}'; ALTER ROLE {MigratorLogin} PASSWORD '{MigratorPassword}';");
            await ExecuteAsync("postgres", $"CREATE DATABASE {EmptyTemplate}");
            await ExecuteAsync(EmptyTemplate, "REVOKE CREATE ON SCHEMA public FROM PUBLIC");
        }
        catch (Exception exception)
        {
            UnavailableReason = $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        _createLock.Dispose();
    }

    /// <summary>Skips or fails the calling test unless the container is running.</summary>
    public void RequirePostgreSql() => Iskender.Infrastructure.PostgreSql.Tests.Fixture.RequirePostgreSql.That(UnavailableReason);

    /// <summary>A credential provider that answers the runtime and migration secret references with this server's passwords.</summary>
    public DatabaseCredentials Credentials() => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [DatabaseDocuments.RuntimeSecret.Name] = RuntimePassword,
        [DatabaseDocuments.MigrationSecret.Name] = MigratorPassword,
    });

    /// <summary>Creates <c>isk_c_&lt;n&gt;</c> from the empty template, with <c>CREATE</c> granted to the migration login.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"isk_c_{Interlocked.Increment(ref _databaseCount)}";
        await _createLock.WaitAsync();
        try
        {
            await ExecuteAsync("postgres", $"CREATE DATABASE {name} TEMPLATE {EmptyTemplate}");
        }
        finally
        {
            _createLock.Release();
        }

        await ExecuteAsync("postgres", $"GRANT CREATE ON DATABASE {name} TO {MigratorLogin}");
        return name;
    }

    public Task DropDatabaseAsync(string name) => ExecuteAsync("postgres", $"DROP DATABASE IF EXISTS {name} WITH (FORCE)");

    /// <summary>Runs <paramref name="sql"/> as the superuser and returns the first column of the first row.</summary>
    public async Task<T> ScalarAsync<T>(string database, string sql)
    {
        await using var connection = await OpenSuperuserAsync(database);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>An open, unpooled superuser connection, for a test that must hold a session, such as a lock.</summary>
    public async Task<NpgsqlConnection> OpenSuperuserAsync(string database)
    {
        var connection = new NpgsqlConnection(ConnectionString(database, _superuserName, _superuserPassword));
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>An unpooled connection string with TLS off, for test-side setup and checks.</summary>
    public string ConnectionString(string database, string username, string password) => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Database = database,
        Username = username,
        Password = password,
        SslMode = SslMode.Disable,
        Pooling = false,
    }.ConnectionString;

    private async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = await OpenSuperuserAsync(database);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string ReadResource(string name)
    {
        var assembly = typeof(DatabaseServer).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(candidate => candidate.EndsWith("." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string NewSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
