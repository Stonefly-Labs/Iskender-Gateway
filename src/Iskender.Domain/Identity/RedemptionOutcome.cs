using System.Diagnostics.CodeAnalysis;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Domain.Identity;

/// <summary>
/// A redemption's result. A success has a principal and an enrollment code. A failure has neither, and has a reason.
/// Failure is a result, not an error. Owned by ISK-0010.
/// </summary>
public sealed class RedemptionOutcome
{
    private RedemptionOutcome(ResourceId? principal, IssuedOperatorToken? enrollmentCode, RedemptionFailureReason? failureReason)
    {
        Principal = principal;
        EnrollmentCode = enrollmentCode;
        FailureReason = failureReason;
    }

    [MemberNotNullWhen(true, nameof(Principal), nameof(EnrollmentCode))]
    [MemberNotNullWhen(false, nameof(FailureReason))]
    public bool Succeeded => Principal is not null;

    /// <summary>The new operator principal, or <c>null</c>.</summary>
    public ResourceId? Principal { get; }

    /// <summary>The new principal's enrollment code, revealed once in the redemption's answer, or <c>null</c>.</summary>
    public IssuedOperatorToken? EnrollmentCode { get; }

    public RedemptionFailureReason? FailureReason { get; }

    /// <summary>
    /// A success. A <c>null</c> value, a principal not of type <c>principal</c>, or a code whose purpose is not
    /// <c>enrollment</c> fails with <c>core.value.invalid</c>.
    /// </summary>
    public static RedemptionOutcome Success(ResourceId principal, IssuedOperatorToken enrollmentCode) =>
        principal is null || !ReferenceEquals(principal.Type, ResourceTypes.Principal)
            || enrollmentCode is null || enrollmentCode.Purpose != OperatorTokenPurpose.Enrollment
            ? throw new GatewayException(ErrorCodes.ValueInvalid)
            : new(principal, enrollmentCode, failureReason: null);

    /// <summary>A failure. An undefined reason fails with <c>core.value.invalid</c>.</summary>
    public static RedemptionOutcome Failure(RedemptionFailureReason reason) =>
        Enum.IsDefined(reason) ? new(principal: null, enrollmentCode: null, reason) : throw new GatewayException(ErrorCodes.ValueInvalid);

    public override string ToString() => Succeeded ? "succeeded" : $"failed: {FailureReason.Value.ToCode()}";
}
