using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Domain.Identity;

/// <summary>
/// Decides whether an operator principal may perform an action (operator-security contract, Admin routes rule 9). It
/// reads the principal's grants from the store on every call, with no cache, so a grant or revocation takes effect on the
/// next request, and throws the persistence failure as a <c>GatewayException</c> when it cannot read (FR-015). It denies
/// by default. Owned by ISK-0010.
/// </summary>
public interface IOperatorAuthorization
{
    ValueTask<OperatorDecision> DecideAsync(ResourceId principal, OperatorAction action, CancellationToken cancellationToken);
}
