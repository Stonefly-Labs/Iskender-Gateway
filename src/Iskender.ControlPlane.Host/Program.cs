using Iskender.Domain.Ports;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Iskender.Management;
using Iskender.Management.Migrations;
using Microsoft.Extensions.DependencyInjection;

// The control plane's entry point (container contract, Entry points rules 1 and 2): the registrations TR-5 lists, with the
// outbox (ISK-0009), no traffic listener, and the migrate command (Migration command rules 1 to 7); and the worker, served by
// the work command with its own root, the same adapters, and the dispatcher (ISK-0009 research R11). Owned by ISK-0007.
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
        Adapters(services);
    },
    null,
    new Dictionary<string, ContainerCommand> { ["migrate"] = MigrateAsync })
{
    ServingCommands = new Dictionary<string, ContainerServingCommand>
    {
        ["work"] = new(
            CompositionPlane.Worker,
            "iskender-worker",
            (services, sources) =>
            {
                services.AddIskenderWorker(options =>
                {
                    options.BaseDocument = sources.BaseDocument;
                    options.OverlayDocument = sources.OverlayDocument;
                    options.ReadEnvironmentVariables = sources.ReadEnvironmentVariables;
                    options.EnvironmentName = sources.EnvironmentName;
                });
                Adapters(services);
                services.AddIskenderPostgreSqlWorkDispatcher();
            },
            PostgreSqlServiceCollectionExtensions.CheckWorkSettings),
    },
};

return await ContainerEntryPoint.RunAsync(args, definition);

// The adapters the serving control plane and the worker both compose, after their roots (Entry points rule 2).
static void Adapters(IServiceCollection services)
{
    services.AddIskenderContainer();
    services.AddIskenderCoordination();
    services.AddIskenderPostgreSqlPersistence();
    services.AddIskenderPostgreSqlCoordination();
    services.AddIskenderRedisCoordination();
    services.AddIskenderPostgreSqlWork();
}

// Parses before reading any configuration, then hands the library a way to compose the serving plane's registrations; the
// library starts nothing, runs one operation, and writes event 7006 for every run whose arguments parse (Migration command
// rule 5, FR-025).
static async ValueTask<int> MigrateAsync(IReadOnlyList<string> args, ContainerCommandContext context, CancellationToken cancellationToken)
{
    if (MigrationArguments.Parse(args) is not { } operation)
    {
        await context.StandardError.WriteLineAsync("usage: migrate apply|migrate reverse [--steps <n>] [--confirm-data-loss]|migrate status");
        return ExitCodes.Usage;
    }

    var outcome = await MigrationCommand.RunAsync(context.BuildServices, operation, context.LoggerFactory, context.StandardError, cancellationToken);
    return outcome.ExitCode;
}
