using System.Collections.Concurrent;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Tests.Configuration;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Composition;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Iskender.Infrastructure.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>
/// User Story 5, acceptance scenarios 1 to 6, and every row of the hosting contract's Registration mistakes table. Every
/// host is built with <c>ValidateScopes</c> and <c>ValidateOnBuild</c> on, so each mistake surfaces as its code at start
/// or at the call, never as a container error at build (Lifetimes rule 2).
/// </summary>
public class RegistrationMistakesTests
{
    private const string Persistence = """
        "persistence":{"host":"iskender-database.invalid","database":"iskender","username":"gateway","password":{"secretRef":"database/runtime"}}
        """;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// The module registrations, each by name, with what its duplicate's diagnostics name: the module, or, for a
    /// coordinator's module, whose kind is checked first, the coordinator kind.
    /// </summary>
    public static TheoryData<string, string> ModuleRegistrations => new()
    {
        { "coordination", "module coordination" },
        { "persistence-postgresql", "module persistence-postgresql" },
        { "coordination-postgresql", "coordinator postgresql" },
        { "coordination-redis", "coordinator redis" },
    };

    /// <summary>US5-1: a second <c>AddIskender(...)</c> fails at the call, before any logger exists, so no event is written.</summary>
    [Fact]
    public async Task AddIskenderTwiceFailsAtTheSecondCallWithNoEvent()
    {
        GatewayException? failure = null;

        await using var host = await EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Services.AddIskender(_ => { });
                var before = builder.Services.Count;
                failure = Assert.Throws<GatewayException>(() => builder.Services.AddIskender(_ => { }));
                Assert.Equal(before, builder.Services.Count);
            },
            start: false,
            ct: Token);

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, failure!.Code);
        Assert.Contains(new KeyValuePair<string, string>("module", "hosting"), failure.Error!.Diagnostics!.Values);
        Assert.DoesNotContain(host.Logs.Entries, entry => entry.Event.Id is >= 6001 and <= 6003);
    }

    /// <summary>US5-3: each module registration called twice fails at the second call, naming the module.</summary>
    [Theory]
    [MemberData(nameof(ModuleRegistrations))]
    public async Task AModuleRegistrationTwiceFailsAtTheSecondCall(string module, string named)
    {
        GatewayException? failure = null;

        await using var host = await EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Services.AddIskender(_ => { });
                Register(builder.Services, module);
                var before = builder.Services.Count;
                failure = Assert.Throws<GatewayException>(() => Register(builder.Services, module));
                Assert.Equal(before, builder.Services.Count);
            },
            start: false,
            ct: Token);

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, failure!.Code);
        Assert.Equal($"{ErrorCodes.HostingRegistrationDuplicate.Id}: {named}", failure.Error!.Diagnostics!.Text);
        Assert.DoesNotContain(host.Logs.Entries, entry => entry.Event.Id is >= 6001 and <= 6003);
    }

    /// <summary>US5-3: two coordinator registrations of one kind fail at the second, naming the kind.</summary>
    [Fact]
    public void TwoCoordinatorRegistrationsOfOneKindFailAtTheSecond()
    {
        var services = new ServiceCollection();
        services.AddIskender(_ => { });
        services.AddIskenderCoordination();
        CoordinatorRegistration.Add(services, Fake(CoordinatorKind.Redis));
        var before = services.Count;

        var failure = Assert.Throws<GatewayException>(() => CoordinatorRegistration.Add(services, Fake(CoordinatorKind.Redis)));

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, failure.Code);
        Assert.Contains(new KeyValuePair<string, string>("coordinator", "redis"), failure.Error!.Diagnostics!.Values);
        Assert.Equal(before, services.Count);
        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, Assert.Throws<GatewayException>(services.AddIskenderRedisCoordination).Code);
    }

    /// <summary>US5-2: a module registration without <c>AddIskender(...)</c> fails at host start, with event 6003.</summary>
    [Fact]
    public async Task AModuleWithoutAddIskenderFailsAtHostStart()
    {
        var logs = new CapturedLog();

        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Logging.AddProvider(logs);
                builder.Services.AddIskenderCoordination();
            },
            ct: Token));

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        AssertMissing(failure, logs, "hosting", "coordination");
    }

    /// <summary>US5-2: <c>MapIskender(...)</c> without <c>AddIskender(...)</c> fails at the call, with event 6003.</summary>
    [Fact]
    public async Task MapIskenderWithoutAddIskenderFailsAtTheCall()
    {
        var logs = new CapturedLog();
        var mapped = false;

        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder => builder.Logging.AddProvider(logs),
            application =>
            {
                mapped = true;
                application.MapIskender("/iskender");
            },
            ct: Token));

        Assert.True(mapped);
        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        AssertMissing(failure, logs, "hosting", "MapIskender");
    }

    /// <summary>US5-5: the PostgreSQL coordinator without PostgreSQL persistence fails at start, before validation.</summary>
    [Fact]
    public async Task ThePostgreSqlCoordinatorWithoutPersistenceFailsBeforeValidation()
    {
        var logs = new CapturedLog();

        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Logging.AddProvider(logs);
                builder.Services.AddIskender(_ => { });
                builder.Services.AddIskenderCoordination();
                builder.Services.AddIskenderPostgreSqlCoordination();
            },
            ct: Token));

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        AssertMissing(failure, logs, "persistence-postgresql", "coordination-postgresql");
        Assert.DoesNotContain(logs.Entries, entry => entry.Event.Id is >= 3001 and <= 3003);
    }

    /// <summary>
    /// US5-4, and the edge case of PostgreSQL selected with persistence but no PostgreSQL coordinator: a coordinator the
    /// configuration selects with no registration fails at start, after validation, naming the kind and primitive only.
    /// </summary>
    [Theory]
    [InlineData("redis", false)]
    [InlineData("postgresql", true)]
    public async Task ASelectedCoordinatorWithNoRegistrationFailsAfterValidation(string coordinator, bool persistence)
    {
        var logs = new CapturedLog();
        var time = new TimerCountingTime();
        var document = "{\"iskender\":{" + (persistence ? Persistence + "," : string.Empty)
            + "\"coordination\":{\"topology\":\"single_instance\",\"leases\":{\"coordinator\":\"" + coordinator + "\"}}}}";

        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Logging.AddProvider(logs);
                builder.Services.AddSingleton<TimeProvider>(time);
                builder.Services.AddIskender(options => options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, document));
                builder.Services.AddIskenderCoordination();
                if (persistence)
                {
                    builder.Services.AddIskenderPostgreSqlPersistence();
                }
            },
            ct: Token));

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        AssertMissing(failure, logs, coordinator + " coordinator for leases", "configuration");
        Assert.Single(logs.ById(3001));
        Assert.Single(logs.ById(6001));
        Assert.Empty(logs.ById(5001));
        Assert.Empty(logs.ById(5002));
        Assert.Equal(0, time.Timers);
        foreach (var value in new[] { "iskender-database.invalid", "database/runtime", "gateway" })
        {
            Assert.DoesNotContain(value, failure.Error!.Diagnostics!.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(value, failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(value, logs.Only(6003).Message, StringComparison.Ordinal);
        }
    }

    /// <summary>US5-6: PostgreSQL persistence alone composes no coordination service or schema.</summary>
    [Fact]
    public async Task PersistenceAloneComposesNoCoordination()
    {
        await using var host = await EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Services.AddIskender(options => options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, "{\"iskender\":{" + Persistence + "}}"));
                builder.Services.AddIskenderPostgreSqlPersistence();
            },
            ct: Token);

        var registered = host.Services.GetRequiredService<IServiceProviderIsService>();
        Assert.Equal(["hosting", "persistence"], host.Services.GetRequiredService<ConfigurationSchemaRegistry>().Schemas.Select(schema => schema.Namespace).Order(StringComparer.Ordinal));
        Assert.Equal("hosting,persistence-postgresql", host.Logs.Only(6001).Value("Modules"));
        foreach (var type in new[] { typeof(CoordinationSettings), typeof(CoordinationGate), typeof(ILeaseCoordinator), typeof(IAtomicOperations), typeof(IVersionedCache), typeof(ISingleInstanceStatus), typeof(CoordinatorRegistration) })
        {
            Assert.False(registered.IsService(type), type.Name);
        }
    }

    /// <summary>Two modules claiming one section namespace fail at host start.</summary>
    [Fact]
    public async Task TwoModulesClaimingOneNamespaceFailAtStart()
    {
        var schema = TestSchemas.Registry().Schemas[0];

        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                builder.Services.AddIskender(_ => { });
                CompositionRegistration.Add(builder.Services, new CompositionModule("test-first", [schema], ["hosting"]));
                CompositionRegistration.Add(builder.Services, new CompositionModule("test-second", [schema], ["hosting"]));
            },
            ct: Token));

        Assert.Same(ErrorCodes.ConfigSchemaDuplicateNamespace, failure.Code);
    }

    /// <summary>SC-007: every <c>hosting.*</c> code is produced by a mistake in this suite.</summary>
    [Fact]
    public async Task EveryHostingCodeIsProducedByAMistake()
    {
        var producers = new Dictionary<string, Func<Task<GatewayException>>>(StringComparer.Ordinal)
        {
            ["hosting.configuration_rejected"] = () => Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
                builder => builder.Services.AddIskender(options => options.BaseDocument = IskenderConfigurationDocument.FromText(
                    "base.json", IskenderDocumentFormat.Json, "{\"iskender\":{\"hosting\":{\"requestTimeout\":\"0s\"}}}")),
                ct: Token)),
            ["hosting.registration_duplicate"] = () => Task.FromResult(Assert.Throws<GatewayException>(() =>
            {
                var services = new ServiceCollection();
                services.AddIskender(_ => { });
                services.AddIskender(_ => { });
            })),
            ["hosting.registration_missing"] = () => Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
                builder => builder.Services.AddIskenderCoordination(),
                ct: Token)),
        };
        var hostingCodes = ErrorCodes.All.Where(code => code.Id.StartsWith("hosting.", StringComparison.Ordinal)).Select(code => code.Id).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(hostingCodes, producers.Keys.Order(StringComparer.Ordinal));
        foreach (var (code, produce) in producers)
        {
            Assert.Equal(code, (await produce()).Code.Id);
        }
    }

    private static void Register(IServiceCollection services, string module)
    {
        switch (module)
        {
            case "coordination":
                services.AddIskenderCoordination();
                break;
            case "persistence-postgresql":
                services.AddIskenderPostgreSqlPersistence();
                break;
            case "coordination-postgresql":
                services.AddIskenderPostgreSqlCoordination();
                break;
            case "coordination-redis":
                services.AddIskenderRedisCoordination();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(module), module, null);
        }
    }

    private static CoordinatorRegistration Fake(CoordinatorKind kind) =>
        new(kind, kind == CoordinatorKind.Redis ? CoordinatorCapabilities.Redis : CoordinatorCapabilities.PostgreSql, _ => throw new InvalidOperationException("Not resolved."));

    private static void AssertMissing(GatewayException failure, CapturedLog logs, string missing, string requiredBy)
    {
        Assert.Contains(new KeyValuePair<string, string>("missing", missing), failure.Error!.Diagnostics!.Values);
        Assert.Contains(new KeyValuePair<string, string>("requiredBy", requiredBy), failure.Error.Diagnostics.Values);
        var entry = logs.Only(6003);
        Assert.Equal("Iskender.Hosting", entry.Category);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(missing, entry.Value("Missing"));
        Assert.Equal(requiredBy, entry.Value("RequiredBy"));
    }

    // Counts the timers created on it, so a test can show nothing was started.
    private sealed class TimerCountingTime : TimeProvider
    {
        private readonly ConcurrentBag<ITimer> _timers = [];

        public int Timers => _timers.Count;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = System.CreateTimer(callback, state, dueTime, period);
            _timers.Add(timer);
            return timer;
        }
    }
}
