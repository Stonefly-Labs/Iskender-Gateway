using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Hosting.Testing;
using Iskender.Management;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Hosting.Tests.Composition;

/// <summary>
/// The worker's composition root (ISK-0009 T046, hosting contract Ownership rule 2, Registration rules 1, 6, and 8): it
/// composes plane <c>Worker</c> with module <c>worker</c> and no section, a second root of any plane on one collection
/// fails with <c>hosting.registration_duplicate</c>, and a <c>work</c> section is unknown without the dispatcher.
/// </summary>
public class WorkerRootTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Pairings()
    {
        var data = new TheoryData<string, string>();
        foreach (var first in Roots)
        {
            foreach (var second in Roots)
            {
                data.Add(first, second);
            }
        }

        return data;
    }

    private static readonly string[] Roots = ["data_plane", "control_plane", "worker"];

    [Fact]
    public async Task TheRootComposesTheWorkerPlane()
    {
        await using var host = await EmbeddedHost.CreateAsync(builder => builder.Services.AddIskenderWorker(options => options.EnvironmentName = "development"), ct: Token);

        Assert.Equal(new CompositionRoot(CompositionPlane.Worker, WorkerServiceCollectionExtensions.WorkerModule), host.Services.GetRequiredService<CompositionRoot>());
        Assert.Equal("worker", WorkerServiceCollectionExtensions.WorkerModule);
    }

    [Theory]
    [MemberData(nameof(Pairings))]
    public void ASecondRootOfAnyPlaneFailsWithRegistrationDuplicate(string first, string second)
    {
        var services = new ServiceCollection();
        Add(services, first);
        var before = services.Count;

        var failure = Assert.Throws<GatewayException>(() => Add(services, second));

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, failure.Code);
        Assert.Contains(new KeyValuePair<string, string>("module", Module(second)), failure.Error!.Diagnostics!.Values);
        Assert.Equal(before, services.Count);
    }

    [Fact]
    public void AWorkerRootWithANullArgumentFailsWithValueInvalid()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ((IServiceCollection)null!).AddIskenderWorker(_ => { })).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new ServiceCollection().AddIskenderWorker(null!)).Code);
    }

    [Fact]
    public async Task ALaterChangeToTheOptionsDoesNotReachTheSources()
    {
        var services = new ServiceCollection();
        var document = CompositionDocument.FromText("iskender.json", ConfigurationDocumentFormat.Json, "{}");
        WorkerOptions? kept = null;
        services.AddIskenderWorker(options =>
        {
            options.BaseDocument = document;
            options.EnvironmentName = "development";
            kept = options;
        });

        kept!.BaseDocument = CompositionDocument.FromText("other.json", ConfigurationDocumentFormat.Json, "{}");
        kept.OverlayDocument = document;
        kept.ReadEnvironmentVariables = true;
        kept.EnvironmentName = "production";
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Assert.Equal(new CompositionSources(document, null, false, "development"), provider.GetRequiredService<CompositionSources>());
    }

    [Fact]
    public async Task AWorkSectionIsUnknownWhenTheDispatcherIsNotComposed()
    {
        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder => builder.Services.AddIskenderWorker(options =>
            {
                options.EnvironmentName = "development";
                options.BaseDocument = CompositionDocument.FromText("iskender.json", ConfigurationDocumentFormat.Json, "{\"iskender\":{\"work\":{}}}");
            }),
            ct: Token));

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        Assert.Contains(ErrorCodes.ConfigNamespaceUnknown.Id, failure.Error!.Diagnostics!.Text, StringComparison.Ordinal);
    }

    private static void Add(IServiceCollection services, string root)
    {
        switch (root)
        {
            case "data_plane":
                services.AddIskender(_ => { });
                break;
            case "control_plane":
                services.AddIskenderControlPlane(_ => { });
                break;
            default:
                services.AddIskenderWorker(_ => { });
                break;
        }
    }

    private static string Module(string root) => root switch
    {
        "data_plane" => "hosting",
        "control_plane" => ControlPlaneServiceCollectionExtensions.ControlPlaneModule,
        _ => WorkerServiceCollectionExtensions.WorkerModule,
    };
}
