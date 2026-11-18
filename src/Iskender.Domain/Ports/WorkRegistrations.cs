using System.Globalization;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
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
            throw Duplicate(declaration.Kind.ToString());
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
            throw Duplicate(registration.Kind.ToString());
        }

        services.AddSingleton(registration);
        services.AddTransient<THandler>();
        return services;
    }

    /// <summary>
    /// Registers a recurring job, and its feature <c>work.job.&lt;name&gt;</c> of module <c>work</c>, needing strict
    /// leases and fencing on a mandatory path that fails closed (work contract Leases and fencing rule 1), so a job
    /// registered after the dispatcher has its requirement too. A registration whose
    /// <see cref="RecurringJobRegistration.JobType"/> is not <typeparamref name="TJob"/> fails with
    /// <c>core.value.invalid</c>.
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
            throw Duplicate(registration.Name);
        }

        services.AddSingleton(registration);
        services.AddSingleton(new FeatureRequirement(
            "work.job." + registration.Name,
            "work",
            new Dictionary<CoordinationPrimitive, ConsistencyLevel> { [CoordinationPrimitive.Leases] = ConsistencyLevel.Strict, [CoordinationPrimitive.Fencing] = ConsistencyLevel.Strict },
            mandatoryPath: true,
            OutageBehavior.FailClosed));
        services.AddTransient<TJob>();
        return services;
    }

    // The failure CompositionModules raises for a duplicate module, built here because the shared namespace may use no
    // module namespace (architecture rule A18).
    private static GatewayException Duplicate(string name) =>
        new(GatewayError.Create(
            ErrorCodes.HostingRegistrationDuplicate,
            RequestCorrelationId.New(),
            OutputVisibility.None,
            diagnostics: new ErrorDiagnostics(
                string.Create(CultureInfo.InvariantCulture, $"{ErrorCodes.HostingRegistrationDuplicate.Id}: module {name}"),
                [new("module", name)])));

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
