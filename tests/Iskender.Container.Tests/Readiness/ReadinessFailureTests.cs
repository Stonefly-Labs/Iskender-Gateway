using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;
using Iskender.Domain.Registry.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Readiness;

/// <summary>
/// FR-016, FR-018, TR-8, and container contract Readiness rules 1, 5, and 6, with Diagnostics rule 8: a read in the
/// evaluator that throws still answers <c>503</c> with the report as <c>application/json</c>, reports the check
/// <c>unexpected</c>, and writes event 7007 with the exception's type name, never its message (CONV-0007-005).
/// </summary>
public class ReadinessFailureTests
{
    private const string Canary = "readiness-failure-canary";

    [Fact]
    public Task AProbeThatCannotBeBuiltReportsItsCheckUnexpected() =>
        AssertUnexpectedAsync("coordination", services => services.AddSingleton<ICoordinationHealthProbe>(_ => throw new InvalidOperationException(Canary)));

    [Fact]
    public Task ASingleInstanceReadThatThrowsReportsItsCheckUnexpected() =>
        AssertUnexpectedAsync("single_instance", services => services.AddSingleton<ISingleInstanceStatus>(new ThrowingStatus()));

    [Fact]
    public Task ACapabilityReadThatThrowsReportsItsCheckUnexpected() =>
        // The composition reads the report once while starting; every read after the start throws.
        AssertUnexpectedAsync("capability", services => services.AddTransient(provider =>
            provider.GetRequiredService<CompositionStatus>().Phase == CompositionPhase.Started
                ? throw new InvalidOperationException(Canary)
                : new CapabilityReport([], CoordinationTopology.SingleInstance, SingleInstanceState.NotApplicable)));

    private static async Task AssertUnexpectedAsync(string check, Action<IServiceCollection> failing)
    {
        using var host = new ContainerTestHost().WithDocument();
        using var client = new HttpClient();
        var run = await host.StartAsync(TestPlanes.ControlPlane(host, failing));

        // ReadyAsync asserts that the answer is application/json, and names it in full when it is not.
        var (status, body) = await host.WhenStartedAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("not_ready", body.GetProperty("status").GetString());
        Assert.Equal(("not_ready", "unexpected"), (ContainerTestHost.Status(body, check), ContainerTestHost.Reason(body, check)));
        var failed = host.Log.ById(7007).Where(entry => entry.Value("Check") == check).ToList();
        Assert.NotEmpty(failed);
        Assert.All(failed, entry => Assert.Equal(nameof(InvalidOperationException), entry.Value("Failure")));
        Assert.DoesNotContain(host.Log.Entries, entry => entry.Message.Contains(Canary, StringComparison.Ordinal));

        host.Stop();
        Assert.Equal(0, await run);
    }

    private sealed class ThrowingStatus : ISingleInstanceStatus
    {
        public SingleInstanceState State => throw new InvalidOperationException(Canary);
    }
}
