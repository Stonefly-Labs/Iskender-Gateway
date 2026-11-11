using System.Globalization;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Infrastructure.PostgreSql.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.PostgreSql.Migrations;

/// <summary>
/// The fixed safe reasons for a refused set or a failed step (contract Migrations rule 4), and the failure that carries
/// one with its module and version as <c>ErrorDiagnostics</c> values (contract Failures rule 1). Owned by ISK-0004.
/// </summary>
internal static class MigrationRefusal
{
    public const string MissingReverse = "missing_reverse";
    public const string DuplicateVersion = "duplicate_version";
    public const string UnknownDependency = "unknown_dependency";
    public const string DependencyCycle = "dependency_cycle";
    public const string InvalidEncoding = "invalid_encoding";
    public const string CrossSchemaReference = "cross_schema_reference";
    public const string UnknownAppliedMigration = "unknown_applied_migration";
    public const string MissingRuntimeRole = "missing_runtime_role";
    public const string MissingRowSecurity = "missing_row_security";
    public const string ChecksumMismatch = "checksum_mismatch";
    public const string ForwardFailed = "forward_failed";
    public const string ReverseFailed = "reverse_failed";

    /// <summary>Logs event 4012 and returns the failure to throw. A refusal with no safe reason on the list, such as an unconfirmed data-losing reverse, passes none.</summary>
    public static GatewayException Refuse(ILogger logger, ErrorCode code, string? reason, string? module = null, int? version = null)
    {
        PersistenceLog.LogMigrationRefused(logger, code.Id, reason ?? PersistenceLog.None, module, version);
        return Failure(code, reason, module, version);
    }

    /// <summary>The failure, with <c>module</c>, <c>version</c>, and <c>reason</c> values, each present when it applies.</summary>
    public static GatewayException Failure(ErrorCode code, string? reason, string? module, int? version)
    {
        var values = new List<KeyValuePair<string, string>>(3);
        if (module is not null)
        {
            values.Add(new("module", module));
        }

        if (version is { } number)
        {
            values.Add(new("version", number.ToString(CultureInfo.InvariantCulture)));
        }

        if (reason is not null)
        {
            values.Add(new("reason", reason));
        }

        return new GatewayException(GatewayError.Create(code, RequestCorrelationId.New(), OutputVisibility.None, diagnostics: new ErrorDiagnostics(values: values)));
    }
}
