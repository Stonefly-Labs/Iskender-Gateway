using Iskender.Domain.Ports;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Iskender.Management;
using Iskender.Management.Migrations;

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
