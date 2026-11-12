using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Iskender.Infrastructure.Redis;

/// <summary>
/// The adapter's one <see cref="ConnectionMultiplexer"/> (ISK-0005 research R11, contract Credentials rule 1), built from
/// <see cref="ConfigurationOptions"/> set field by field from <see cref="CoordinationSettings"/>, never from a connection
/// string. The password comes from <see cref="ICoordinationCredentialProvider"/> with <c>redis.password</c>'s secret
/// reference when the multiplexer is built; after an authentication failure the multiplexer is disposed, and the next call
/// builds a new one with a freshly resolved credential, at most once per <c>redis.connectTimeout</c>. Commands outside the
/// contract's ACL are left out of the command map, and the client's own logging is never connected. Every operation runs a
/// <see cref="RedisScript"/> through <see cref="RunAsync"/>, bounded by <c>operationTimeout</c> and the scope's deadline,
/// classified by <see cref="RedisFailureClassifier"/>, and never retried. Owned by ISK-0005.
/// </summary>
internal sealed class RedisConnection : IAsyncDisposable
{
    /// <summary>Commands the client would otherwise send that the contract's ACL does not grant.</summary>
    internal static readonly HashSet<string> ExcludedCommands = new(StringComparer.Ordinal)
    {
        "INFO", "CONFIG", "CLUSTER", "ECHO", "SUBSCRIBE", "UNSUBSCRIBE", "PSUBSCRIBE", "PUNSUBSCRIBE", "SSUBSCRIBE", "SUNSUBSCRIBE",
        "PUBLISH", "CLIENT", "SENTINEL", "QUIT", "READONLY", "READWRITE", "SELECT",
    };

    private readonly CoordinationSettings _settings;
    private readonly ICoordinationCredentialProvider? _credentials;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _build = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _loaded = new(StringComparer.Ordinal);
    private ConnectionMultiplexer? _multiplexer;
    private long? _lastBuild;
    private bool _authenticationFailed;
    private int _lastAttemptFailure;
    private TaskCompletionSource? _firstAttempt;

    public RedisConnection(CoordinationSettings settings, ICoordinationCredentialProvider? credentials, ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        _settings = settings ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _credentials = credentials;
        _logger = logger ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>How many multiplexers this connection has built, so a test can prove a rebuild.</summary>
    internal int Builds { get; private set; }

    internal CoordinationSettings Settings => _settings;

    internal ILogger Logger => _logger;

    /// <summary>
    /// Runs <paramref name="script"/> on <paramref name="keys"/> with <paramref name="arguments"/> in <paramref name="scope"/>,
    /// bounded by <c>operationTimeout</c> and the scope's deadline, and records its activity and metrics. Any failure becomes
    /// a contract failure; a failure after a write was sent carries <c>outcome = unknown</c>.
    /// </summary>
    public async ValueTask<RedisResult> RunAsync(
        CoordinationScope scope,
        string module,
        CoordinationPrimitive primitive,
        RedisScript script,
        RedisKey[] keys,
        RedisValue[] arguments,
        CancellationToken cancellationToken)
    {
        if (scope is null)
        {
            throw new GatewayException(ErrorCodes.CoordinationScopeRequired);
        }

        ArgumentNullException.ThrowIfNull(script);
        var started = _timeProvider.GetTimestamp();
        var activity = CoordinationTelemetry.StartOperation(module, primitive, script.Name, CoordinatorKind.Redis);
        using var bound = new CancellationTokenSource(Bound(scope), _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, scope.RequestCancellation, bound.Token);
        try
        {
            if (cancellationToken.IsCancellationRequested || scope.RequestCancellation.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            var database = await DatabaseAsync(linked.Token);
            var result = await EvaluateAsync(database, script, keys, arguments).WaitAsync(linked.Token);
            CoordinationTelemetry.End(activity, "succeeded");
            CoordinationTelemetry.RecordOperation(module, primitive, script.Name, CoordinatorKind.Redis, "succeeded", _timeProvider.GetElapsedTime(started));
            return result;
        }
        catch (Exception caught) when (caught is not GatewayException)
        {
            var exception = await ExplainAsync(caught);
            var stop = cancellationToken.IsCancellationRequested || scope.RequestCancellation.IsCancellationRequested ? RedisStop.Cancelled
                : bound.IsCancellationRequested ? RedisStop.TimedOut
                : RedisStop.None;
            var failure = RedisFailureClassifier.Classify(exception, stop, module, primitive, script, _logger);
            var failureClass = CoordinationFailures.ClassOf(failure);
            CoordinationTelemetry.End(activity, "failed", failureClass);
            CoordinationTelemetry.RecordOperation(module, primitive, script.Name, CoordinatorKind.Redis, "failed", _timeProvider.GetElapsedTime(started), failureClass);
            throw failure;
        }
    }

    /// <summary>The database, building the multiplexer first if there is none. A missing or failing provider fails with <see cref="RedisCredentialUnavailableException"/>.</summary>
    internal async ValueTask<IDatabase> DatabaseAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _multiplexer) is { } existing)
        {
            return existing.GetDatabase();
        }

        await _build.WaitAsync(cancellationToken);
        try
        {
            if (_multiplexer is { } built)
            {
                return built.GetDatabase();
            }

            if (_authenticationFailed && _lastBuild is { } last && _timeProvider.GetElapsedTime(last) < _settings.RedisConnectTimeout)
            {
                throw new RedisCredentialUnavailableException();
            }

            if (!_settings.HasRedis)
            {
                throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "No Redis endpoint is configured.", null, CommandStatus.Unknown);
            }

            var password = await PasswordAsync(cancellationToken);
            _lastBuild = _timeProvider.GetTimestamp();
            _authenticationFailed = false;
            _loaded.Clear();
            Volatile.Write(ref _lastAttemptFailure, (int)ConnectionFailureType.None);
            var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _firstAttempt, firstAttempt);
            var options = Options(_settings, password);
            options.ConnectionAttemptCompleted += OnConnectionAttemptCompleted;
            var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
            Builds++;
            if (!multiplexer.IsConnected)
            {
                // The attempt's outcome can arrive after ConnectAsync returns. Waiting for it, within connectTimeout, lets the
                // first command's failure say whether a credential was refused.
                try
                {
                    await firstAttempt.Task.WaitAsync(_settings.RedisConnectTimeout, _timeProvider, cancellationToken);
                }
                catch (TimeoutException)
                {
                    // No outcome within connectTimeout: the command's own failure is classified as it is.
                }
                catch (OperationCanceledException)
                {
                    await multiplexer.DisposeAsync();
                    throw;
                }
            }

            Volatile.Write(ref _multiplexer, multiplexer);
            return multiplexer.GetDatabase();
        }
        finally
        {
            _build.Release();
        }
    }

    // Subscribed before connecting, so the first attempt is seen too. Only the failure type is kept.
    private void OnConnectionAttemptCompleted(object? sender, ConnectionAttemptCompletedEventArgs attempt)
    {
        Volatile.Write(ref _lastAttemptFailure, (int)(attempt.IsSuccess ? ConnectionFailureType.None : attempt.FailureType));
        Volatile.Read(ref _firstAttempt)?.TrySetResult();
    }

    /// <summary>
    /// The failure to classify for <paramref name="exception"/>. With a fail-fast backlog, a command on a multiplexer that
    /// is not connected reports only that no connection is available, before anything is sent; the last connection
    /// attempt says whether a credential was refused. After an authentication failure the multiplexer is dropped, so the
    /// next call resolves the credential again.
    /// </summary>
    internal async ValueTask<Exception> ExplainAsync(Exception exception)
    {
        var explained = exception is RedisConnectionException { FailureType: ConnectionFailureType.UnableToResolvePhysicalConnection }
            && (ConnectionFailureType)Volatile.Read(ref _lastAttemptFailure) == ConnectionFailureType.AuthenticationFailure
            ? new RedisConnectionException(ConnectionFailureType.AuthenticationFailure, CommandFlags.None, "The last connection attempt was refused.", null, CommandStatus.WaitingToBeSent)
            : exception;
        if (RedisFailureClassifier.IsAuthenticationFailure(explained))
        {
            await ResetAfterAuthenticationFailureAsync();
        }

        return explained;
    }

    /// <summary>The live multiplexer, or <c>null</c>, for the health probe's server commands.</summary>
    internal ConnectionMultiplexer? Multiplexer => Volatile.Read(ref _multiplexer);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _multiplexer, null) is { } multiplexer)
        {
            await multiplexer.DisposeAsync();
        }

        _build.Dispose();
    }

    /// <summary>
    /// The client options, set field by field: endpoints parsed from <c>host:port</c>, database 0, ACL user, the resolved
    /// password, TLS with certificate chain and per-endpoint host name validation for <c>require</c>, <c>connectTimeout</c>, no abort on
    /// a failed first connect, a fail-fast backlog so that a command never waits for a reconnect (FR-022), and
    /// <c>operationTimeout</c> as the client's own timeouts.
    /// </summary>
    internal static ConfigurationOptions Options(CoordinationSettings settings, string? password)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var options = new ConfigurationOptions
        {
            User = settings.RedisUsername,
            Password = password,
            AbortOnConnectFail = false,
            BacklogPolicy = BacklogPolicy.FailFast,
            ConnectTimeout = Milliseconds(settings.RedisConnectTimeout),
            SyncTimeout = Milliseconds(settings.OperationTimeout),
            AsyncTimeout = Milliseconds(settings.OperationTimeout),
            ConnectRetry = 1,
            Protocol = RedisProtocol.Resp3,
            CommandMap = CommandMap.Create(ExcludedCommands, available: false),
            ConfigurationChannel = "",
            TieBreaker = "",
            SetClientLibrary = false,
            AllowAdmin = false,
            Ssl = settings.RedisTlsMode == RedisTlsMode.Require,
        };

        foreach (var endpoint in settings.RedisEndpoints)
        {
            var (host, port) = ParseEndpoint(endpoint);
            if (options.Ssl)
            {
                // With SslHost unset the client checks a DnsEndPoint's certificate against its own host, but an address
                // endpoint against a host name inferred from the others; as a DnsEndPoint, an address is checked against itself.
                options.EndPoints.Add(new DnsEndPoint(host, port));
            }
            else
            {
                options.EndPoints.Add(host, port);
            }
        }

        if (options.Ssl)
        {
            options.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            options.CheckCertificateRevocation = true;
            options.CertificateValidation += ValidateServerCertificate;
        }

        return options;
    }

    /// <summary>The certificate check for <c>tlsMode: require</c>: the chain must build against the system store and the host name must match.</summary>
    internal static bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors) =>
        certificate is not null && errors == SslPolicyErrors.None;

    /// <summary>
    /// Parses <c>host:port</c>, or <c>[address]:port</c> for IPv6. A missing or out-of-range port fails with
    /// <c>core.value.invalid</c>, naming nothing.
    /// </summary>
    internal static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        var separator = endpoint?.LastIndexOf(':') ?? -1;
        if (separator <= 0 || !int.TryParse(endpoint.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var host = endpoint![..separator];
        if (host.StartsWith('[') && host.EndsWith(']'))
        {
            host = host[1..^1];
        }

        return host.Length == 0 || host.Contains('[', StringComparison.Ordinal) || host.Contains(']', StringComparison.Ordinal) ? throw new GatewayException(ErrorCodes.ValueInvalid) : (host, port);
    }

    // EVALSHA with the hash one SCRIPT LOAD per multiplexer returned, and EVAL when the server has lost the script
    // (NOSCRIPT), which also drops the hash so the next call loads it again.
    private async Task<RedisResult> EvaluateAsync(IDatabase database, RedisScript script, RedisKey[] keys, RedisValue[] arguments)
    {
        if (!_loaded.TryGetValue(script.Name, out var hash))
        {
            hash = (string?)await database.ExecuteAsync("SCRIPT", "LOAD", script.Text);
            if (hash is not { Length: 40 })
            {
                throw new InvalidOperationException("The server returned an unexpected script hash.");
            }

            _loaded[script.Name] = hash;
        }

        try
        {
            return await database.ExecuteAsync("EVALSHA", Arguments(hash, keys, arguments));
        }
        catch (RedisServerException exception) when (exception.Message.StartsWith("NOSCRIPT", StringComparison.Ordinal))
        {
            _loaded.TryRemove(script.Name, out _);
            return await database.ExecuteAsync("EVAL", Arguments(script.Text, keys, arguments));
        }
    }

    /// <summary>The hash loaded for <paramref name="script"/>, or <c>null</c>, so a test can prove the fallback.</summary>
    internal string? LoadedHash(RedisScript script) => _loaded.TryGetValue(script.Name, out var hash) ? hash : null;

    private static object[] Arguments(string script, RedisKey[] keys, RedisValue[] arguments)
    {
        var all = new object[2 + keys.Length + arguments.Length];
        all[0] = script;
        all[1] = keys.Length;
        keys.CopyTo(all, 2);
        for (var index = 0; index < arguments.Length; index++)
        {
            all[2 + keys.Length + index] = arguments[index];
        }

        return all;
    }

    private async ValueTask<string?> PasswordAsync(CancellationToken cancellationToken)
    {
        if (_settings.RedisPassword is not { } reference)
        {
            return null;
        }

        if (_credentials is null)
        {
            throw new RedisCredentialUnavailableException();
        }

        try
        {
            return (await _credentials.GetCredentialAsync(new CoordinationCredentialRequest(reference), cancellationToken)).Password;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // The provider's own message never leaves this method.
            throw new RedisCredentialUnavailableException();
        }
    }

    private async ValueTask ResetAfterAuthenticationFailureAsync()
    {
        await _build.WaitAsync();
        try
        {
            _authenticationFailed = true;
            if (Interlocked.Exchange(ref _multiplexer, null) is { } multiplexer)
            {
                await multiplexer.DisposeAsync();
            }
        }
        finally
        {
            _build.Release();
        }
    }

    // The earlier of operationTimeout and the scope's deadline; a passed deadline gives a bound of zero.
    private TimeSpan Bound(CoordinationScope scope)
    {
        var bound = _settings.OperationTimeout;
        if (scope.Deadline is { } deadline)
        {
            var remaining = deadline - _timeProvider.GetUtcNow();
            bound = remaining < bound ? remaining : bound;
        }

        return bound > TimeSpan.Zero ? bound : TimeSpan.FromTicks(1);
    }

    private static int Milliseconds(TimeSpan value) => (int)Math.Min(int.MaxValue, Math.Ceiling(value.TotalMilliseconds));
}
