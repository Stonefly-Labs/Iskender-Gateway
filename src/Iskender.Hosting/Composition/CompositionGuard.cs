using System.Globalization;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Composition;

/// <summary>
/// Finds the modules registered in a service collection, and builds the registration failures, which name a module and
/// never a configuration value (hosting contract, Registration mistakes). Owned by ISK-0006.
/// </summary>
internal static class CompositionGuard
{
    /// <summary>The module <c>AddIskender(...)</c> registers.</summary>
    public const string HostingModule = "hosting";

    /// <summary>Every <see cref="CompositionModule"/> instance registered in the collection, in registration order.</summary>
    public static IEnumerable<CompositionModule> Modules(IServiceCollection services) =>
        services.Where(descriptor => descriptor.ServiceType == typeof(CompositionModule) && !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<CompositionModule>();

    /// <summary>Whether <c>AddIskender(...)</c> has registered the <c>hosting</c> module in the collection.</summary>
    public static bool HasHosting(IServiceCollection services) => Modules(services).Any(module => module.Name == HostingModule);

    /// <summary>Fails with <c>hosting.registration_duplicate</c> when a module of this name is already registered.</summary>
    public static void ThrowIfRegistered(IServiceCollection services, string module)
    {
        if (Modules(services).Any(registered => registered.Name == module))
        {
            throw Failure(ErrorCodes.HostingRegistrationDuplicate, [new("module", module)]);
        }
    }

    /// <summary>The first requirement, in ordinal order of module and then of requirement, that names no registered module.</summary>
    public static (string Missing, string RequiredBy)? FirstUnmet(IEnumerable<CompositionModule> modules)
    {
        var registered = modules.ToList();
        var names = registered.Select(module => module.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var module in registered.OrderBy(module => module.Name, StringComparer.Ordinal))
        {
            foreach (var required in module.Requires.Order(StringComparer.Ordinal))
            {
                if (!names.Contains(required))
                {
                    return (required, module.Name);
                }
            }
        }

        return null;
    }

    /// <summary>A <c>hosting.registration_missing</c> failure naming what is missing and what requires it.</summary>
    public static GatewayException Missing(string missing, string requiredBy) =>
        Failure(ErrorCodes.HostingRegistrationMissing, [new("missing", missing), new("requiredBy", requiredBy)]);

    private static GatewayException Failure(ErrorCode code, KeyValuePair<string, string>[] values) =>
        new(GatewayError.Create(
            code,
            RequestCorrelationId.New(),
            OutputVisibility.None,
            diagnostics: new ErrorDiagnostics(
                string.Create(CultureInfo.InvariantCulture, $"{code.Id}: {string.Join(", ", values.Select(pair => $"{pair.Key} {pair.Value}"))}"),
                values)));
}
