using System.Globalization;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.Coordination;

/// <summary>
/// The contract's coordination events 5001 to 5011, with fixed IDs, names, levels, and templates (FR-028, research
/// R18). Fields are only feature, module, primitive, operation, outcome, failure class, consistency level, coordinator
/// kind, and reason code: never a scope ID, key, hash, cached value, host, or credential (FR-026). Public because the
/// Redis and PostgreSQL adapters write these events too. Owned by ISK-0005.
/// </summary>
public static class CoordinationLog
{
    /// <summary>The text written for a field that has no value.</summary>
    public const string None = "(none)";

    private static readonly Action<ILogger, string, string, string, string, Exception?> FeatureAccepted =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Information, new EventId(5001, "CoordinationFeatureAccepted"),
            "Coordination feature accepted: feature {Feature}, module {Module}, mode {Outcome}, levels {ConsistencyLevel}.");

    private static readonly Action<ILogger, string, string, string, Exception?> FeatureRefused =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning, new EventId(5002, "CoordinationFeatureRefused"),
            "Coordination feature refused: feature {Feature}, module {Module}, reason {Reason}.");

    private static readonly Action<ILogger, string, string, Exception?> ConnectionFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning, new EventId(5003, "CoordinationConnectionFailed"),
            "Coordinator connection failed: coordinator {CoordinatorKind}, class {FailureClass}.");

    private static readonly Action<ILogger, string, string, string, string, Exception?> OutcomeUnknown =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Error, new EventId(5004, "CoordinationOutcomeUnknown"),
            "Coordination write outcome unknown: module {Module}, primitive {Primitive}, operation {Operation}, coordinator {CoordinatorKind}.");

    private static readonly Action<ILogger, string, string, Exception?> LeaseLost =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning, new EventId(5005, "CoordinationLeaseLost"),
            "Coordination lease lost on renewal: module {Module}, coordinator {CoordinatorKind}.");

    private static readonly Action<ILogger, string, string, Exception?> FencingRejected =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning, new EventId(5006, "CoordinationFencingRejected"),
            "Fencing token rejected: module {Module}, reason {Reason}.");

    private static readonly Action<ILogger, string, string, Exception?> CacheInvalidated =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug, new EventId(5007, "CoordinationCacheInvalidated"),
            "Cache namespace invalidated: module {Module}, coordinator {CoordinatorKind}.");

    private static readonly Action<ILogger, string, string, string, Exception?> CacheDegraded =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning, new EventId(5008, "CoordinationCacheDegraded"),
            "Cache read degraded to a miss: module {Module}, coordinator {CoordinatorKind}, class {FailureClass}.");

    private static readonly Action<ILogger, string, string, Exception?> HealthChanged =
        LoggerMessage.Define<string, string>(
            LogLevel.Information, new EventId(5009, "CoordinationHealthChanged"),
            "Coordination health changed: status {Outcome}, reason {Reason}.");

    private static readonly Action<ILogger, string, string, string, string, Exception?> UnexpectedFailure =
        LoggerMessage.Define<string, string, string, string>(
            LogLevel.Error, new EventId(5010, "CoordinationUnexpectedFailure"),
            "Unexpected coordination failure: module {Module}, operation {Operation}, coordinator {CoordinatorKind}, class {FailureClass}.");

    private static readonly Action<ILogger, string, string, Exception?> SingleInstanceChanged =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning, new EventId(5011, "CoordinationSingleInstanceChanged"),
            "Single-instance lease state changed: state {Outcome}, previous state {Reason}.");

    public static void LogFeatureAccepted(ILogger logger, FeatureCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        FeatureAccepted(logger, capability.Feature.Name, capability.Feature.Module, capability.Mode?.ToCode() ?? None, Levels(capability.Levels), null);
    }

    public static void LogFeatureRefused(ILogger logger, FeatureCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        FeatureRefused(logger, capability.Feature.Name, capability.Feature.Module, capability.Reason.ToCode(), null);
    }

    public static void LogConnectionFailed(ILogger logger, CoordinatorKind coordinator, CoordinationFailureClass failureClass) =>
        ConnectionFailed(logger, coordinator.ToCode(), failureClass.ToCode(), null);

    public static void LogOutcomeUnknown(ILogger logger, string module, CoordinationPrimitive primitive, string operation, CoordinatorKind coordinator) =>
        OutcomeUnknown(logger, module, primitive.ToCode(), operation, coordinator.ToCode(), null);

    public static void LogLeaseLost(ILogger logger, string module, CoordinatorKind coordinator) =>
        LeaseLost(logger, module, coordinator.ToCode(), null);

    /// <summary>Writes event 5006. <paramref name="reason"/> is <c>stale_token</c> or <c>other_lease</c>.</summary>
    public static void LogFencingRejected(ILogger logger, string module, string reason) =>
        FencingRejected(logger, module, reason, null);

    public static void LogCacheInvalidated(ILogger logger, string module, CoordinatorKind coordinator) =>
        CacheInvalidated(logger, module, coordinator.ToCode(), null);

    public static void LogCacheDegraded(ILogger logger, string module, CoordinatorKind coordinator, CoordinationFailureClass failureClass) =>
        CacheDegraded(logger, module, coordinator.ToCode(), failureClass.ToCode(), null);

    public static void LogHealthChanged(ILogger logger, CoordinationHealthResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        HealthChanged(logger, result.Status == CoordinationHealthStatus.Healthy ? "healthy" : "unhealthy", result.Reason.ToCode(), null);
    }

    public static void LogUnexpectedFailure(ILogger logger, string module, string operation, CoordinatorKind coordinator, CoordinationFailureClass failureClass) =>
        UnexpectedFailure(logger, module, operation, coordinator.ToCode(), failureClass.ToCode(), null);

    public static void LogSingleInstanceChanged(ILogger logger, SingleInstanceState state, SingleInstanceState previous) =>
        SingleInstanceChanged(logger, state.ToCode(), previous.ToCode(), null);

    /// <summary>The granted levels as <c>primitive=level</c> pairs in primitive order, or <see cref="None"/>.</summary>
    public static string Levels(IReadOnlyDictionary<CoordinationPrimitive, ConsistencyLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var pairs = CoordinationPrimitives.All
            .Where(levels.ContainsKey)
            .Select(primitive => string.Create(CultureInfo.InvariantCulture, $"{primitive.ToCode()}={levels[primitive].ToCode()}"))
            .ToList();
        return pairs.Count == 0 ? None : string.Join(',', pairs);
    }
}
