using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Domain.Ports;

/// <summary>
/// Registers work kinds, handlers, and recurring jobs in a host's service collection (work contract, Registration
/// rules 3 and 4). A second registration for one kind or job name fails at the call with
/// <c>hosting.registration_duplicate</c>. Owned by ISK-0009.
/// </summary>
public static class WorkRegistrations
{
    /// <summary>Declares that this process may enqueue <paramref name="declaration"/>'s kind.</summary>
    public static IServiceCollection AddKind(IServiceCollection services, WorkKindDeclaration declaration)
    {
        Require(services, declaration);
        if (Registered<WorkKindDeclaration>(services).Any(existing => existing.Kind == declaration.Kind))
        {
            throw CompositionModules.Duplicate(declaration.Kind.ToString());
        }

        services.AddSingleton(declaration);
        return services;
    }

    /// <summary>
    /// Registers the kind's one handler. A registration whose <see cref="WorkHandlerRegistration.HandlerType"/> is not
    /// <typeparamref name="THandler"/> fails with <c>core.value.invalid</c>.
    /// </summary>
    public static IServiceCollection AddHandler<THandler>(IServiceCollection services, WorkHandlerRegistration registration)
        where THandler : class, IWorkHandler
    {
        Require(services, registration);
        if (registration.HandlerType != typeof(THandler))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        if (Registered<WorkHandlerRegistration>(services).Any(existing => existing.Kind == registration.Kind))
        {
            throw CompositionModules.Duplicate(registration.Kind.ToString());
        }

        services.AddSingleton(registration);
        services.AddTransient<THandler>();
        return services;
    }

    /// <summary>
    /// Registers a recurring job. A registration whose <see cref="RecurringJobRegistration.JobType"/> is not
    /// <typeparamref name="TJob"/> fails with <c>core.value.invalid</c>.
    /// </summary>
    public static IServiceCollection AddJob<TJob>(IServiceCollection services, RecurringJobRegistration registration)
        where TJob : class, IRecurringJob
    {
        Require(services, registration);
        if (registration.JobType != typeof(TJob))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        if (Registered<RecurringJobRegistration>(services).Any(existing => existing.Name == registration.Name))
        {
            throw CompositionModules.Duplicate(registration.Name);
        }

        services.AddSingleton(registration);
        services.AddTransient<TJob>();
        return services;
    }

    private static void Require(IServiceCollection services, object registration)
    {
        if (services is null || registration is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }
    }

    private static IEnumerable<T> Registered<T>(IServiceCollection services) =>
        services.Where(descriptor => descriptor.ServiceType == typeof(T) && !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<T>();
}
