using Iskender.Domain.Ports;
using Iskender.Infrastructure.Container;
using Iskender.Infrastructure.Container.Diagnostics;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Iskender.Management;
using Iskender.Management.Bootstrap;
using Iskender.Management.Migrations;
using Iskender.Management.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

// The control plane's entry point (container contract, Entry points rules 1 and 2): the registrations TR-5 lists, with the
// outbox (ISK-0009), and the migrate command (Migration command rules 1 to 7); and the worker, served by the work command
// with its own root, the same adapters, and the dispatcher (ISK-0009 research R11). ISK-0010 adds the operator security
// and PostgreSQL identity registrations to the serving plane, migrate, and the new bootstrap command (Bootstrap command),
// but not to the worker, and maps the admin routes under /admin/v1 on the control plane's traffic listener, its only
// routes there (Listeners rule 5). Owned by ISK-0007.
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
        services.AddIskenderPostgreSqlIdentity();
        services.AddIskenderOperatorSecurity();
    },
    app => app.MapIskenderOperatorSecurity("/admin/v1"),
    new Dictionary<string, ContainerCommand> { ["migrate"] = MigrateAsync, ["bootstrap"] = BootstrapAsync })
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

// Parses before reading any configuration, then runs one issue through the serving plane's registrations, under the runtime
// login, starting nothing; writes event 7008 for every run whose arguments parse (Bootstrap command rules 2 to 6).
static async ValueTask<int> BootstrapAsync(IReadOnlyList<string> args, ContainerCommandContext context, CancellationToken cancellationToken)
{
    if (BootstrapArguments.Parse(args) is not { } operation)
    {
        await context.StandardError.WriteLineAsync("usage: bootstrap issue|bootstrap recover");
        return ExitCodes.Usage;
    }

    var outcome = await BootstrapCommand.RunAsync(context.BuildServices, operation, context.LoggerFactory, context.StandardOutput, context.StandardError, cancellationToken);
    ContainerLog.LogBootstrapFinished(context.LoggerFactory.CreateLogger(ContainerLog.Category), operation.ToName(), outcome.Outcome, outcome.Code?.Id);
    return outcome.ExitCode;
}
