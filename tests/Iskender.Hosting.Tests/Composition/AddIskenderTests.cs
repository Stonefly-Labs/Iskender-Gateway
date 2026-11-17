using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Iskender.Hosting.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Iskender.Hosting.Tests.Composition;

/// <summary><c>AddIskender(...)</c>'s registration rules, with no host (hosting contract, Registration rules 1 and 5; FR-011, US2-5).</summary>
public class AddIskenderTests
{
    private static readonly ServiceProviderOptions Validated = new() { ValidateScopes = true, ValidateOnBuild = true };

    [Fact]
    public void ConfigureRunsOnceAndLaterChangesToTheOptionsHaveNoEffect()
    {
        var services = new ServiceCollection();
        var calls = 0;
        IskenderOptions? captured = null;
        var document = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, "{}");

        services.AddIskender(options =>
        {
            calls++;
            captured = options;
            options.BaseDocument = document;
            options.EnvironmentName = "staging";
        });
        captured!.BaseDocument = null;
        captured.EnvironmentName = "production";
        captured.ReadEnvironmentVariables = true;

        using var provider = services.BuildServiceProvider(Validated);
        var runtime = provider.GetRequiredService<CompositionSources>();
        Assert.Equal(1, calls);
        Assert.Same(document.Composition, runtime.BaseDocument);
        Assert.Equal("staging", runtime.EnvironmentName);
        Assert.False(runtime.ReadEnvironmentVariables);
        Assert.Equal("staging", provider.GetRequiredService<ConfigurationSnapshot>().EnvironmentName);
    }

    [Fact]
    public void ItReturnsABuilderOverTheSameCollection()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddIskender(_ => { }).Services);
    }

    [Fact]
    public void RegisteringAndBuildingReadsNoFileCallsNoFactoryAndRunsNoValidation()
    {
        var services = new ServiceCollection();
        var factoryCalls = 0;
        var logs = new CountingLoggerProvider();
        services.AddLogging(logging => logging.AddProvider(logs));

        services.AddIskender(options =>
        {
            options.BaseDocument = IskenderConfigurationDocument.FromFile(Path.Combine(Path.GetTempPath(), $"iskender-{Guid.NewGuid():N}", "absent.yaml"), IskenderDocumentFormat.Yaml);
            options.OverlayDocument = IskenderConfigurationDocument.FromStream("overlay.json", IskenderDocumentFormat.Json, () =>
            {
                factoryCalls++;
                throw new IOException("must not be called");
            });
        });
        using var provider = services.BuildServiceProvider(Validated);
        _ = provider.GetServices<IHostedService>().ToList();

        Assert.Equal(0, factoryCalls);
        Assert.Equal(0, logs.Count);
    }

    [Fact]
    public void ResolvingTheSnapshotValidatesOnceAndEveryServiceSeesIt()
    {
        var services = new ServiceCollection();
        services.AddIskender(options => options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, "{\"iskender\":{\"hosting\":{\"requestTimeout\":\"5s\"}}}"));

        using var provider = services.BuildServiceProvider(Validated);
        var snapshot = provider.GetRequiredService<ConfigurationSnapshot>();

        Assert.Same(snapshot, provider.GetRequiredService<IValidatedConfiguration>().Snapshot);
        Assert.Same(snapshot.Revision, provider.GetRequiredService<ConfigurationRevision>());
        Assert.Equal(TimeSpan.FromSeconds(5), provider.GetRequiredService<HostingSettings>().RequestTimeout);
        Assert.Equal(["hosting"], provider.GetServices<CompositionModule>().Select(module => module.Name));
        Assert.Same(provider.GetRequiredService<CompositionLifecycle>(), Assert.Single(provider.GetServices<IHostedService>()));
        Assert.Same(provider.GetRequiredService<CompositionLifecycle>(), provider.GetRequiredService<IHostedLifecycleService>());
    }

    [Fact]
    public void ASecondCallFailsAndLeavesTheCollectionUnchanged()
    {
        var services = new ServiceCollection();
        services.AddIskender(_ => { });
        var before = services.ToList();
        var calls = 0;

        var failure = Assert.Throws<GatewayException>(() => services.AddIskender(_ => calls++));

        Assert.Same(ErrorCodes.HostingRegistrationDuplicate, failure.Code);
        Assert.Equal(0, calls);
        Assert.Equal(before, services);
    }

    [Fact]
    public void NullArgumentsFail()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ((IServiceCollection)null!).AddIskender(_ => { })).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new ServiceCollection().AddIskender(null!)).Code);
    }

    [Fact]
    public void AModuleClaimingTheHostingNamespaceFailsWhenTheRegistryIsBuilt()
    {
        var services = new ServiceCollection();
        services.AddIskender(_ => { });
        services.AddSingleton(new CompositionModule("rival", [SectionSchema.Create("hosting", "Iskender.Tests", 1)], ["hosting"]));

        using var provider = services.BuildServiceProvider(Validated);

        Assert.Equal("config.schema.duplicate_namespace", Assert.Throws<GatewayException>(() => provider.GetRequiredService<ConfigurationSnapshot>()).Code.Id);
    }

    [Fact]
    public void TwoContainersFromOneCollectionShareNoSnapshot()
    {
        var services = new ServiceCollection();
        services.AddIskender(_ => { });

        using var first = services.BuildServiceProvider(Validated);
        using var second = services.BuildServiceProvider(Validated);

        Assert.NotSame(first.GetRequiredService<ConfigurationSnapshot>(), second.GetRequiredService<ConfigurationSnapshot>());
    }

    private sealed class CountingLoggerProvider : ILoggerProvider, ILogger
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Interlocked.Increment(ref _count);

        public void Dispose()
        {
        }
    }
}
