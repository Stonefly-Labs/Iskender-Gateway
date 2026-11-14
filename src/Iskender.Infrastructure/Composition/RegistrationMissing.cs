using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.Composition;

/// <summary>
/// Hosting's event 6003 and its <c>hosting.registration_missing</c> failure, for the checks this assembly runs at host
/// start (hosting contract, Registration mistakes; research R8). The event's ID, name, level, and template match
/// Hosting's definition exactly, since this assembly cannot reference Hosting (rule A8). Owned by ISK-0006.
/// </summary>
internal static class RegistrationMissing
{
    /// <summary>Hosting's logger category, which event 6003 is written under.</summary>
    public const string Category = "Iskender.Hosting";

    private static readonly Action<ILogger, string, string, Exception?> Event =
        LoggerMessage.Define<string, string>(
            LogLevel.Error, new EventId(6003, "IskenderRegistrationMissing"),
            "Iskender registration missing: {Missing} required by {RequiredBy}.");

    /// <summary>Writes 6003, and returns the failure to throw, naming <paramref name="missing"/> and <paramref name="requiredBy"/>.</summary>
    public static GatewayException Report(ILogger logger, string missing, string requiredBy)
    {
        Event(logger, missing, requiredBy, null);
        return new GatewayException(GatewayError.Create(
            ErrorCodes.HostingRegistrationMissing,
            RequestCorrelationId.New(),
            OutputVisibility.None,
            diagnostics: new ErrorDiagnostics(
                $"{ErrorCodes.HostingRegistrationMissing.Id}: missing {missing}, requiredBy {requiredBy}",
                [new("missing", missing), new("requiredBy", requiredBy)])));
    }
}
