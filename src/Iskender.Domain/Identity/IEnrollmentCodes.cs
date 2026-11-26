using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Identity;

/// <summary>
/// Consumes an enrollment code once, in the caller's operator-scope unit, so the caller attaches its sign-in method in
/// the same commit (operator-security contract, Enrollment). It succeeds only for an unconsumed, unexpired
/// <c>enrollment</c> code bound to <paramref name="principal"/> whose secret matches, and writes an
/// <c>enrollment.consumed</c> audit record. Anything else fails with <c>operator.enrollment_invalid</c> and changes
/// nothing. ISK-0013 or ISK-0014 is the first caller; ISK-0010 ships none. Owned by ISK-0010.
/// </summary>
public interface IEnrollmentCodes
{
    ValueTask ConsumeAsync(
        IUnitOfWork unit, ResourceId principal, OperatorTokenText code, OperatorRequestContext context, CancellationToken cancellationToken);
}
