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

    /// <summary>Runs inside the send gate just before a script command is handed to the client, so a test can abandon a call there.</summary>
    internal Action? BeforeSend { get; set; }

    internal CoordinationSettings Settings => _settings;

    internal ILogger Logger => _logger;

    /// <summary>
    /// Runs <paramref name="script"/> on <paramref name="keys"/> with <paramref name="arguments"/> in <paramref name="scope"/>,
    /// bounded by <c>operationTimeout</c> and the scope's deadline, and records its activity and metrics. Any failure becomes
    /// a contract failure; a failure after a write's command was handed to the client carries <c>outcome = unknown</c>.
    /// A <paramref name="detached"/> run, for cleanup after the request stopped, ignores the scope's deadline and request
    /// token and is bounded by <c>operationTimeout</c> alone.
    /// </summary>
    public ValueTask<RedisResult> RunAsync(
        CoordinationScope scope,
        string module,
        CoordinationPrimitive primitive,
        RedisScript script,
        RedisKey[] keys,
        RedisValue[] arguments,
        CancellationToken cancellationToken,
        bool detached = false) =>
        RunAsync(scope, module, primitive, script, keys, arguments, static reply => reply, cancellationToken, detached);

    /// <summary>
    /// Runs <paramref name="script"/> as <see cref="RunAsync(CoordinationScope, string, CoordinationPrimitive, RedisScript, RedisKey[], RedisValue[], CancellationToken, bool)"/>
    /// does, and reads its reply with <paramref name="parse"/> inside the same classification, so a reply of an unexpected
    /// shape fails with <c>core.internal</c>, class <c>Unexpected</c>, and event 5010.
    /// </summary>
    public async ValueTask<T> RunAsync<T>(
        CoordinationScope scope,
        string module,
        CoordinationPrimitive primitive,
        RedisScript script,
        RedisKey[] keys,
        RedisValue[] arguments,
        Func<RedisResult, T> parse,
        CancellationToken cancellationToken,
        bool detached = false)
    {
        if (scope is null)
        {
            throw CoordinationFailures.Raise(ErrorCodes.CoordinationScopeRequired, CoordinationFailureClass.ScopeViolation);
        }

        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(parse);
        var started = _timeProvider.GetTimestamp();
        var activity = CoordinationTelemetry.StartOperation(module, primitive, script.Name, CoordinatorKind.Redis);
        var requestCancellation = detached ? CancellationToken.None : scope.RequestCancellation;
        using var bound = new CancellationTokenSource(detached ? _settings.OperationTimeout : Bound(scope), _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestCancellation, bound.Token);
        var gate = new SendGate(BeforeSend);
        IDatabase? database = null;
        try
        {
            if (cancellationToken.IsCancellationRequested || requestCancellation.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            database = await DatabaseAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            var result = parse(await EvaluateAsync(database, script, keys, arguments, gate, linked.Token).WaitAsync(linked.Token));
            CoordinationTelemetry.End(activity, "succeeded");
            CoordinationTelemetry.RecordOperation(module, primitive, script.Name, CoordinatorKind.Redis, "succeeded", _timeProvider.GetElapsedTime(started));
            return result;
        }
        catch (Exception caught) when (caught is not GatewayException)
        {
            // Abandoned first, so the evaluation still running sends nothing more once the outcome below is decided.
            var commandSent = gate.Abandon();
            var exception = await ExplainAsync(caught, database?.Multiplexer, linked.Token);
            var stop = cancellationToken.IsCancellationRequested || requestCancellation.IsCancellationRequested ? RedisStop.Cancelled
                : bound.IsCancellationRequested ? RedisStop.TimedOut
                : RedisStop.None;
            var failure = RedisFailureClassifier.Classify(exception, stop, commandSent, module, primitive, script, _logger);
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
            var connecting = ConnectionMultiplexer.ConnectAsync(options);
            ConnectionMultiplexer multiplexer;
            try
            {
                // Bounded by the operation's own bound, not only connectTimeout, which can be far longer.
                multiplexer = await connecting.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The abandoned build is disposed when it completes; the next call builds again.
                _ = connecting.ContinueWith(built => built.Result.Dispose(), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
                throw;
            }

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
    /// attempt says whether a credential was refused. After an authentication failure <paramref name="failing"/>, the
    /// multiplexer the failed command used, is dropped if it is still the current one, so the next call resolves the
    /// credential again; the reset waits for a concurrent build no longer than <paramref name="cancellationToken"/>, the
    /// call's own bound.
    /// </summary>
    internal async ValueTask<Exception> ExplainAsync(Exception exception, IConnectionMultiplexer? failing, CancellationToken cancellationToken)
    {
        var explained = exception is RedisConnectionException { FailureType: ConnectionFailureType.UnableToResolvePhysicalConnection }
            && (ConnectionFailureType)Volatile.Read(ref _lastAttemptFailure) == ConnectionFailureType.AuthenticationFailure
            ? new RedisConnectionException(ConnectionFailureType.AuthenticationFailure, CommandFlags.None, "The last connection attempt was refused.", null, CommandStatus.WaitingToBeSent)
            : exception;
        if (RedisFailureClassifier.IsAuthenticationFailure(explained))
        {
            await ResetAfterAuthenticationFailureAsync(failing, cancellationToken);
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
    internal static (string Host, int Port) ParseEndpoint(string endpoint) =>
        CoordinationSettings.TryParseRedisEndpoint(endpoint, out var host, out var port) ? (host, port) : throw new GatewayException(ErrorCodes.ValueInvalid);

    // EVALSHA with the hash one SCRIPT LOAD per multiplexer returned, and EVAL when the server has lost the script
    // (NOSCRIPT), which also drops the hash so the next call loads it again. Each script command passes the gate as it is
    // handed to the client, so a failure before it is never an unknown outcome, and a call abandoned while SCRIPT LOAD or
    // EVALSHA was in flight sends nothing after it: no write is made once the caller was told the outcome. The command is
    // handed to the client inside the gate, and the client queues commands in call order, so a command counted as sent is
    // always queued ahead of anything the caller sends after abandoning the call, such as a lease acquire's undo.
    private async Task<RedisResult> EvaluateAsync(IDatabase database, RedisScript script, RedisKey[] keys, RedisValue[] arguments, SendGate gate, CancellationToken cancellationToken)
    {
        if (script.Inline)
        {
            return await gate.Send(() => database.ExecuteAsync("EVAL", Arguments(script.Text, keys, arguments)), cancellationToken);
        }

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
            return await gate.Send(() => database.ExecuteAsync("EVALSHA", Arguments(hash, keys, arguments)), cancellationToken);
        }
        catch (RedisServerException exception) when (exception.Message.StartsWith("NOSCRIPT", StringComparison.Ordinal))
        {
            _loaded.TryRemove(script.Name, out _);
            return await gate.Send(() => database.ExecuteAsync("EVAL", Arguments(script.Text, keys, arguments)), cancellationToken);
        }
    }

    /// <summary>
    /// Whether a call's script command was handed to the client, decided once against the call being abandoned: a command
    /// passes only while the call is live, and abandoning it reports whether one had passed. Either the command is sent and
    /// counted, or it is never sent, and <see cref="Abandon"/> returns only once a command that passed is with the client.
    /// </summary>
    private sealed class SendGate(Action? beforeSend)
    {
        private readonly Lock _lock = new();
        private bool _sent;
        private bool _abandoned;

        /// <summary>
        /// Hands one command to the client with <paramref name="send"/>, inside the gate, or throws when the call was
        /// abandoned or <paramref name="cancellationToken"/> fired.
        /// </summary>
        public Task<RedisResult> Send(Func<Task<RedisResult>> send, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (_abandoned || cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                beforeSend?.Invoke();
                _sent = true;
                return send();
            }
        }

        /// <summary>Stops any further command, and returns whether one was sent, waiting while one is being handed over.</summary>
        public bool Abandon()
        {
            lock (_lock)
            {
                _abandoned = true;
                return _sent;
            }
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

    // Bounded by the call's own bound, which may already be spent: a free semaphore is still taken, but a build holding it
    // is not waited for. That build resolves a fresh credential itself, and there is no multiplexer to drop while it runs,
    // so nothing is left to reset. A multiplexer other than the failing one was built after the failure, with a credential
    // resolved since, so it is left in place.
    private async ValueTask ResetAfterAuthenticationFailureAsync(IConnectionMultiplexer? failing, CancellationToken cancellationToken)
    {
        try
        {
            if (!_build.Wait(0, CancellationToken.None) && !await _build.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken))
            {
                return;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (!ReferenceEquals(_multiplexer, failing))
            {
                return;
            }

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
