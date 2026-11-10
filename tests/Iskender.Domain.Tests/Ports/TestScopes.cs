using System.Diagnostics;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Domain.Tests.Ports;

/// <summary>Builds owner scopes, attested references, and request contexts for the port tests.</summary>
internal static class TestScopes
{
    public static OrganizationScope Organization() => new(ResourceId.New(ResourceTypes.Organization));

    public static ProjectScope Project() => new(ResourceId.New(ResourceTypes.Organization), ResourceId.New(ResourceTypes.Project));

    public static ScopedResourceReference Reference(OwnerScope owner) => TestResolver.Resolve(ResourceId.New(ResourceTypes.Endpoint), owner);

    public static GatewayExecutionContext Context(DateTimeOffset deadline, CancellationToken cancellationToken) =>
        new(new CorrelationContext(RequestCorrelationId.New(), ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom()), deadline, TimeProvider.System, cancellationToken);

    private sealed class TestResolver : OwnershipResolver
    {
        public static ScopedResourceReference Resolve(ResourceId id, OwnerScope owner) => Attest(id, owner);
    }
}
