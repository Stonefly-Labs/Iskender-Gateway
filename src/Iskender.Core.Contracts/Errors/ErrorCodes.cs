using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Errors;

/// <summary>
/// The error code registry: the only place an <see cref="ErrorCode"/> is declared (rule H5). The published contract's
/// Error codes table must match it row for row (rule H4).
/// </summary>
[ContractVersion("core-contracts", 1)]
public static partial class ErrorCodes
{
    private const string InternalMessage = "An internal error occurred.";

    public static readonly ErrorCode ResourceIdMalformed = Active(
        "core.resource_id.malformed", ErrorCategory.InvalidRequest,
        "The text is empty, too long, or not `<prefix>_<body>`",
        "The identifier is not in a valid format.");

    public static readonly ErrorCode ResourceIdUnknownType = Active(
        "core.resource_id.unknown_type", ErrorCategory.InvalidRequest,
        "The prefix is not registered",
        "The identifier is not of a known resource type.");

    public static readonly ErrorCode ResourceIdRetiredType = Active(
        "core.resource_id.retired_type", ErrorCategory.InvalidRequest,
        "The prefix is retired",
        "The identifier is of a resource type that is no longer accepted.");

    public static readonly ErrorCode ResourceIdWrongType = Active(
        "core.resource_id.wrong_type", ErrorCategory.InvalidRequest,
        "The ID is a valid ID of another type",
        "The identifier is not of the expected resource type.");

    public static readonly ErrorCode ResourceIdNonCanonical = Active(
        "core.resource_id.non_canonical", ErrorCategory.InvalidRequest,
        "The body has upper case, a character outside the alphabet, or set pad bits",
        "The identifier is not in canonical form.");

    public static readonly ErrorCode ResourceNotFound = Active(
        "core.resource.not_found", ErrorCategory.NotFound,
        "Missing, or not visible to the caller",
        "The resource was not found.");

    public static readonly ErrorCode ContextAlreadyResolved = Active(
        "core.context.already_resolved", ErrorCategory.Internal,
        "A set-once context value was resolved twice",
        InternalMessage);

    public static readonly ErrorCode ContextNotResolved = Active(
        "core.context.not_resolved", ErrorCategory.Internal,
        "A context value was read before it was resolved",
        InternalMessage);

    public static readonly ErrorCode ErrorUnregisteredCode = Active(
        "core.error.unregistered_code", ErrorCategory.Internal,
        "An error was built with a code not in the registry",
        InternalMessage);

    public static readonly ErrorCode ErrorRetiredCode = Active(
        "core.error.retired_code", ErrorCategory.Internal,
        "An error was built with a retired code",
        InternalMessage);

    public static readonly ErrorCode ErrorRetryNotPermitted = Active(
        "core.error.retry_not_permitted", ErrorCategory.Internal,
        "Retry advice was given where the category or visibility forbids it",
        InternalMessage);

    public static readonly ErrorCode RandomSourceFailed = Active(
        "core.random_source_failed", ErrorCategory.Internal,
        "The secure random source failed",
        InternalMessage);

    public static readonly ErrorCode ValueInvalid = Active(
        "core.value.invalid", ErrorCategory.InvalidRequest,
        "A bounded value, such as a revision or provider ID, is outside its limits",
        "A value in the request is outside its allowed limits.");

    public static readonly ErrorCode DeadlineExceeded = Active(
        "core.deadline_exceeded", ErrorCategory.DeadlineExceeded,
        "The request's deadline passed",
        "The request did not complete before its deadline.");

    public static readonly ErrorCode Cancelled = Active(
        "core.cancelled", ErrorCategory.Cancelled,
        "The request was cancelled",
        "The request was cancelled.");

    public static readonly ErrorCode Internal = Active(
        "core.internal", ErrorCategory.Internal,
        "An unexpected gateway fault",
        InternalMessage);

    public static readonly ErrorCode IdentityUnauthenticated = Active(
        "identity.unauthenticated", ErrorCategory.Unauthenticated,
        "No valid credential was presented",
        "No valid credential was presented.");

    public static readonly ErrorCode IdentityAuthDenied = Active(
        "identity.auth_denied", ErrorCategory.PermissionDenied,
        "The caller may not perform the action (`docs/08` reason)",
        "The caller is not allowed to perform this action.");

    public static readonly ErrorCode PolicyModelDisallowed = Active(
        "policy.model_disallowed", ErrorCategory.PermissionDenied,
        "Policy does not allow the requested model (`docs/08` reason)",
        "The requested model is not allowed.");

    public static readonly ErrorCode GovernanceQuotaExceeded = Active(
        "governance.quota_exceeded", ErrorCategory.LimitExceeded,
        "A quota was reached (`docs/08` reason)",
        "A usage limit was reached.");

    public static readonly ErrorCode GovernanceBudgetBlocked = Active(
        "governance.budget_blocked", ErrorCategory.BudgetBlocked,
        "A hard budget blocks the request (`docs/08` reason)",
        "A budget limit blocks this request.");

    public static readonly ErrorCode RoutingNoHealthyBackend = Active(
        "routing.no_healthy_backend", ErrorCategory.UpstreamUnavailable,
        "No healthy backend could take the request (`docs/08` reason)",
        "No backend is currently available to handle the request.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigDocumentMalformed = ActiveSince2(
        "config.document.malformed", ErrorCategory.InvalidRequest,
        "The document is not well-formed JSON or YAML, or is not clean UTF-8",
        "The configuration document could not be read.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigDocumentTooLarge = ActiveSince2(
        "config.document.too_large", ErrorCategory.InvalidRequest,
        "The document passes a size, depth, node, or string limit",
        "The configuration document is larger than allowed.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigDocumentDuplicateKey = ActiveSince2(
        "config.document.duplicate_key", ErrorCategory.InvalidRequest,
        "A key appears twice in one object",
        "The configuration document repeats a key.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigDocumentUnsupportedConstruct = ActiveSince2(
        "config.document.unsupported_construct", ErrorCategory.InvalidRequest,
        "The YAML uses an anchor, alias, tag, merge key, or several documents",
        "The configuration document uses an unsupported construct.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigNamespaceUnknown = ActiveSince2(
        "config.namespace.unknown", ErrorCategory.InvalidRequest,
        "A key under `iskender` names no registered section",
        "The configuration names an unknown section.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigFieldUnknown = ActiveSince2(
        "config.field.unknown", ErrorCategory.InvalidRequest,
        "A field is not declared by its section schema",
        "The configuration names an unknown field.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigFieldRequired = ActiveSince2(
        "config.field.required", ErrorCategory.InvalidRequest,
        "A required field without a default is missing",
        "A required configuration field is missing.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigFieldTypeMismatch = ActiveSince2(
        "config.field.type_mismatch", ErrorCategory.InvalidRequest,
        "A value is not of the declared type",
        "A configuration value has the wrong type.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigFieldOutOfRange = ActiveSince2(
        "config.field.out_of_range", ErrorCategory.InvalidRequest,
        "A value is outside its range, length, or count, or does not fit its type",
        "A configuration value is outside its allowed range.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigFieldInvalidFormat = ActiveSince2(
        "config.field.invalid_format", ErrorCategory.InvalidRequest,
        "A value breaks its pattern, enumeration, duration, or URI rule",
        "A configuration value is not in a valid format.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigSecretPlaintext = ActiveSince2(
        "config.secret.plaintext", ErrorCategory.InvalidRequest,
        "A secret field holds a plain value instead of a placeholder",
        "A secret configuration field must use a secret reference.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigSecretReferenceMalformed = ActiveSince2(
        "config.secret.reference_malformed", ErrorCategory.InvalidRequest,
        "A placeholder's form or reference name is invalid",
        "A secret reference is not in a valid format.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigValueUnsafeForEnvironment = ActiveSince2(
        "config.value.unsafe_for_environment", ErrorCategory.InvalidRequest,
        "A development-only value is set outside `development`",
        "A configuration value is not allowed in this environment.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigEnvironmentInvalid = ActiveSince2(
        "config.environment.invalid", ErrorCategory.InvalidRequest,
        "The active environment name is malformed",
        "The environment name is not in a valid format.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigOverlayConflict = ActiveSince2(
        "config.overlay.conflict", ErrorCategory.InvalidRequest,
        "Two environment variables map to one field",
        "Two configuration sources set the same field.");

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigSchemaDuplicateNamespace = ActiveSince2(
        "config.schema.duplicate_namespace", ErrorCategory.Internal,
        "Two section schemas claim one namespace",
        InternalMessage);

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigSchemaInvalid = ActiveSince2(
        "config.schema.invalid", ErrorCategory.Internal,
        "A section schema contradicts itself",
        InternalMessage);

    [ContractVersion("core-contracts", 2)]
    public static readonly ErrorCode ConfigSectionInvalidAccess = ActiveSince2(
        "config.section.invalid_access", ErrorCategory.Internal,
        "A module read a snapshot with a schema instance that was not registered, an undeclared field name, or a getter of the wrong type",
        InternalMessage);

    [ContractVersion("core-contracts", 3)]
    public static readonly ErrorCode PersistenceUnavailable = ActiveSince3(
        "persistence.unavailable", ErrorCategory.Internal,
        "The data store cannot be reached, the connection broke, the credentials were rejected or unavailable, or a commit's outcome is unknown",
        "The data store is unavailable.");

    [ContractVersion("core-contracts", 3)]
    public static readonly ErrorCode PersistenceTimeout = ActiveSince3(
        "persistence.timeout", ErrorCategory.DeadlineExceeded,
        "A connection, statement, or unit of work bound or the request deadline passed",
        "The data store did not respond in time.");

    [ContractVersion("core-contracts", 3)]
    public static readonly ErrorCode PersistenceCancelled = ActiveSince3(
        "persistence.cancelled", ErrorCategory.Cancelled,
        "The caller's or the request's cancellation stopped a data store operation",
        "The data store operation was cancelled.");

    [ContractVersion("core-contracts", 3)]
    public static readonly ErrorCode PersistenceConcurrencyConflict = ActiveSince3(
        "persistence.concurrency_conflict", ErrorCategory.Conflict,
        "A stale version token, or a serialization failure or deadlock that was not retried",
        "The data changed while the request was being processed.");

    [ContractVersion("core-contracts", 3)]
    public static readonly ErrorCode PersistenceUniqueConflict = ActiveSince3(
        "persistence.unique_conflict", ErrorCategory.Conflict,
        "A write broke a unique constraint",
        "A record with the same identity already exists.");

    [ContractVersion("core-contracts", 3)]
    public static readonly ErrorCode PersistenceScopeRequired = ActiveSince3(
        "persistence.scope_required", ErrorCategory.Internal,
        "A unit of work was opened without a scope or from an unresolved owner",
        "A data store operation was attempted without an owner scope.");

    [ContractVersion("core-contracts", 3)]
    public static readonly ErrorCode PersistenceScopeNotPermitted = ActiveSince3(
        "persistence.scope_not_permitted", ErrorCategory.PermissionDenied,
        "An operator-scope unit named a tenant table, or a tenant-scope unit an operator table",
        "This data is not available in the current scope.");

    [ContractVersion("core-contracts", 3)]
    public static readonly ErrorCode PersistenceLockTimeout = ActiveSince3(
        "persistence.lock_timeout", ErrorCategory.Conflict,
        "A bounded advisory lock wait expired",
        "A required lock could not be acquired in time.");

    [ContractVersion("core-contracts", 3)]
    public static readonly ErrorCode PersistenceMigrationLocked = ActiveSince3(
        "persistence.migration_locked", ErrorCategory.Internal,
        "Another migrator held the migration lock for longer than the configured wait",
        "Another schema migration is in progress.");

    [ContractVersion("core-contracts", 3)]
    public static readonly ErrorCode PersistenceMigrationChecksumMismatch = ActiveSince3(
        "persistence.migration_checksum_mismatch", ErrorCategory.Internal,
        "An applied migration's checksum differs from the shipped scripts",
        "An applied schema migration does not match this release.");

    [ContractVersion("core-contracts", 3)]
    public static readonly ErrorCode PersistenceSchemaIncompatible = ActiveSince3(
        "persistence.schema_incompatible", ErrorCategory.Internal,
        "The migration set or the database state was refused before anything was applied",
        "The database schema is not compatible with this release.");

    [ContractVersion("core-contracts", 3)]
    public static readonly ErrorCode PersistenceReverseRequiresConfirmation = ActiveSince3(
        "persistence.reverse_requires_confirmation", ErrorCategory.InvalidRequest,
        "A requested reverse step is data-losing and was not confirmed",
        "This rollback step deletes data and must be confirmed.");

    [ContractVersion("core-contracts", 4)]
    public static readonly ErrorCode CoordinationUnavailable = ActiveSince4(
        "coordination.unavailable", ErrorCategory.Internal,
        "The coordinator cannot be reached, the connection broke, it answered as a read-only replica, or its credentials were rejected or unavailable",
        "The coordination service is unavailable.");

    [ContractVersion("core-contracts", 4)]
    public static readonly ErrorCode CoordinationTimeout = ActiveSince4(
        "coordination.timeout", ErrorCategory.DeadlineExceeded,
        "The coordination operation timeout or the request deadline passed",
        "The coordination service did not respond in time.");

    [ContractVersion("core-contracts", 4)]
    public static readonly ErrorCode CoordinationCancelled = ActiveSince4(
        "coordination.cancelled", ErrorCategory.Cancelled,
        "The caller's or the request's cancellation stopped a coordination operation",
        "The coordination operation was cancelled.");

    [ContractVersion("core-contracts", 4)]
    public static readonly ErrorCode CoordinationLeaseLost = ActiveSince4(
        "coordination.lease_lost", ErrorCategory.Conflict,
        "A lease was renewed by a holder that no longer holds its token",
        "The lease is no longer held.");

    [ContractVersion("core-contracts", 4)]
    public static readonly ErrorCode CoordinationFencingRejected = ActiveSince4(
        "coordination.fencing_rejected", ErrorCategory.Conflict,
        "The fencing guard refused a token below the highest accepted, or one from another lease key",
        "A newer holder has already changed this resource.");

    [ContractVersion("core-contracts", 4)]
    public static readonly ErrorCode CoordinationCapabilityUnsupported = ActiveSince4(
        "coordination.capability_unsupported", ErrorCategory.Internal,
        "The capability check refused a feature or its declaration, the feature is unknown or used an undeclared primitive, or the fencing guard was given a lease it does not accept",
        "This feature is not available in the current deployment.");

    [ContractVersion("core-contracts", 4)]
    public static readonly ErrorCode CoordinationScopeRequired = ActiveSince4(
        "coordination.scope_required", ErrorCategory.Internal,
        "A coordination operation was attempted without a scope or from an unresolved owner",
        "A coordination operation was attempted without an owner scope.");

    [ContractVersion("core-contracts", 5)]
    public static readonly ErrorCode HostingConfigurationRejected = ActiveSince5(
        "hosting.configuration_rejected", ErrorCategory.Internal,
        "Host start failed because Iskender's configuration was rejected or a configuration document could not be read",
        "Iskender could not start because its configuration was rejected.");

    [ContractVersion("core-contracts", 5)]
    public static readonly ErrorCode HostingRegistrationDuplicate = ActiveSince5(
        "hosting.registration_duplicate", ErrorCategory.Internal,
        "AddIskender(...) or one module registration was called twice on one service collection",
        "An Iskender registration was added more than once.");

    [ContractVersion("core-contracts", 5)]
    public static readonly ErrorCode HostingRegistrationMissing = ActiveSince5(
        "hosting.registration_missing", ErrorCategory.Internal,
        "A module registration or MapIskender(...) was used without AddIskender(...), a module was registered without a module it requires, or a coordinator selected by configuration has no registration",
        "A required Iskender registration is missing.");

    /// <summary>Every registered code, in contract order.</summary>
    public static IReadOnlyList<ErrorCode> All { get; } = Validate(
    [
        ResourceIdMalformed, ResourceIdUnknownType, ResourceIdRetiredType, ResourceIdWrongType, ResourceIdNonCanonical,
        ResourceNotFound, ContextAlreadyResolved, ContextNotResolved, ErrorUnregisteredCode, ErrorRetiredCode,
        ErrorRetryNotPermitted, RandomSourceFailed, ValueInvalid, DeadlineExceeded, Cancelled, Internal,
        IdentityUnauthenticated, IdentityAuthDenied, PolicyModelDisallowed, GovernanceQuotaExceeded, GovernanceBudgetBlocked,
        RoutingNoHealthyBackend, ConfigDocumentMalformed, ConfigDocumentTooLarge, ConfigDocumentDuplicateKey,
        ConfigDocumentUnsupportedConstruct, ConfigNamespaceUnknown, ConfigFieldUnknown, ConfigFieldRequired, ConfigFieldTypeMismatch,
        ConfigFieldOutOfRange, ConfigFieldInvalidFormat, ConfigSecretPlaintext, ConfigSecretReferenceMalformed,
        ConfigValueUnsafeForEnvironment, ConfigEnvironmentInvalid, ConfigOverlayConflict, ConfigSchemaDuplicateNamespace,
        ConfigSchemaInvalid, ConfigSectionInvalidAccess, PersistenceUnavailable, PersistenceTimeout, PersistenceCancelled,
        PersistenceConcurrencyConflict, PersistenceUniqueConflict, PersistenceScopeRequired, PersistenceScopeNotPermitted,
        PersistenceLockTimeout, PersistenceMigrationLocked, PersistenceMigrationChecksumMismatch, PersistenceSchemaIncompatible,
        PersistenceReverseRequiresConfirmation, CoordinationUnavailable, CoordinationTimeout, CoordinationCancelled,
        CoordinationLeaseLost, CoordinationFencingRejected, CoordinationCapabilityUnsupported, CoordinationScopeRequired,
        HostingConfigurationRejected, HostingRegistrationDuplicate, HostingRegistrationMissing,
    ]);

    /// <summary>Finds a registered code by its ID, compared ordinally.</summary>
    public static bool TryGet(string id, [NotNullWhen(true)] out ErrorCode? code)
    {
        code = All.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        return code is not null;
    }

    /// <summary>Rejects a duplicate ID, an ID outside the code pattern, or a category outside the closed set.</summary>
    internal static IReadOnlyList<ErrorCode> Validate(ErrorCode[] codes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            if (!CodePattern().IsMatch(code.Id))
            {
                throw new InvalidOperationException($"Error code '{code.Id}' does not match {CodePattern()}.");
            }

            if (!seen.Add(code.Id))
            {
                throw new InvalidOperationException($"Error code '{code.Id}' is registered twice.");
            }

            if (!ErrorCategory.All.Contains(code.Category))
            {
                throw new InvalidOperationException($"Error code '{code.Id}' has a category outside the closed set.");
            }
        }

        return Array.AsReadOnly(codes);
    }

    private static ErrorCode Active(string id, ErrorCategory category, string meaning, string safeMessage) =>
        new(id, category, meaning, safeMessage, RegistryStatus.Active, since: 1);

    private static ErrorCode ActiveSince2(string id, ErrorCategory category, string meaning, string safeMessage) =>
        new(id, category, meaning, safeMessage, RegistryStatus.Active, since: 2);

    private static ErrorCode ActiveSince3(string id, ErrorCategory category, string meaning, string safeMessage) =>
        new(id, category, meaning, safeMessage, RegistryStatus.Active, since: 3);

    private static ErrorCode ActiveSince4(string id, ErrorCategory category, string meaning, string safeMessage) =>
        new(id, category, meaning, safeMessage, RegistryStatus.Active, since: 4);

    private static ErrorCode ActiveSince5(string id, ErrorCategory category, string meaning, string safeMessage) =>
        new(id, category, meaning, safeMessage, RegistryStatus.Active, since: 5);

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
