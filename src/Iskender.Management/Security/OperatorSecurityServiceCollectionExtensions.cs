using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Iskender.Management.Security;

/// <summary>Adds the operator-security module to a control plane's service collection (operator-security contract, Composition rule 1). Owned by ISK-0010.</summary>
public static class OperatorSecurityServiceCollectionExtensions
{
    /// <summary>The module <see cref="AddIskenderOperatorSecurity"/> composes.</summary>
    public const string OperatorSecurityModule = "operator-security";

    /// <summary>The PostgreSQL identity module it requires, which <c>AddIskenderPostgreSqlIdentity()</c> composes.</summary>
    public const string IdentityModule = "identity-postgresql";

    /// <summary>
    /// Composes module <c>operator-security</c>, with the <c>operator</c> section, requiring <c>control-plane</c> and
    /// <c>identity-postgresql</c>: the settings, the redemption attempt limiter (a one-minute fixed window with no queue,
    /// one per container, under an Iskender-owned type, so a host's own <c>RateLimiter</c> is untouched), the operator events and telemetry, the admin pipeline, and the route-declaration check. Under
    /// <c>AddIskender(...)</c> or <c>AddIskenderWorker(...)</c> the start fails with <c>hosting.registration_missing</c>. A
    /// <c>null</c> collection fails with <c>core.value.invalid</c>, and a second call with
    /// <c>hosting.registration_duplicate</c>.
    /// </summary>
    public static IServiceCollection AddIskenderOperatorSecurity(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        CompositionServices.AddModule(services, new CompositionModule(
            OperatorSecurityModule, [OperatorSectionSchema.Schema], [ControlPlaneServiceCollectionExtensions.ControlPlaneModule, IdentityModule]));

        services.AddSingleton(provider => OperatorSettings.From(provider.GetRequiredService<IValidatedConfiguration>().Snapshot));
        services.AddSingleton(provider => new OperatorAttemptLimiter(provider.GetRequiredService<OperatorSettings>().AttemptsPerMinute));
        services.AddSingleton(provider => new OperatorLog(provider.GetService<ILoggerFactory>(), provider.GetService<TimeProvider>()));
        services.AddSingleton<OperatorTelemetry>();
        services.AddSingleton<OperatorRoutes>();
        services.AddSingleton(provider => new AdminPipeline(
            provider.GetRequiredService<IInstallationState>(),
            provider.GetRequiredService<IOperatorAuthorization>(),
            provider.GetRequiredService<IOperatorRedemption>(),
            provider.GetRequiredService<OperatorAttemptLimiter>(),
            provider.GetRequiredService<IValidatedConfiguration>().Snapshot.Revision,
            provider.GetRequiredService<OperatorLog>(),
            provider.GetRequiredService<OperatorTelemetry>()));
        services.AddSingleton<OperatorRouteCheck>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<OperatorRouteCheck>());
        services.AddSingleton<IHostedLifecycleService>(provider => provider.GetRequiredService<OperatorRouteCheck>());

        return services;
    }
}
