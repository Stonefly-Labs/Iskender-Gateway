using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>The probe's 10 fixed reasons (TR-11). Owned by ISK-0004.</summary>
public enum PersistenceHealthReason
{
    Ok,
    Unreachable,
    AuthenticationFailed,
    Timeout,
    SchemaBehind,
    SchemaAhead,
    ChecksumMismatch,
    MigrationInProgress,
    InsufficientPrivilege,
    Unexpected,
}

/// <summary>The snake-case code of each reason, safe to show on a dashboard. Owned by ISK-0004.</summary>
public static class PersistenceHealthReasonCodes
{
    public static string ToCode(this PersistenceHealthReason reason) => reason switch
    {
        PersistenceHealthReason.Ok => "ok",
        PersistenceHealthReason.Unreachable => "unreachable",
        PersistenceHealthReason.AuthenticationFailed => "authentication_failed",
        PersistenceHealthReason.Timeout => "timeout",
        PersistenceHealthReason.SchemaBehind => "schema_behind",
        PersistenceHealthReason.SchemaAhead => "schema_ahead",
        PersistenceHealthReason.ChecksumMismatch => "checksum_mismatch",
        PersistenceHealthReason.MigrationInProgress => "migration_in_progress",
        PersistenceHealthReason.InsufficientPrivilege => "insufficient_privilege",
        PersistenceHealthReason.Unexpected => "unexpected",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };
}
