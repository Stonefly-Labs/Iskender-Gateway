using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql;

/// <summary>
/// The runtime data source and, when both migration fields are set, the migration data source (research R15, R17). Each
/// physical connection open asks the host's <see cref="IDatabaseCredentialProvider"/> for its password, so nothing is
/// cached in settings or logged. <c>tlsMode: require</c> verifies the certificate chain and host name. Npgsql's own
/// logging is never wired, and its command spans, which carry SQL, are filtered off. Owned by ISK-0004.
/// </summary>
public sealed class PostgreSqlDataSources : IAsyncDisposable
{
    private readonly IDatabaseCredentialProvider? _credentials;
    private readonly NpgsqlDataSource? _migration;

    /// <summary>A <c>null</c> provider is allowed: every connection attempt then fails with <c>persistence.unavailable</c> (FR-031).</summary>
    public PostgreSqlDataSources(PostgreSqlSettings settings, IDatabaseCredentialProvider? credentials)
    {
        Settings = settings ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _credentials = credentials;
        Runtime = Build(settings, settings.Username, settings.Password, DatabaseCredentialPurpose.Runtime);
        if (settings.HasMigrationCredentials)
        {
            _migration = Build(settings, settings.MigrationUsername!, settings.MigrationPassword!, DatabaseCredentialPurpose.Migration);
        }
    }

    public PostgreSqlSettings Settings { get; }

    internal NpgsqlDataSource Runtime { get; }

    /// <summary>The migration data source. Fails with <c>persistence.unavailable</c>, before connecting, when either migration field is missing.</summary>
    internal NpgsqlDataSource Migration => _migration ?? throw new GatewayException(ErrorCodes.PersistenceUnavailable);

    public async ValueTask DisposeAsync()
    {
        await Runtime.DisposeAsync();
        if (_migration is not null)
        {
            await _migration.DisposeAsync();
        }
    }

    /// <summary>The connection string the adapter builds, without any password. Internal so tests can check its mapping.</summary>
    internal static NpgsqlConnectionStringBuilder ConnectionString(PostgreSqlSettings settings, string username) => new()
    {
        Host = settings.Host,
        Port = settings.Port,
        Database = settings.Database,
        Username = username,
        SslMode = settings.TlsMode == PostgreSqlTlsMode.Require ? SslMode.VerifyFull : SslMode.Disable,
        MinPoolSize = settings.PoolMinimum,
        MaxPoolSize = settings.PoolMaximum,
        Timeout = Math.Max(1, (int)Math.Ceiling(settings.ConnectionTimeout.TotalSeconds)),
        CommandTimeout = 0,
        IncludeErrorDetail = false,
        LogParameters = false,
        Enlist = false,
    };

    private NpgsqlDataSource Build(PostgreSqlSettings settings, string username, SecretReference reference, DatabaseCredentialPurpose purpose)
    {
        var builder = new NpgsqlDataSourceBuilder(ConnectionString(settings, username).ConnectionString);
        var request = new DatabaseCredentialRequest(reference, purpose);
        builder.UsePasswordProvider(
            _ => PasswordAsync(request, CancellationToken.None).AsTask().GetAwaiter().GetResult(),
            (_, cancellationToken) => PasswordAsync(request, cancellationToken));
        builder.ConfigureTracing(tracing => tracing
            .ConfigureCommandFilter(_ => false)
            .ConfigureBatchFilter(_ => false)
            .ConfigureCopyOperationFilter(_ => false)
            .EnablePhysicalOpenTracing(false));
        return builder.Build();
    }

    /// <summary>Asks the provider once per physical open. A missing or failing provider becomes a credential failure with none of the provider's text.</summary>
    private async ValueTask<string> PasswordAsync(DatabaseCredentialRequest request, CancellationToken cancellationToken)
    {
        if (_credentials is null)
        {
            throw new CredentialUnavailableException();
        }

        DatabaseCredential credential;
        try
        {
            credential = await _credentials.GetCredentialAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The provider's exception is dropped on purpose, so none of its text crosses the port (research R17).
            throw new CredentialUnavailableException();
        }

        return credential?.Password ?? throw new CredentialUnavailableException();
    }
}
