using System.Collections.Immutable;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iskender.Management.Security;

/// <summary>
/// The operator-security contract's events 9001 to 9008 under category <c>Iskender.Identity</c>, with fixed IDs, names,
/// levels, and templates (Diagnostics, FR-037). Fields are only purpose, reason class, route template, error code, a
/// count, and an exception's type name: never a token, hash, principal ID, network address, or configuration value. One
/// instance per host's service provider, so event 9006's once-a-minute limit is per control-plane host (service
/// container), never per process and never static. Owned by ISK-0010.
/// </summary>
internal sealed class OperatorLog
{
    /// <summary>The logger category.</summary>
    public const string Category = "Iskender.Identity";

    /// <summary>The interval event 9006 is written at most once in.</summary>
    public static readonly TimeSpan LimitedInterval = TimeSpan.FromMinutes(1);

    private static readonly Action<ILogger, string, Exception?> RedemptionSucceededEvent =
        LoggerMessage.Define<string>(
            LogLevel.Information, new EventId(9001, "OperatorRedemptionSucceeded"),
            "Iskender operator redemption succeeded: purpose {Purpose}.");

    private static readonly Action<ILogger, string, string, Exception?> RedemptionFailedEvent =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning, new EventId(9002, "OperatorRedemptionFailed"),
            "Iskender operator redemption failed: purpose {Purpose}, reason {Reason}.");

    private static readonly Action<ILogger, string, string, Exception?> RequestDeniedEvent =
        LoggerMessage.Define<string, string>(
            LogLevel.Information, new EventId(9003, "OperatorRequestDenied"),
            "Iskender admin request denied: route {Route}, reason {Reason}.");

    private static readonly Action<ILogger, string, int, Exception?> TokenIssuedEvent =
        LoggerMessage.Define<string, int>(
            LogLevel.Information, new EventId(9004, "OperatorTokenIssued"),
            "Iskender operator token issued: purpose {Purpose}, superseded {Superseded}.");

    private static readonly Action<ILogger, string, Exception?> RouteUndeclaredEvent =
        LoggerMessage.Define<string>(
            LogLevel.Error, new EventId(9005, "OperatorRouteUndeclared"),
            "Iskender admin route has no single declaration: route {Route}.");

    private static readonly Action<ILogger, Exception?> RedemptionLimitedEvent =
        LoggerMessage.Define(
            LogLevel.Warning, new EventId(9006, "OperatorRedemptionLimited"),
            "Iskender operator redemption attempts limited.");

    private static readonly Action<ILogger, string, Exception?> ResolutionFailedEvent =
        LoggerMessage.Define<string>(
            LogLevel.Warning, new EventId(9007, "OperatorResolutionFailed"),
            "Iskender operator principal resolution failed: failure {Failure}.");

    private static readonly Action<ILogger, string, string, Exception?> StoreUnavailableEvent =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning, new EventId(9008, "OperatorStoreUnavailable"),
            "Iskender operator store unavailable: route {Route}, code {Code}.");

    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _limitedGate = new();
    private long? _limitedAt;

    public OperatorLog(ILoggerFactory? loggers, TimeProvider? timeProvider = null)
    {
        _logger = loggers?.CreateLogger(Category) ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Event 9001.</summary>
    public void RedemptionSucceeded(OperatorTokenPurpose purpose) => RedemptionSucceededEvent(_logger, purpose.ToCode(), null);

    /// <summary>Event 9002, naming the reason class.</summary>
    public void RedemptionFailed(OperatorTokenPurpose purpose, RedemptionFailureReason reason) =>
        RedemptionFailedEvent(_logger, purpose.ToCode(), reason.ToCode(), null);

    /// <summary>Event 9003, with reason <c>uninitialized</c>, <c>unauthenticated</c>, or <c>forbidden</c>.</summary>
    public void RequestDenied(string route, string reason) =>
        RequestDeniedEvent(_logger, route, OperatorDenials.RequireLogged(reason), null);

    /// <summary>Event 9004, with the number of tokens the issue superseded.</summary>
    public void TokenIssued(OperatorTokenPurpose purpose, int superseded) => TokenIssuedEvent(_logger, purpose.ToCode(), superseded, null);

    /// <summary>Event 9005.</summary>
    public void RouteUndeclared(string route) => RouteUndeclaredEvent(_logger, route, null);

    /// <summary>Event 9006, written at most once per <see cref="LimitedInterval"/> by this instance.</summary>
    public void RedemptionLimited()
    {
        var now = _timeProvider.GetTimestamp();
        lock (_limitedGate)
        {
            if (_limitedAt is { } last && _timeProvider.GetElapsedTime(last, now) < LimitedInterval)
            {
                return;
            }

            _limitedAt = now;
        }

        RedemptionLimitedEvent(_logger, null);
    }

    /// <summary>Event 9007, naming only the exception's type, never its message.</summary>
    public void ResolutionFailed(Exception failure) => ResolutionFailedEvent(_logger, failure.GetType().Name, null);

    /// <summary>Event 9008, with the code the route answered.</summary>
    public void StoreUnavailable(string route, ErrorCode code) => StoreUnavailableEvent(_logger, route, code.Id, null);
}

/// <summary>The fixed denial reasons of event 9003 and <c>iskender.identity.admin.denials</c>. Owned by ISK-0010.</summary>
internal static class OperatorDenials
{
    public const string Limited = "limited";
    public const string RejectedBody = "rejected_body";
    public const string Uninitialized = "uninitialized";
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string Unavailable = "unavailable";

    public static ImmutableArray<string> All { get; } = [Limited, RejectedBody, Uninitialized, Unauthenticated, Forbidden, Unavailable];

    /// <summary>A reason event 9003 may name.</summary>
    public static string RequireLogged(string reason) =>
        reason is Uninitialized or Unauthenticated or Forbidden ? reason : throw new GatewayException(ErrorCodes.ValueInvalid);

    /// <summary>A reason the denials metric may carry.</summary>
    public static string Require(string reason) => All.Contains(reason) ? reason : throw new GatewayException(ErrorCodes.ValueInvalid);
}
