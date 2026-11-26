using Microsoft.AspNetCore.Http;

namespace Iskender.Management.Security;

/// <summary>
/// Resolves the operator principal a request on a protected admin route speaks for (operator-security contract, Admin
/// routes rule 8, research R10, FR-017). ISK-0013, ISK-0014, and ISK-0020 implement it; none ships here, so with none
/// registered every protected route answers <c>401</c> <c>identity.unauthenticated</c>. It runs only after the
/// uninitialized gate, and a throw answers <c>503</c> <c>operator.authorization_unavailable</c>. It is resolved from
/// the request's services, so it may be registered with any lifetime, scoped included. Owned by ISK-0010.
/// </summary>
public interface IOperatorPrincipalResolver
{
    /// <summary>The request's principal, or <see cref="OperatorPrincipalResolution.Unresolved"/>.</summary>
    ValueTask<OperatorPrincipalResolution> ResolveAsync(HttpContext context, CancellationToken cancellationToken);
}
