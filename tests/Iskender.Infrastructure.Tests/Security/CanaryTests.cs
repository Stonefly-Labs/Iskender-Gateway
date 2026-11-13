using Iskender.Coordination.Conformance;
using Iskender.Coordination.Conformance.Security;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Tests.Security;

/// <summary>
/// The canary check against the local substitutes (SC-005, FR-004, FR-026): with the canary as the organization ID and a
/// cached value, every port operation, a lease loss, a capability refusal, an oversized value, the health probe, and the
/// single-instance guard report it 0 times in results, <c>ToString()</c> output, exceptions and their diagnostics, log
/// messages and fields, metric labels, and activity tags.
/// </summary>
public class CanaryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheCanaryNeverAppearsInWhatTheLocalSubstitutesReport()
    {
        var coordinator = new LocalCoordinatorUnderTest();
        var client = coordinator.CreateClient(0);
        var scopes = CanaryDriver.Scopes();
        var scope = scopes.OrganizationA;
        var duration = coordinator.Settings.MinimumLeaseDuration;

        await CanaryDriver.DriveAsync(client, scope, duration, Token);

        var lease = (await client.Leases.AcquireAsync(scope, new CoordinationName(CanaryDriver.Module, "lost"), duration, LeaseWait.TryOnly, Token)).Lease!;
        await coordinator.PassTimeAsync(duration + TimeSpan.FromMilliseconds(1));
        Assert.Same(ErrorCodes.CoordinationLeaseLost, (await CanaryDriver.FailAsync(async () => await client.Leases.RenewAsync(lease, duration, Token))).Code);
        Assert.Same(ErrorCodes.CoordinationCapabilityUnsupported, (await CanaryDriver.RefuseAsync(client, scope, Token)).Code);
        var miss = await client.Cache.TryGetAsync(scope, new CacheNamespace(CanaryDriver.Module, "entries"), "large", 1, Token);
        Assert.Same(ErrorCodes.ValueInvalid, (await CanaryDriver.FailAsync(async () =>
            await client.Cache.SetAsync(scope, new CacheNamespace(CanaryDriver.Module, "entries"), "large", 1, new byte[coordinator.Settings.MaximumValueBytes + 1], TimeSpan.FromMinutes(1), miss.Stamp, Token))).Code);

        var probe = new CoordinationHealthProbe(coordinator.CoordinationSettings, postgreSqlConfigured: false, redis: null, postgreSql: null, coordinator.Logger);
        CapturingDiagnostics.Shared.Report(await probe.CheckAsync(Token));
        await using (var guard = new SingleInstanceGuard(null, coordinator.CoordinationSettings.LeaseCoordinator, SingleInstancePlane.DataPlane, coordinator.CoordinationSettings.InstanceLeaseDuration, coordinator.Logger))
        {
            await guard.StartAsync(Token);
            CapturingDiagnostics.Shared.Report(guard.State);
        }

        // A positive control: the substitutes did log, and the canary really was the scope's organization.
        Assert.NotEmpty(coordinator.Logger.Events);
        Assert.Contains(Canary.Value, scopes.OrganizationAScope.Organization.Value, StringComparison.Ordinal);
        Assert.Empty(CapturingDiagnostics.Shared.CanaryHits());
    }
}
