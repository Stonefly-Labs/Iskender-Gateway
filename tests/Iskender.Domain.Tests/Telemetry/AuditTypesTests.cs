using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Telemetry;

namespace Iskender.Domain.Tests.Telemetry;

/// <summary>The audit port types of ISK-0010 (data-model "Audit port", T006).</summary>
public class AuditTypesTests
{
    private static readonly ConfigurationRevision Revision = new("rev-1");

    [Fact]
    public void TheActionResultAndReasonCodesArePinned()
    {
        Assert.Equal(
            ["bootstrap_token.issued", "recovery_token.issued", "token.superseded", "bootstrap.redeemed", "recovery.redeemed", "enrollment.consumed"],
            Enum.GetValues<AuditAction>().Select(action => action.ToCode()));
        Assert.Equal(["succeeded", "failed"], Enum.GetValues<AuditResult>().Select(result => result.ToCode()));
        Assert.Equal(
            ["none", "wrong_secret", "wrong_purpose", "consumed", "superseded", "expired", "already_initialized", "not_initialized"],
            Enum.GetValues<AuditReason>().Select(reason => reason.ToCode()));
        Assert.Equal("operator", AuditRecord.Scope);
    }

    [Fact]
    public void TheActorIsStoredAsItsCode()
    {
        var principal = ResourceId.New(ResourceTypes.Principal);

        Assert.Equal("anonymous", AuditActor.Anonymous.ToCode());
        Assert.Equal("operator_command", AuditActor.OperatorCommand.ToCode());
        Assert.Equal(principal.Value, AuditActor.ForPrincipal(principal).ToCode());
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => AuditActor.ForPrincipal(null!)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => AuditActor.ForPrincipal(ResourceId.New(ResourceTypes.Project))).Code);
    }

    [Theory]
    [InlineData(AuditResult.Succeeded, AuditReason.None, true)]
    [InlineData(AuditResult.Failed, AuditReason.Consumed, true)]
    [InlineData(AuditResult.Succeeded, AuditReason.Consumed, false)]
    [InlineData(AuditResult.Failed, AuditReason.None, false)]
    public void TheReasonIsNoneExactlyWhenTheResultSucceeded(AuditResult result, AuditReason reason, bool valid)
    {
        var record = new AuditRecord(AuditActor.Anonymous, null, AuditAction.BootstrapRedeemed, result, reason, Revision, RequestCorrelationId.New());

        Assert.Equal(valid, record.IsValid);
        if (!valid)
        {
            Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => record.RequireValid()).Code);
        }
    }

    [Fact]
    public void ARecordNeedsEveryValueAndAPrincipalTarget()
    {
        var valid = new AuditRecord(AuditActor.OperatorCommand, null, AuditAction.TokenSuperseded, AuditResult.Succeeded, AuditReason.None, Revision, RequestCorrelationId.New());

        Assert.Same(valid, valid.RequireValid());
        Assert.False((valid with { Actor = null! }).IsValid);
        Assert.False((valid with { ConfigurationRevision = null! }).IsValid);
        Assert.False((valid with { CorrelationId = null! }).IsValid);
        Assert.False((valid with { Action = (AuditAction)42 }).IsValid);
        Assert.False((valid with { TargetPrincipal = ResourceId.New(ResourceTypes.Project) }).IsValid);
        Assert.True((valid with { TargetPrincipal = ResourceId.New(ResourceTypes.Principal) }).IsValid);
    }
}
