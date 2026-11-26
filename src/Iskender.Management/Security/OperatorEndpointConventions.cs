using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Microsoft.AspNetCore.Builder;

namespace Iskender.Management.Security;

/// <summary>
/// The two declarations an endpoint in the admin route group makes, exactly one each (operator-security contract, Admin
/// routes rule 2, research R9): an operator action it requires, or the anonymous endpoint inventory entry it is. One that
/// declares neither or both fails the host's start with <c>operator.route_undeclared</c>. Owned by ISK-0010.
/// </summary>
public static class OperatorEndpointConventions
{
    /// <summary>
    /// Declares that the endpoint requires <paramref name="action"/>: it runs the gate, principal resolution, and
    /// authorization first. A <c>null</c> argument fails with <c>core.value.invalid</c>.
    /// </summary>
    public static TBuilder RequireOperatorAction<TBuilder>(this TBuilder builder, OperatorAction action)
        where TBuilder : IEndpointConventionBuilder
    {
        if (builder is null || action is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        builder.WithMetadata(new OperatorActionRequirement(action));
        return builder;
    }

    /// <summary>
    /// Declares that the endpoint is anonymous, as the anonymous endpoint inventory's <paramref name="inventoryEntry"/>.
    /// A <c>null</c> builder or an empty entry fails with <c>core.value.invalid</c>.
    /// </summary>
    public static TBuilder AllowAnonymousOperatorEndpoint<TBuilder>(this TBuilder builder, string inventoryEntry)
        where TBuilder : IEndpointConventionBuilder
    {
        if (builder is null || string.IsNullOrWhiteSpace(inventoryEntry))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        builder.WithMetadata(new AnonymousOperatorEndpoint(inventoryEntry));
        return builder;
    }
}

/// <summary>The metadata <see cref="OperatorEndpointConventions.RequireOperatorAction"/> adds. Owned by ISK-0010.</summary>
internal sealed record OperatorActionRequirement(OperatorAction Action);

/// <summary>The metadata <see cref="OperatorEndpointConventions.AllowAnonymousOperatorEndpoint"/> adds. Owned by ISK-0010.</summary>
internal sealed record AnonymousOperatorEndpoint(string InventoryEntry);

/// <summary>
/// Marks the group's own bootstrap and recovery redemption routes, the only endpoints that skip the uninitialized gate
/// (operator-security contract, Admin routes rules 5 and 6; FR-018; CONV-0010-021). Internal, so no other endpoint can
/// carry it. Owned by ISK-0010.
/// </summary>
internal sealed record OperatorRedemptionEndpoint;

/// <summary>
/// Marks an endpoint of the admin route group, with its route template, the declarations it made, and whether it is one of
/// the group's own redemption routes. Owned by ISK-0010.
/// </summary>
internal sealed record OperatorGroupEndpoint(string Route, OperatorActionRequirement? Action, AnonymousOperatorEndpoint? Anonymous, bool Declared, bool Redemption = false);

/// <summary>Marks the group's own catch-all, which answers <c>404</c> with no body and needs no declaration. Owned by ISK-0010.</summary>
internal sealed record OperatorUnmatchedEndpoint;
