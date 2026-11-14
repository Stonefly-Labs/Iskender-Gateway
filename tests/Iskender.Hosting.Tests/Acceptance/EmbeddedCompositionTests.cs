using System.Diagnostics;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Hosting.Configuration;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Coordination;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>User Story 1, acceptance scenarios 1, 3, and 4, and SC-002: an embedding host resolves Iskender's modules in process.</summary>
public class EmbeddedCompositionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>US1-1: the sample starts in process and every composed module's services resolve from its container.</summary>
    [Fact]
    public async Task TheSampleStartsInProcessAndResolvesEveryComposedService()
    {
        var children = ChildProcesses();

        await StartSampleAndResolveAsync();

        Assert.Equal(children, ChildProcesses());
    }

    /// <summary>SC-002: US1-1 succeeds 20 consecutive times, each with a fresh host.</summary>
    [Fact]
    public async Task TheSampleStartsTwentyConsecutiveTimes()
    {
        var succeeded = 0;
        for (var run = 0; run < 20; run++)
        {
            await StartSampleAndResolveAsync();
            succeeded++;
        }

        Assert.Equal(20, succeeded);
    }

    /// <summary>US1-3: with <c>AddIskender(...)</c> alone, only the <c>hosting</c> module is composed.</summary>
    [Fact]
    public async Task AHostWithOnlyAddIskenderComposesOnlyHosting()
    {
        await using var host = await EmbeddedHost.CreateAsync(builder => builder.Services.AddIskender(options => options.BaseDocument = Timeout("20s")), ct: Token);

        Assert.Equal("hosting", host.Logs.Only(6001).Value("Modules"));
        Assert.Equal(["hosting"], host.Services.GetServices<CompositionModule>().Select(module => module.Name));
        var registered = host.Services.GetRequiredService<IServiceProviderIsService>();
        Assert.False(registered.IsService(typeof(IUnitOfWorkFactory)));
        Assert.False(registered.IsService(typeof(ILeaseCoordinator)));
        Assert.False(registered.IsService(typeof(CoordinationGate)));
    }

    /// <summary>US1-4: two hosts in one process each see their own configuration, and disposing one leaves the other working.</summary>
    [Fact]
    public async Task TwoHostsInOneProcessKeepTheirOwnConfiguration()
    {
        var first = await EmbeddedHost.CreateAsync(builder => Compose(builder, "20s"), ct: Token);
        await using var second = await EmbeddedHost.CreateAsync(builder => Compose(builder, "40s"), ct: Token);

        Assert.Equal(TimeSpan.FromSeconds(20), first.Services.GetRequiredService<HostingSettings>().RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(40), second.Services.GetRequiredService<HostingSettings>().RequestTimeout);
        var secondRevision = second.Services.GetRequiredService<ConfigurationRevision>();
        Assert.NotEqual(first.Services.GetRequiredService<ConfigurationRevision>(), secondRevision);
        Assert.NotSame(first.Services.GetRequiredService<CoordinationGate>(), second.Services.GetRequiredService<CoordinationGate>());

        await first.StopAsync(Token);
        await first.DisposeAsync();

        Assert.Equal(secondRevision, second.Services.GetRequiredService<ConfigurationRevision>());
        await using var scope = second.Services.CreateAsyncScope();
        Assert.Same(second.Services.GetRequiredService<ConfigurationSnapshot>(), scope.ServiceProvider.GetRequiredService<ConfigurationSnapshot>());
        Assert.NotNull(second.Services.GetRequiredService<CoordinationGate>());
    }

    private static async Task StartSampleAndResolveAsync()
    {
        await using var host = await EmbeddedHost.FromSampleAsync(ct: Token);
        var services = host.Services;

        var snapshot = services.GetRequiredService<ConfigurationSnapshot>();
        Assert.Equal(snapshot.Revision, services.GetRequiredService<ConfigurationRevision>());
        Assert.Equal(["coordination", "hosting"], services.GetRequiredService<ConfigurationSchemaRegistry>().Schemas.Select(schema => schema.Namespace).Order(StringComparer.Ordinal));
        Assert.NotNull(services.GetRequiredService<CoordinationSettings>());
        Assert.NotNull(services.GetRequiredService<LocalAtomicOperations>());
        Assert.NotNull(services.GetRequiredService<LocalLeaseCoordinator>());
        Assert.NotNull(services.GetRequiredService<LocalVersionedCache>());
        Assert.Same(services.GetRequiredService<CoordinationGate>().Report, services.GetRequiredService<CapabilityReport>());
        Assert.NotNull(services.GetRequiredService<ISingleInstanceStatus>());
        Assert.NotNull(services.GetRequiredService<ICoordinationHealthProbe>());
        Assert.IsType<LocalAtomicOperations>(services.GetRequiredService<IAtomicOperations>());
        Assert.IsType<LocalLeaseCoordinator>(services.GetRequiredService<ILeaseCoordinator>());
        Assert.IsType<LocalVersionedCache>(services.GetRequiredService<IVersionedCache>());
        Assert.IsType<TestServer>(host.Server);
    }

    private static void Compose(Microsoft.AspNetCore.Builder.WebApplicationBuilder builder, string timeout)
    {
        builder.Services.AddIskender(options => options.BaseDocument = Timeout(timeout));
        builder.Services.AddIskenderCoordination();
    }

    private static IskenderConfigurationDocument Timeout(string timeout) =>
        IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, "{\"iskender\":{\"hosting\":{\"requestTimeout\":\"" + timeout + "\"}}}");

    // The direct children of this test process, by ID. pgrep excludes itself from its own output.
    private static List<string> ChildProcesses()
    {
        using var pgrep = Process.Start(new ProcessStartInfo("pgrep", ["-P", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var output = pgrep.StandardOutput.ReadToEnd();
        pgrep.WaitForExit();
        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal)];
    }
}
