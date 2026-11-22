namespace Iskender.Domain.Identity;

/// <summary>
/// Redeems a bootstrap or recovery token at the route of <paramref name="route"/>'s purpose (operator-security contract,
/// Redemption). It is one operator-scope unit at Read Committed that locks the token's row: exactly one of any number of
/// concurrent redemptions of a token succeeds. A refused token is a <see cref="RedemptionOutcome"/> failure. A store
/// failure, or a commit whose outcome is unknown, throws the persistence failure as a <c>GatewayException</c> and is never
/// retried. A route of <see cref="OperatorTokenPurpose.Enrollment"/> fails with <c>core.value.invalid</c>. Owned by
/// ISK-0010.
/// </summary>
public interface IOperatorRedemption
{
    ValueTask<RedemptionOutcome> RedeemAsync(
        OperatorTokenPurpose route, OperatorTokenText token, OperatorRequestContext context, CancellationToken cancellationToken);
}
