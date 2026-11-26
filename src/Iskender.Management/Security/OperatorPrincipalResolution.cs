using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Management.Security;

/// <summary>A principal resolution: the principal's ID, or none. Owned by ISK-0010.</summary>
public readonly record struct OperatorPrincipalResolution
{
    private OperatorPrincipalResolution(ResourceId? principal)
    {
        Principal = principal;
    }

    /// <summary>No principal: the request answers <c>401</c>.</summary>
    public static OperatorPrincipalResolution Unresolved => default;

    /// <summary>The resolved principal, or <c>null</c> when unresolved.</summary>
    public ResourceId? Principal { get; }

    /// <summary>
    /// <paramref name="principal"/>, which must be a <c>principal</c> ID; any other value fails with <c>core.value.invalid</c>.
    /// </summary>
    public static OperatorPrincipalResolution Resolved(ResourceId principal) =>
        principal is not null && principal.Type == ResourceTypes.Principal ? new(principal) : throw new GatewayException(ErrorCodes.ValueInvalid);
}
