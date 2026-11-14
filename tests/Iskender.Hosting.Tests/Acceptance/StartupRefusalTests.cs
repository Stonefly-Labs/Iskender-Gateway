using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Hosting.Testing;
using Iskender.Hosting.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>
/// User Story 2, acceptance scenarios 1 to 4, 6, and 7: a rejected configuration stops the host's start, with no value
/// leaked. One test sets a process variable, so the class runs in <see cref="ProcessEnvironment"/>.
/// </summary>
[Collection(ProcessEnvironment.Name)]
public class StartupRefusalTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>US2-1: a misspelled field fails start, and ISK-0003's events come before Hosting's.</summary>
    [Fact]
    public async Task AMisspelledFieldFailsStartWithTheRejection()
    {
        var logs = new CapturedLog();

        var failure = await StartAsync(Text("{\"iskender\":{\"hosting\":{\"requestTimout\":\"20s\"}}}"), logs: logs);

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        var diagnostics = failure.Error!.Diagnostics!;
        Assert.Contains("config.field.unknown", diagnostics.Text, StringComparison.Ordinal);
        Assert.Contains(diagnostics.Values, pair => pair.Key == "errorCount" && pair.Value == "1");
        Assert.Contains(diagnostics.Values, pair => pair.Key == "reportedCount" && pair.Value == "1");
        Assert.Equal([3002, 3003, 6002], logs.Entries.Select(entry => entry.Event.Id).Where(id => id is 3002 or 3003 or 6002));
    }

    /// <summary>US2-2: a plaintext value in a <c>secretRef</c> field fails start, and the value appears nowhere.</summary>
    [Fact]
    public async Task APlaintextSecretFailsStartAndAppearsNowhere()
    {
        const string Planted = "plaintext-canary-7f3a";
        var logs = new CapturedLog();

        var failure = await StartAsync(
            Text("{\"iskender\":{\"persistence\":{\"host\":\"db.example\",\"pool\":{\"name\":\"main\"},\"password\":\"" + Planted + "\"}}}"),
            services => services.AddTestSchema(),
            logs: logs);

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        Assert.Contains("config.secret.plaintext", failure.Error!.Diagnostics!.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, failure.Error!.Diagnostics!.Text ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(failure.Error!.Diagnostics!.Values, pair => pair.Value.Contains(Planted, StringComparison.Ordinal));
        Assert.All(logs.Entries, entry =>
        {
            Assert.DoesNotContain(Planted, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(entry.Values.Values, value => value?.ToString()?.Contains(Planted, StringComparison.Ordinal) == true);
        });
    }

    /// <summary>US2-3: a valid configuration starts, and 3001 and 6001 are each written once, naming one revision.</summary>
    [Fact]
    public async Task AValidConfigurationStartsAndNamesOneRevision()
    {
        await using var host = await EmbeddedHost.CreateAsync(builder => builder.Services.AddIskender(options => options.BaseDocument = Text("{\"iskender\":{\"hosting\":{\"requestTimeout\":\"20s\"}}}")), ct: Token);

        var revision = host.Services.GetRequiredService<ConfigurationSnapshot>().Revision.Value;
        Assert.Equal(revision, host.Logs.Only(3001).Value("Revision"));
        Assert.Equal(revision, host.Logs.Only(6001).Value("Revision"));
    }

    /// <summary>US2-4: resolving the snapshot before start, with a rejected configuration, fails the same way each time.</summary>
    [Fact]
    public async Task ResolvingTheSnapshotOfARejectedConfigurationFailsEachTime()
    {
        await using var host = await EmbeddedHost.CreateAsync(builder => builder.Services.AddIskender(options => options.BaseDocument = Text("{\"iskender\":{\"hosting\":{\"requestTimeout\":\"0s\"}}}")), start: false, ct: Token);

        var first = Assert.Throws<GatewayException>(() => host.Services.GetRequiredService<ConfigurationSnapshot>());
        var second = Assert.Throws<GatewayException>(() => host.Services.GetRequiredService<ConfigurationSnapshot>());

        Assert.Same(ErrorCodes.HostingConfigurationRejected, first.Code);
        Assert.Same(ErrorCodes.HostingConfigurationRejected, second.Code);
    }

    /// <summary>US2-6: the host's <c>Development</c> environment does not relax Iskender's production-grade default.</summary>
    [Fact]
    public async Task TheHostsDevelopmentEnvironmentDoesNotRelaxIskendersDefaults()
    {
        var failure = await StartAsync(
            Text("{\"iskender\":{\"persistence\":{\"host\":\"db.example\",\"pool\":{\"name\":\"main\"},\"trace\":true}}}"),
            services => services.AddTestSchema(),
            builder => builder.Environment.EnvironmentName = "Development");

        Assert.Same(ErrorCodes.HostingConfigurationRejected, failure.Code);
        Assert.Contains("config.value.unsafe_for_environment", failure.Error!.Diagnostics!.Text, StringComparison.Ordinal);
    }

    /// <summary>US2-7: without the opt-in, a planted <c>ISKENDER__</c> variable is not applied.</summary>
    [Fact]
    public async Task APlantedVariableIsIgnoredWithoutTheOptIn()
    {
        using var planted = ProcessEnvironment.Set(new Dictionary<string, string>(StringComparer.Ordinal) { ["ISKENDER__HOSTING__REQUESTTIMEOUT"] = "7s" });

        await using var host = await EmbeddedHost.CreateAsync(builder => builder.Services.AddIskender(_ => { }), ct: Token);

        var snapshot = host.Services.GetRequiredService<ConfigurationSnapshot>();
        Assert.Equal(0, snapshot.Layers.EnvironmentVariableCount);
        Assert.Equal(TimeSpan.FromSeconds(100), snapshot.GetSection(Iskender.Hosting.Configuration.HostingSectionSchema.Schema).GetDuration("requestTimeout"));
    }

    private static async Task<GatewayException> StartAsync(
        IskenderConfigurationDocument document,
        Action<IServiceCollection>? services = null,
        Action<WebApplicationBuilder>? host = null,
        CapturedLog? logs = null) =>
        await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(
            builder =>
            {
                host?.Invoke(builder);
                if (logs is not null)
                {
                    builder.Logging.AddProvider(logs);
                }

                builder.Services.AddIskender(options => options.BaseDocument = document);
                services?.Invoke(builder.Services);
            },
            ct: Token));

    private static IskenderConfigurationDocument Text(string json) => IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, json);
}
