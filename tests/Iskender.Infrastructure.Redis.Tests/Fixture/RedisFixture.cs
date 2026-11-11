using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DotNet.Testcontainers.Configurations;
using Iskender.Infrastructure.Redis.Tests.Fixture;
using StackExchange.Redis;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(RedisFixture))]

namespace Iskender.Infrastructure.Redis.Tests.Fixture;

/// <summary>
/// One Valkey 8 container per test run (FR-031, contract Test fixture rule 2, research R2 and R19), from the official
/// image pinned by the digest of its multi-architecture index, read from the registry in T002. The <c>default</c> user is
/// disabled. The adapter's ACL user, with a name and password generated per run, gets exactly the key pattern and commands
/// in the contract's Configuration section. A second, fixture-only administrative user is used by tests for inspection and
/// fault injection (<c>SCAN</c>, <c>KEYS</c>, <c>INFO</c>, <c>CLIENT KILL</c>, <c>REPLICAOF</c>, <c>SCRIPT FLUSH</c>), and
/// the adapter never receives it. The host port is fixed when the container is created, so a stop and start keeps the
/// endpoint. Without a container runtime it records why, and <see cref="RequireRedis"/> skips or fails each
/// container-backed test. The resource reaper is turned off, and the fixture removes the container when the run ends.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    /// <summary>The image, by tag and index digest, recorded in T002 on 2026-11-11.</summary>
    public const string Image = "valkey/valkey:8.1.11@sha256:8f836008e0986c36adf50da714c7060cf689dbb335f61fe077939d90fa650809";

    /// <summary>The server port inside the container.</summary>
    public const int ServerPort = 6379;

    /// <summary>
    /// Set to <c>off</c> to run as if no container runtime were present, so the skip-or-fail path can be checked on a
    /// machine that has one. With <c>CI=true</c> the container-backed tests then fail, as they must.
    /// </summary>
    public const string SwitchVariable = "ISKENDER_TEST_REDIS";

    /// <summary>The adapter user's key pattern and commands, exactly as the contract's Configuration section lists them.</summary>
    public static readonly IReadOnlyList<string> AdapterRules =
    [
        "~isk:v1:*", "resetchannels", "-@all",
        "+evalsha", "+eval", "+script|load", "+ping", "+role", "+hello",
        "+get", "+set", "+del", "+incr", "+pexpire", "+pttl", "+hget", "+hset", "+hmget", "+time",
    ];

    private RedisContainer? _container;
    private ConnectionMultiplexer? _admin;

    public string Host { get; private set; } = "";

    public int Port { get; private set; }

    /// <summary>The <c>host:port</c> endpoint the adapter is configured with.</summary>
    public string Endpoint => $"{Host}:{Port}";

    /// <summary>Why the container could not start, or <c>null</c> when it is running.</summary>
    public string? UnavailableReason { get; private set; }

    /// <summary>The adapter's ACL user name, generated per run.</summary>
    public string Username { get; } = "isk_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    /// <summary>The adapter's ACL password, generated per run.</summary>
    public string Password { get; } = NewSecret();

    internal string AdminUsername { get; } = "isk_admin_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    internal string AdminPassword { get; } = NewSecret();

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
            Port = FreePort();
            _container = new RedisBuilder(Image)
                .WithPortBinding(Port, ServerPort)
                .WithCommand(
                    ["valkey-server",
                    "--user", "default", "off",
                    "--user", Username, "on", ">" + Password, .. AdapterRules,
                    "--user", AdminUsername, "on", ">" + AdminPassword, "~*", "&*", "+@all"])
                .Build();
            await _container.StartAsync();
            Host = _container.Hostname;
            await WaitUntilServingAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            UnavailableReason = $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_admin is not null)
        {
            await _admin.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Skips or fails the calling test unless the container is running.</summary>
    public void RequireRedis() => Fixture.RequireRedis.That(UnavailableReason);

    /// <summary>The administrative connection, for inspection and fault injection only. The adapter never receives it.</summary>
    public async Task<IServer> AdminAsync()
    {
        _admin ??= await ConnectionMultiplexer.ConnectAsync(AdminOptions());
        return _admin.GetServer(Host, Port);
    }

    /// <summary>The administrative connection's database 0.</summary>
    public async Task<IDatabase> AdminDatabaseAsync(int database = 0)
    {
        await AdminAsync();
        return _admin!.GetDatabase(database);
    }

    /// <summary>Stops the container, as an outage; the endpoint stays the same.</summary>
    public async Task StopAsync()
    {
        await DropAdminAsync();
        await _container!.StopAsync();
    }

    /// <summary>Starts the stopped container again and waits until it serves.</summary>
    public async Task StartAsync()
    {
        await _container!.StartAsync();
        await WaitUntilServingAsync(CancellationToken.None);
    }

    /// <summary>Pauses the container, so connections stay open and every command times out.</summary>
    public Task PauseAsync() => _container!.PauseAsync();

    /// <summary>Resumes a paused container.</summary>
    public Task UnpauseAsync() => _container!.UnpauseAsync();

    /// <summary>Makes the server a replica of an address that never answers, so it reports the replica role and refuses writes with <c>READONLY</c>.</summary>
    public async Task BecomeReplicaAsync()
    {
        var admin = await AdminAsync();
        await admin.ExecuteAsync("REPLICAOF", "127.0.0.1", "1");
    }

    /// <summary>Makes the server a primary again.</summary>
    public async Task BecomePrimaryAsync()
    {
        var admin = await AdminAsync();
        await admin.ExecuteAsync("REPLICAOF", "NO", "ONE");
    }

    /// <summary>The fixture's own options for a user, built field by field. These are the test's, not the adapter's.</summary>
    internal ConfigurationOptions OptionsFor(string user, string password, bool allowAdmin = false)
    {
        var options = new ConfigurationOptions
        {
            User = user,
            Password = password,
            AllowAdmin = allowAdmin,
            AbortOnConnectFail = false,
            ConnectTimeout = 5000,
            SyncTimeout = 5000,
            AsyncTimeout = 5000,
            ConnectRetry = 1,
        };
        options.EndPoints.Add(Host, Port);
        return options;
    }

    private ConfigurationOptions AdminOptions() => OptionsFor(AdminUsername, AdminPassword, allowAdmin: true);

    private async Task DropAdminAsync()
    {
        if (_admin is not null)
        {
            await _admin.DisposeAsync();
            _admin = null;
        }
    }

    private async Task WaitUntilServingAsync(CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                await DropAdminAsync();
                var admin = await AdminAsync();
                await admin.PingAsync();
                return;
            }
            catch (RedisException) when (DateTime.UtcNow < until)
            {
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    // Lowercase letters and digits only, so it needs no quoting on the server's command line.
    private static string NewSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
}
