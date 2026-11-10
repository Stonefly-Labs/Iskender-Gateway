using System.Reflection;
using System.Security.Cryptography;
using DotNet.Testcontainers.Configurations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgreSqlFixture))]

namespace Iskender.Infrastructure.PostgreSql.Tests.Fixture;

/// <summary>
/// One PostgreSQL 17 container per test run (FR-033, research R19), from the official image pinned by the digest of its
/// multi-architecture index, read from the registry during implement (T002). It creates the roles in
/// <c>init-roles.sql</c> and the template databases, and hands out one database per test class. Without a container
/// runtime it records why, and <see cref="RequirePostgreSql"/> skips or fails each container-backed test. The only network
/// access is pulling this image once: the resource reaper, which would pull a second image, is turned off, and the
/// fixture removes the container itself when the run ends.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    /// <summary>The image, by tag and index digest, as <c>docker buildx imagetools inspect</c> printed it on 2026-11-10.</summary>
    public const string Image = "postgres:17-bookworm@sha256:665dc7b37ac52a17e298511414c21d23ffa4e6a6ef6225b3e5d59c7afb35e994";

    public const string RuntimeGroup = "iskender_runtime";
    public const string RuntimeLogin = "isk_runtime_login";
    public const string MigratorLogin = "isk_migrator";
    public const string EmptyTemplate = "isk_template_empty";

    private readonly SemaphoreSlim _createLock = new(1, 1);
    private PostgreSqlContainer? _container;
    private int _databaseCount;

    public string Host { get; private set; } = "";

    public int Port { get; private set; }

    /// <summary>Why the container could not start, or <c>null</c> when it is running.</summary>
    public string? UnavailableReason { get; private set; }

    public string RuntimePassword { get; } = NewSecret();

    public string MigratorPassword { get; } = NewSecret();

    internal string SuperuserName { get; } = "isk_super_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    internal string SuperuserPassword { get; } = NewSecret();

    /// <summary>
    /// Set to <c>off</c> to run as if no container runtime were present, so the skip-or-fail path can be checked on a
    /// machine that has one. With <c>CI=true</c> the container-backed tests then fail, as they must.
    /// </summary>
    public const string SwitchVariable = "ISKENDER_TEST_POSTGRESQL";

    public async ValueTask InitializeAsync()
    {
        if (string.Equals(Environment.GetEnvironmentVariable(SwitchVariable), "off", StringComparison.OrdinalIgnoreCase))
        {
            UnavailableReason = $"{SwitchVariable}=off";
            return;
        }

        try
        {
            TestcontainersSettings.ResourceReaperEnabled = false;
            _container = new PostgreSqlBuilder(Image)
                .WithUsername(SuperuserName)
                .WithPassword(SuperuserPassword)
                .WithDatabase("postgres")
                .Build();
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
    public void RequirePostgreSql() => Fixture.RequirePostgreSql.That(UnavailableReason);

    /// <summary>Creates <c>isk_t_&lt;n&gt;</c> from <paramref name="template"/>, with <c>CREATE</c> granted to the migration login.</summary>
    internal async Task<string> CreateDatabaseAsync(string template)
    {
        var name = $"isk_t_{Interlocked.Increment(ref _databaseCount)}";
        await _createLock.WaitAsync();
        try
        {
            await ExecuteAsync("postgres", $"CREATE DATABASE {name} TEMPLATE {template}");
        }
        finally
        {
            _createLock.Release();
        }

        await ExecuteAsync("postgres", $"GRANT CREATE ON DATABASE {name} TO {MigratorLogin}");
        return name;
    }

    internal Task DropDatabaseAsync(string name) => ExecuteAsync("postgres", $"DROP DATABASE IF EXISTS {name} WITH (FORCE)");

    /// <summary>Runs <paramref name="sql"/> as the superuser on an unpooled connection, so templates have no lingering session.</summary>
    internal async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString(database, SuperuserName, SuperuserPassword));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>An unpooled connection string with TLS off, for test-side setup and checks.</summary>
    internal string ConnectionString(string database, string username, string password) => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Database = database,
        Username = username,
        Password = password,
        SslMode = SslMode.Disable,
        Pooling = false,
    }.ConnectionString;

    internal static string ReadResource(string name)
    {
        var assembly = typeof(PostgreSqlFixture).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(candidate => candidate.EndsWith("." + name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string NewSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
