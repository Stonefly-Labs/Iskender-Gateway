using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Container.Diagnostics;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Iskender.Management;
using Iskender.Management.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The control plane's entry point (container contract, Entry points rules 1 and 2): the registrations TR-5 lists, with no
// traffic listener, and the migrate command (Migration command rules 1 to 7). Owned by ISK-0007.
var definition = new ContainerPlaneDefinition(
    CompositionPlane.ControlPlane,
    "iskender-control-plane",
    (services, sources) =>
    {
        services.AddIskenderControlPlane(options =>
        {
            options.BaseDocument = sources.BaseDocument;
            options.OverlayDocument = sources.OverlayDocument;
            options.ReadEnvironmentVariables = sources.ReadEnvironmentVariables;
            options.EnvironmentName = sources.EnvironmentName;
        });
        services.AddIskenderContainer();
        services.AddIskenderCoordination();
        services.AddIskenderPostgreSqlPersistence();
        services.AddIskenderPostgreSqlCoordination();
        services.AddIskenderRedisCoordination();
    },
    null,
    new Dictionary<string, ContainerCommand> { ["migrate"] = MigrateAsync });

return await ContainerEntryPoint.RunAsync(args, definition);

// Parses before reading any configuration, then composes the serving plane's registrations, starts nothing, and runs one
// operation. Event 7006 follows every run whose arguments parse, after the hosting contract's own events, and no event
// 7001 to 7005 is written (Migration command rule 5).
static async ValueTask<int> MigrateAsync(IReadOnlyList<string> args, ContainerCommandContext context, CancellationToken cancellationToken)
{
    if (MigrationArguments.Parse(args) is not { } operation)
    {
        await context.StandardError.WriteLineAsync("usage: migrate apply|migrate reverse [--steps <n>] [--confirm-data-loss]|migrate status");
        return ExitCodes.Usage;
    }

    MigrationOutcome outcome;
    try
    {
        var services = context.Compose(new ServiceCollection(), context.ResolveSources());
        // The command builds no host, so this container is the only one; the factory gives it the same validation.
        await using var provider = (ServiceProvider)new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }).CreateServiceProvider(services);
        outcome = await MigrationCommand.RunAsync(provider, operation, cancellationToken);
    }
    catch (GatewayException rejected) when (rejected.Code == ErrorCodes.HostingConfigurationRejected)
    {
        outcome = new MigrationOutcome(ExitCodes.ConfigurationRejected, operation.Name, "failed", 0, rejected.Code);
    }

    ContainerLog.LogMigrationFinished(context.LoggerFactory.CreateLogger(ContainerLog.Category), outcome.Operation, outcome.Outcome, outcome.Count);
    return outcome.ExitCode;
}
