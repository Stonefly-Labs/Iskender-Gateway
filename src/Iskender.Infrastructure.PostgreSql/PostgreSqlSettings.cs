using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Infrastructure.PostgreSql;

/// <summary>How the adapter protects the database link. <c>Require</c> verifies the certificate chain and host name (research R15). Owned by ISK-0004.</summary>
public enum PostgreSqlTlsMode
{
    Require,
    Disable,
}

/// <summary>
/// The validated <c>iskender.persistence</c> section, read once. It holds secret references, never passwords
/// (research R16). Owned by ISK-0004.
/// </summary>
public sealed class PostgreSqlSettings
{
    internal PostgreSqlSettings(
        string host,
        int port,
        string database,
        string username,
        SecretReference password,
        PostgreSqlTlsMode tlsMode,
        int poolMinimum,
        int poolMaximum,
        TimeSpan connectionTimeout,
        TimeSpan statementTimeout,
        TimeSpan healthProbeTimeout,
        string? migrationUsername,
        SecretReference? migrationPassword,
        TimeSpan migrationLockWait)
    {
        Host = host;
        Port = port;
        Database = database;
        Username = username;
        Password = password;
        TlsMode = tlsMode;
        PoolMinimum = poolMinimum;
        PoolMaximum = poolMaximum;
        ConnectionTimeout = connectionTimeout;
        StatementTimeout = statementTimeout;
        HealthProbeTimeout = healthProbeTimeout;
        MigrationUsername = migrationUsername;
        MigrationPassword = migrationPassword;
        MigrationLockWait = migrationLockWait;
    }

    public string Host { get; }

    public int Port { get; }

    public string Database { get; }

    /// <summary>The runtime login.</summary>
    public string Username { get; }

    /// <summary>The runtime login's secret reference.</summary>
    public SecretReference Password { get; }

    public PostgreSqlTlsMode TlsMode { get; }

    public int PoolMinimum { get; }

    public int PoolMaximum { get; }

    public TimeSpan ConnectionTimeout { get; }

    public TimeSpan StatementTimeout { get; }

    public TimeSpan HealthProbeTimeout { get; }

    /// <summary>The migration login, or <c>null</c> for a process that does not migrate.</summary>
    public string? MigrationUsername { get; }

    public SecretReference? MigrationPassword { get; }

    public TimeSpan MigrationLockWait { get; }

    /// <summary>True when both migration fields are set.</summary>
    public bool HasMigrationCredentials => MigrationUsername is not null && MigrationPassword is not null;

    /// <summary>
    /// Reads a validated section. <c>poolMinimum</c> above <c>poolMaximum</c> fails with <c>config.field.out_of_range</c>,
    /// naming <c>poolMinimum</c> in the error's diagnostics.
    /// </summary>
    public static PostgreSqlSettings From(ConfigurationSection section)
    {
        if (section is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var poolMinimum = (int)section.GetInt64("poolMinimum")!.Value;
        var poolMaximum = (int)section.GetInt64("poolMaximum")!.Value;
        if (poolMinimum > poolMaximum)
        {
            throw new GatewayException(GatewayError.Create(
                ErrorCodes.ConfigFieldOutOfRange,
                RequestCorrelationId.New(),
                OutputVisibility.None,
                diagnostics: new ErrorDiagnostics(values: [new("field", $"{section.Path}.poolMinimum")])));
        }

        var migration = section.GetSection("migration");
        return new PostgreSqlSettings(
            section.GetString("host")!,
            (int)section.GetInt64("port")!.Value,
            section.GetString("database")!,
            section.GetString("username")!,
            section.GetSecretReference("password")!,
            section.GetString("tlsMode") == "disable" ? PostgreSqlTlsMode.Disable : PostgreSqlTlsMode.Require,
            poolMinimum,
            poolMaximum,
            section.GetDuration("connectionTimeout")!.Value,
            section.GetDuration("statementTimeout")!.Value,
            section.GetDuration("healthProbeTimeout")!.Value,
            migration.GetString("username"),
            migration.GetSecretReference("password"),
            migration.GetDuration("lockWait")!.Value);
    }
}
