// Real violations of rules A17 and A18, which need compiled types to prove them. No production project references this.
namespace Iskender.Domain.Routing
{
    public sealed class RouteTable;
}

namespace Iskender.Domain.Policy
{
    using Iskender.Domain.Ports;
    using Iskender.Domain.Routing;

    public sealed class UsesRouting
    {
        public RouteTable? Route { get; set; }
    }

    public sealed class UsesOnlyPorts
    {
        public SharedShape? Shape { get; set; }
    }
}

namespace Iskender.Domain.Policy.Cel
{
    using Iskender.Domain.Routing;

    public sealed class ChildUsesRouting
    {
        public RouteTable? Route { get; set; }
    }

    public sealed class ChildUsesItsOwnModule
    {
        public Iskender.Domain.Policy.UsesOnlyPorts? Sibling { get; set; }
    }
}

namespace Iskender.Domain.Ports
{
    using Iskender.Domain.Routing;

    public sealed class SharedShape;

    public sealed class PortUsesModule
    {
        public RouteTable? Route { get; set; }
    }
}

namespace Iskender.Domain
{
    public sealed class InRootNamespace;
}

namespace Iskender.Domain.Unmapped
{
    public sealed class InUnmappedNamespace;
}

namespace Iskender.Domain.PolicyExtras
{
    public sealed class PrefixTrap;
}
