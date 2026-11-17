namespace Iskender.Infrastructure.Container;

/// <summary>The container entry points' exit codes (container contract, Command line and exit codes rule 2). Owned by ISK-0007.</summary>
public static class ExitCodes
{
    /// <summary>A clean stop after a stop signal, or a command that succeeded.</summary>
    public const int Success = 0;

    /// <summary>An unexpected failure, or a <c>health</c> check that did not answer <c>200</c>.</summary>
    public const int Unexpected = 1;

    /// <summary><c>hosting.configuration_rejected</c>, or equal traffic and health ports.</summary>
    public const int ConfigurationRejected = 2;

    /// <summary><c>hosting.registration_missing</c>.</summary>
    public const int RegistrationMissing = 3;

    /// <summary>A refused feature with <c>MandatoryPath</c>.</summary>
    public const int MandatoryFeatureRefused = 4;

    /// <summary>A migration command failed, or <c>migrate status</c> could not run.</summary>
    public const int MigrationFailed = 5;

    /// <summary><c>migrate status</c>: the schema is behind.</summary>
    public const int SchemaBehind = 6;

    /// <summary><c>migrate status</c>: the schema is ahead or mismatched.</summary>
    public const int SchemaAhead = 7;

    /// <summary>A usage error.</summary>
    public const int Usage = 64;
}
