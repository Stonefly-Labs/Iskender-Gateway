using Iskender.Domain.Ports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Iskender.Domain.Registry.Composition;

/// <summary>
/// Fails the host's start when modules are registered without a composition root (hosting contract, Registration
/// mistakes). Without a root no one else runs the start-up checks, so this writes event 6003 itself. Owned by ISK-0006, in
/// <c>Iskender.Infrastructure</c>; moved here by ISK-0007.
/// </summary>
public sealed class CompositionCheck : IHostedService
{
    /// <summary>Hosting's logger category, which event 6003 is written under.</summary>
    public static string Category => CompositionLog.Category;

    private readonly IReadOnlyList<CompositionModule> _modules;

    private readonly CompositionRoot? _root;

    private readonly ILogger _logger;

    public CompositionCheck(IEnumerable<CompositionModule> modules, CompositionRoot? root, ILogger logger)
    {
        _modules = [.. modules];
        _root = root;
        _logger = logger;
    }

    /// <summary>With no composition root, writes 6003 naming <c>hosting</c> and the first registered module in ordinal order, and fails with <c>hosting.registration_missing</c>.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_modules.Count == 0 || _root is not null)
        {
            return Task.CompletedTask;
        }

        var requiredBy = _modules.Select(module => module.Name).Order(StringComparer.Ordinal).First();
        throw CompositionLog.RegistrationMissing(_logger, CompositionRoot.Requirement, requiredBy);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
