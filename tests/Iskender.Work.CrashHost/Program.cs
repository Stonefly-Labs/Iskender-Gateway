using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.PostgreSql.Coordination;
using Iskender.Infrastructure.PostgreSql.Directory;
using Iskender.Infrastructure.PostgreSql.Work;
using Iskender.Work.CrashHost;
using Iskender.Work.Tests.Fixture;
using Npgsql;

// The crash host (ISK-0009 T024, research R18, FR-040). Arguments: a connection string, a comma-separated scope set of
// directory scope texts, an injection point, and optionally the maximum attempts. It runs one dispatcher, prints one fixed
// line on reaching each point, holds at the named point, and otherwise runs until killed. The point AfterProducerCommit
// instead makes it a producer: one committed item per scope, then a second item per scope left uncommitted. It prints no
// connection string, scope, item ID, or key.
if (args.Length is < 3 or > 4)
{
    Console.Error.WriteLine("usage: Iskender.Work.CrashHost <connection string> <scopes> <point> [maximum attempts]");
    return 2;
}

var connection = new NpgsqlConnectionStringBuilder(args[0]);
var scopes = new List<UnitOfWorkScope>();
foreach (var listed in args[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
{
    if (!DirectoryScopeResolver.TryResolve(listed, out var scope))
    {
        Console.Error.WriteLine("malformed scope");
        return 2;
    }

    scopes.Add(scope);
}

var point = args[2];
var maximumAttempts = args.Length == 4 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 8;
var producing = point == CrashLines.ProducerPoint;
var held = producing || point == CrashLines.NoPoint ? null : (WorkInjectionPoint?)Enum.Parse<WorkInjectionPoint>(point);

var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
    ConfigurationSchemaRegistry.Create([PostgreSqlSectionSchema.Schema, WorkSectionSchema.Schema, CoordinationSectionSchema.Schema]),
    ConfigurationDocumentSource.FromString("crash.json", ConfigurationDocumentFormat.Json, $$"""
        {"iskender":{
          "persistence":{"host":"{{connection.Host}}","port":{{connection.Port}},"database":"{{connection.Database}}","username":"{{connection.Username}}","password":{"secretRef":"crash/runtime"},"tlsMode":"disable"},
          "work":{"pollInterval":"1s","leaseDuration":"5s","retry":{"initialDelay":"100ms","maximumDelay":"1s","maximumAttempts":{{maximumAttempts}} } },
          "coordination":{} } }
        """),
    environmentName: "development"));
if (!report.IsValid)
{
    Console.Error.WriteLine("invalid configuration");
    return 2;
}

var snapshot = report.Snapshot!;
await using var sources = new PostgreSqlDataSources(PostgreSqlSettings.From(snapshot.GetSection(PostgreSqlSectionSchema.Schema)), new CrashCredentials(connection.Password ?? ""));
var units = new PostgreSqlUnitOfWorkFactory(sources, TimeProvider.System, logger: null);
var coordination = CoordinationSettings.From(snapshot);
var settings = WorkSettings.From(snapshot, coordination);
using var dispatcher = new WorkDispatcher(
    units,
    new PostgreSqlFencingGuard(),
    new ShardLeases(new PostgreSqlLeaseCoordinator(units, coordination), settings.LeaseDuration, timeProvider: null),
    settings,
    [ApplyRecordingHandler.Registration(), FailOnceHandler.Registration(), CrashHandler.Registration()],
    type => type == typeof(CrashHandler) ? new CrashHandler() : (IWorkHandler)Activator.CreateInstance(type)!,
    new CrashInjection(held),
    logger: null,
    timeProvider: null);

var running = dispatcher.RunAsync(CancellationToken.None, CancellationToken.None, CancellationToken.None);
if (producing)
{
    var outbox = new PostgreSqlWorkOutbox([new WorkKindDeclaration(TestKinds.Record, 1)], logger: null);
    var uncommitted = new List<IUnitOfWork>();
    foreach (var scope in scopes)
    {
        await using (var unit = await units.OpenAsync(scope, new UnitOfWorkOptions("applies", "crash.produce"), CancellationToken.None))
        {
            await outbox.EnqueueAsync(unit, new WorkItemRequest(scope.Owner, TestKinds.Record, 1, new byte[] { 1 }, Guid.NewGuid().ToString("N")), CancellationToken.None);
            await unit.CommitAsync(CancellationToken.None);
        }

        var open = await units.OpenAsync(scope, new UnitOfWorkOptions("applies", "crash.produce"), CancellationToken.None);
        await outbox.EnqueueAsync(open, new WorkItemRequest(scope.Owner, TestKinds.Record, 1, new byte[] { 2 }, Guid.NewGuid().ToString("N")), CancellationToken.None);
        uncommitted.Add(open);
    }

    CrashLines.Write(CrashLines.ProducerPoint);
    CrashLines.Write(CrashLines.UncommittedLine);
}

await running;
return 0;
