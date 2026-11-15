using System.Globalization;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Domain.Registry.Composition;

/// <summary>
/// Finds the modules and composition root registered in a service collection, checks requirements, and builds the
/// registration failures, which name a module and never a configuration value (hosting contract, Registration rule 7 and
/// Registration mistakes). Owned by ISK-0006, as Hosting's composition guard; moved here by ISK-0007.
/// </summary>
public static class CompositionModules
{
    /// <summary>Every <see cref="CompositionModule"/> instance registered in the collection, in registration order.</summary>
    public static IEnumerable<CompositionModule> Modules(IServiceCollection services) =>
        services.Where(descriptor => descriptor.ServiceType == typeof(CompositionModule) && !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<CompositionModule>();

    /// <summary>The <see cref="CompositionRoot"/> registered in the collection, or <c>null</c>.</summary>
    public static CompositionRoot? Root(IServiceCollection services) =>
        services.Where(descriptor => descriptor.ServiceType == typeof(CompositionRoot) && !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<CompositionRoot>()
            .FirstOrDefault();

    /// <summary>Fails with <c>hosting.registration_duplicate</c> when a module of this name is already registered.</summary>
    public static void ThrowIfRegistered(IServiceCollection services, string module)
    {
        if (Modules(services).Any(registered => registered.Name == module))
        {
            throw Duplicate(module);
        }
    }

    /// <summary>
    /// The first requirement, in ordinal order of module and then of requirement, that is not met. <c>hosting</c> is met
    /// when <paramref name="rootRegistered"/> is <c>true</c>, and every other name only by a module of that name.
    /// </summary>
    public static (string Missing, string RequiredBy)? FirstUnmet(IEnumerable<CompositionModule> modules, bool rootRegistered)
    {
        var registered = modules.ToList();
        var names = registered.Select(module => module.Name).ToHashSet(StringComparer.Ordinal);
        if (rootRegistered)
        {
            names.Add(CompositionRoot.Requirement);
        }

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

    /// <summary>A <c>hosting.registration_duplicate</c> failure naming the module.</summary>
    public static GatewayException Duplicate(string module) =>
        Failure(ErrorCodes.HostingRegistrationDuplicate, [new("module", module)]);

    private static GatewayException Failure(ErrorCode code, KeyValuePair<string, string>[] values) =>
        new(GatewayError.Create(
            code,
            RequestCorrelationId.New(),
            OutputVisibility.None,
            diagnostics: new ErrorDiagnostics(
                string.Create(CultureInfo.InvariantCulture, $"{code.Id}: {string.Join(", ", values.Select(pair => $"{pair.Key} {pair.Value}"))}"),
                values)));
}
