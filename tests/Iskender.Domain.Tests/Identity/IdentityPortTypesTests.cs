using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Identity;

namespace Iskender.Domain.Tests.Identity;

/// <summary>The Identity port types of ISK-0010 (data-model "Identity ports", T003 and T004).</summary>
public class IdentityPortTypesTests
{
    [Fact]
    public void InstallationStatusSetsItsTimeExactlyWhenInitialized()
    {
        var at = DateTimeOffset.UtcNow;

        Assert.Equal((false, (DateTimeOffset?)null), (InstallationStatus.Uninitialized.IsInitialized, InstallationStatus.Uninitialized.InitializedAt));
        Assert.Equal((true, (DateTimeOffset?)at), (InstallationStatus.Initialized(at).IsInitialized, InstallationStatus.Initialized(at).InitializedAt));
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => InstallationStatus.Initialized(default)).Code);
    }

    [Fact]
    public void TheActionAndRoleCodesArePinned()
    {
        Assert.Equal("installation.read", OperatorActions.InstallationRead.Name);
        Assert.Equal(["installation.read"], OperatorActions.All.Select(action => action.Name));
        Assert.Equal("system_operator", OperatorRoles.SystemOperator.Name);
        Assert.Equal(["system_operator"], OperatorRoles.All.Select(role => role.Name));
        Assert.Equal(OperatorActions.All, OperatorRoles.SystemOperator.Actions);
        Assert.True(OperatorRoles.SystemOperator.Holds(new OperatorAction("installation.read")));
        Assert.False(OperatorRoles.SystemOperator.Holds(new OperatorAction("installation.write")));
        Assert.True(OperatorRoles.TryGet("system_operator", out var role));
        Assert.Same(OperatorRoles.SystemOperator, role);
        Assert.False(OperatorRoles.TryGet("System_Operator", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Installation.read")]
    [InlineData("1installation")]
    [InlineData("installation-read")]
    [InlineData("installation read")]
    public void AnInvalidActionOrRoleNameFailsWithValueInvalid(string? name)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new OperatorAction(name!)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new OperatorRole(name!, OperatorActions.All)).Code);
    }

    [Fact]
    public void ARoleNeedsItsActions()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new OperatorRole("role", null!)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new OperatorRole("role", [null!])).Code);
    }

    [Fact]
    public void ThePurposeCodesArePinned()
    {
        Assert.Equal(
            [("bootstrap", 'b'), ("recovery", 'r'), ("enrollment", 'e')],
            Enum.GetValues<OperatorTokenPurpose>().Select(purpose => (purpose.ToCode(), purpose.ToPrefixLetter())));
        Assert.True(OperatorTokenPurposes.TryParse("recovery", out var parsed));
        Assert.Equal(OperatorTokenPurpose.Recovery, parsed);
        Assert.False(OperatorTokenPurposes.TryParse("Recovery", out _));
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => ((OperatorTokenPurpose)9).ToCode()).Code);
    }

    [Fact]
    public void TheReasonCodesAndAuditingArePinned()
    {
        Assert.Equal(
            [
                ("malformed", false), ("unknown", false), ("wrong_secret", true), ("wrong_purpose", true), ("consumed", true),
                ("superseded", true), ("expired", true), ("already_initialized", true), ("not_initialized", true),
            ],
            Enum.GetValues<RedemptionFailureReason>().Select(reason => (reason.ToCode(), reason.IsAudited())));
        Assert.False(((RedemptionFailureReason)42).IsAudited());
    }

    [Fact]
    public void AnActorHasAPrincipalExactlyWhenItIsAPrincipal()
    {
        var principal = ResourceId.New(ResourceTypes.Principal);

        Assert.Equal((OperatorActorKind.Anonymous, (ResourceId?)null), (OperatorActor.Anonymous.Kind, OperatorActor.Anonymous.Principal));
        Assert.Equal((OperatorActorKind.OperatorCommand, (ResourceId?)null), (OperatorActor.OperatorCommand.Kind, OperatorActor.OperatorCommand.Principal));
        Assert.Equal((OperatorActorKind.Principal, (ResourceId?)principal), (OperatorActor.ForPrincipal(principal).Kind, OperatorActor.ForPrincipal(principal).Principal));
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => OperatorActor.ForPrincipal(null!)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => OperatorActor.ForPrincipal(ResourceId.New(ResourceTypes.Project))).Code);
    }

    [Fact]
    public void ARequestContextNeedsEveryValue()
    {
        var correlation = RequestCorrelationId.New();
        var revision = new ConfigurationRevision("rev-1");

        var context = new OperatorRequestContext(correlation, revision, OperatorActor.Anonymous);

        Assert.Equal((correlation, revision, OperatorActor.Anonymous), (context.CorrelationId, context.ConfigurationRevision, context.Actor));
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new OperatorRequestContext(null!, revision, OperatorActor.Anonymous)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new OperatorRequestContext(correlation, null!, OperatorActor.Anonymous)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new OperatorRequestContext(correlation, revision, null!)).Code);
    }

    [Fact]
    public void ASuccessHasAPrincipalAndACodeAndAFailureHasNeither()
    {
        var principal = ResourceId.New(ResourceTypes.Principal);
        var code = new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Enrollment), DateTimeOffset.UtcNow);

        var success = RedemptionOutcome.Success(principal, code);
        var failure = RedemptionOutcome.Failure(RedemptionFailureReason.Consumed);

        Assert.Equal((true, (ResourceId?)principal, (IssuedOperatorToken?)code, (RedemptionFailureReason?)null), (success.Succeeded, success.Principal, success.EnrollmentCode, success.FailureReason));
        Assert.Equal((false, (ResourceId?)null, (IssuedOperatorToken?)null, (RedemptionFailureReason?)RedemptionFailureReason.Consumed), (failure.Succeeded, failure.Principal, failure.EnrollmentCode, failure.FailureReason));
        Assert.Equal("failed: consumed", failure.ToString());
        Assert.Equal("succeeded", success.ToString());
    }

    [Fact]
    public void AnOutcomeRefusesInvalidValues()
    {
        var principal = ResourceId.New(ResourceTypes.Principal);
        var bootstrap = new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Bootstrap), DateTimeOffset.UtcNow);
        var code = new IssuedOperatorToken(OperatorTokenText.Generate(OperatorTokenPurpose.Enrollment), DateTimeOffset.UtcNow);

        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => RedemptionOutcome.Success(null!, code)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => RedemptionOutcome.Success(principal, null!)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => RedemptionOutcome.Success(principal, bootstrap)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => RedemptionOutcome.Success(ResourceId.New(ResourceTypes.Project), code)).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => RedemptionOutcome.Failure((RedemptionFailureReason)42)).Code);
    }

    [Fact]
    public void TheDecisionDeniesByDefault()
    {
        Assert.Equal(OperatorDecision.Denied, default(OperatorDecision));
        Assert.Equal(2, Enum.GetValues<OperatorDecision>().Length);
    }
}
