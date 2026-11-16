using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Infrastructure.Container;

/// <summary>
/// A command a plane's definition adds, such as the control plane's <c>migrate</c>. It receives the arguments after its
/// name and the entry point's context, and returns the exit code. It parses its arguments before it reads any
/// configuration, then resolves the roots with <see cref="ContainerCommandContext.ResolveSources"/> (maintainer decision,
/// 2026-11-15). Owned by ISK-0007.
/// </summary>
public delegate ValueTask<int> ContainerCommand(IReadOnlyList<string> args, ContainerCommandContext context, CancellationToken cancellationToken);

/// <summary>What a <see cref="ContainerCommand"/> shares with its entry point. Owned by ISK-0007.</summary>
public sealed class ContainerCommandContext
{
    private readonly Func<CompositionSources> _resolve;

    internal ContainerCommandContext(ContainerPlaneDefinition definition, ILoggerFactory loggerFactory, TimeProvider time, TextWriter standardError, Func<CompositionSources> resolve)
    {
        _resolve = resolve;
        Definition = definition;
        LoggerFactory = loggerFactory;
        Time = time;
        StandardError = standardError;
    }

    /// <summary>The definition the entry point runs.</summary>
    public ContainerPlaneDefinition Definition { get; }

    /// <summary>The plane the entry point runs.</summary>
    public CompositionPlane Plane => Definition.Plane;

    /// <summary>The entry point's logger factory, which the command registers in any container it builds.</summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>The entry point's clock.</summary>
    public TimeProvider Time { get; }

    /// <summary>Where usage errors are written.</summary>
    public TextWriter StandardError { get; }

    /// <summary>
    /// Resolves the configuration roots as the entry point does when it serves (container contract, Configuration roots).
    /// A root that cannot be used writes event 6002 and fails with <c>hosting.configuration_rejected</c>, naming the layer.
    /// </summary>
    public CompositionSources ResolveSources() => _resolve();

    /// <summary>Adds the entry point's logger factory and clock to <paramref name="services"/>, then the definition's registrations.</summary>
    public IServiceCollection Compose(IServiceCollection services, CompositionSources sources)
    {
        services.AddSingleton(LoggerFactory);
        services.AddSingleton(Time);
        Definition.Compose(services, sources);
        return services;
    }
}
