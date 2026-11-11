using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class PersistenceHealthResultTests
{
    [Theory]
    [InlineData(PersistenceHealthReason.Ok, "ok")]
    [InlineData(PersistenceHealthReason.Unreachable, "unreachable")]
    [InlineData(PersistenceHealthReason.AuthenticationFailed, "authentication_failed")]
    [InlineData(PersistenceHealthReason.Timeout, "timeout")]
    [InlineData(PersistenceHealthReason.SchemaBehind, "schema_behind")]
    [InlineData(PersistenceHealthReason.SchemaAhead, "schema_ahead")]
    [InlineData(PersistenceHealthReason.ChecksumMismatch, "checksum_mismatch")]
    [InlineData(PersistenceHealthReason.MigrationInProgress, "migration_in_progress")]
    [InlineData(PersistenceHealthReason.InsufficientPrivilege, "insufficient_privilege")]
    [InlineData(PersistenceHealthReason.Unexpected, "unexpected")]
    public void EachReasonHasItsSnakeCaseCode(PersistenceHealthReason reason, string code)
    {
        Assert.Equal(code, reason.ToCode());
    }

    [Fact]
    public void ThereAreExactlyTenReasons()
    {
        Assert.Equal(10, Enum.GetValues<PersistenceHealthReason>().Length);
    }

    [Fact]
    public void HealthyPairsOnlyWithOk()
    {
        Assert.Equal(PersistenceHealthStatus.Healthy, PersistenceHealthResult.Healthy.Status);
        Assert.Equal(PersistenceHealthReason.Ok, PersistenceHealthResult.Healthy.Reason);
        Assert.Throws<GatewayException>(() => new PersistenceHealthResult(PersistenceHealthStatus.Unhealthy, PersistenceHealthReason.Ok));
        Assert.Throws<GatewayException>(() => PersistenceHealthResult.Unhealthy(PersistenceHealthReason.Ok));
        foreach (var reason in Enum.GetValues<PersistenceHealthReason>().Where(reason => reason != PersistenceHealthReason.Ok))
        {
            Assert.Throws<GatewayException>(() => new PersistenceHealthResult(PersistenceHealthStatus.Healthy, reason));
            Assert.Equal(reason, PersistenceHealthResult.Unhealthy(reason).Reason);
        }
    }

    [Fact]
    public void AnUndefinedReasonIsRejected()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ((PersistenceHealthReason)42).ToCode()).Code);
        Assert.Throws<GatewayException>(() => PersistenceHealthResult.Unhealthy((PersistenceHealthReason)42));
    }
}
