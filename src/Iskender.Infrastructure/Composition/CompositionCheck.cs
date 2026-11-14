using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.Composition;

/// <summary>
/// Fails the host's start when modules are registered without <c>AddIskender(...)</c> (hosting contract, Registration
/// mistakes; research R8). Without Hosting no one else runs the start-up checks, so this writes Hosting's event 6003
/// itself. Its ID, name, level, and template match Hosting's definition exactly, since this assembly cannot reference
/// Hosting (rule A8). Owned by ISK-0006.
/// </summary>
internal sealed class CompositionCheck : IHostedService
{
    /// <summary>Hosting's logger category, which event 6003 is written under.</summary>
    public const string Category = "Iskender.Hosting";

    private const string HostingModule = "hosting";

    private static readonly Action<ILogger, string, string, Exception?> RegistrationMissing =
        LoggerMessage.Define<string, string>(
            LogLevel.Error, new EventId(6003, "IskenderRegistrationMissing"),
            "Iskender registration missing: {Missing} required by {RequiredBy}.");

    private readonly IReadOnlyList<CompositionModule> _modules;

    private readonly ILogger _logger;

    public CompositionCheck(IEnumerable<CompositionModule> modules, ILogger logger)
    {
        _modules = [.. modules];
        _logger = logger;
    }

    /// <summary>With no <c>hosting</c> module, writes 6003 naming the first registered module in ordinal order, and fails with <c>hosting.registration_missing</c>.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_modules.Count == 0 || _modules.Any(module => module.Name == HostingModule))
        {
            return Task.CompletedTask;
        }

        var requiredBy = _modules.Select(module => module.Name).Order(StringComparer.Ordinal).First();
        RegistrationMissing(_logger, HostingModule, requiredBy, null);
        throw new GatewayException(GatewayError.Create(
            ErrorCodes.HostingRegistrationMissing,
            RequestCorrelationId.New(),
            OutputVisibility.None,
            diagnostics: new ErrorDiagnostics(
                $"{ErrorCodes.HostingRegistrationMissing.Id}: missing {HostingModule}, requiredBy {requiredBy}",
                [new("missing", HostingModule), new("requiredBy", requiredBy)])));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
