using System.Net;
using Iskender.Container.Tests.Fixture;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Container;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.Migrations;

/// <summary>
/// User Story 4, acceptance scenario 3, and FR-024: neither plane applies or reverses a migration when it starts, serves,
/// or stops, and both start with a configuration that holds no migration login (container contract, Start-up rule 5).
/// </summary>
public class NoStartupMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Planes() => ["data_plane", "control_plane"];

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task APlaneStartsServesAndStopsWithNoMigrationLoginAndRunsNoMigration(string plane)
    {
        var recording = new RecordingRunner.Counts();
        void Wrap(IServiceCollection services) => RecordingRunner.Decorate(services, recording);
        var definition = plane == "data_plane" ? PlaneDefinitions.DataPlane(Wrap) : PlaneDefinitions.ControlPlane(Wrap);

        // PlaneDefinitions.Persistence names the runtime login only, with no migration section.
        using var host = new ContainerTestHost().WithDocument(sections: PlaneDefinitions.Persistence);
        using var client = new HttpClient();
        var run = await host.StartAsync(definition);
        var (_, body) = await host.WhenStartedAsync(client);
        if (plane == "data_plane")
        {
            using var served = await client.GetAsync($"http://127.0.0.1:{host.TrafficPort}/v1/anything", Token);
            Assert.Equal(HttpStatusCode.NotFound, served.StatusCode);
        }

        host.Stop();

        Assert.Equal(ExitCodes.Success, await run);
        Assert.Equal("started", ContainerTestHost.Reason(body, "lifecycle"));
        Assert.Equal((0, 0), (recording.Applied, recording.Reversed));
    }

    /// <summary>Wraps the registered <see cref="IMigrationRunner"/>, counting <c>ApplyAsync</c> and <c>ReverseAsync</c> calls.</summary>
    private sealed class RecordingRunner(IMigrationRunner inner, RecordingRunner.Counts counts) : IMigrationRunner
    {
        public static void Decorate(IServiceCollection services, Counts counts)
        {
            var registered = services.Last(descriptor => descriptor.ServiceType == typeof(IMigrationRunner));
            services.Remove(registered);
            services.AddSingleton<IMigrationRunner>(provider => new RecordingRunner(
                registered.ImplementationFactory is { } factory ? (IMigrationRunner)factory(provider) : (IMigrationRunner)registered.ImplementationInstance!,
                counts));
        }

        public ValueTask<MigrationRunResult> ApplyAsync(MigrationApplyRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref counts.AppliedCalls);
            return inner.ApplyAsync(request, cancellationToken);
        }

        public ValueTask<MigrationRunResult> ReverseAsync(MigrationReverseRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref counts.ReversedCalls);
            return inner.ReverseAsync(request, cancellationToken);
        }

        public ValueTask<IReadOnlyList<MigrationStatus>> GetStatusAsync(CancellationToken cancellationToken) => inner.GetStatusAsync(cancellationToken);

        public sealed class Counts
        {
            internal int AppliedCalls;

            internal int ReversedCalls;

            public int Applied => Volatile.Read(ref AppliedCalls);

            public int Reversed => Volatile.Read(ref ReversedCalls);
        }
    }
}
