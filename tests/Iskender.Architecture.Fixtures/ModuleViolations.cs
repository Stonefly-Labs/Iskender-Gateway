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

    // Each of these makes the compiler emit types: <PrivateImplementationDetails>, a record's members, and closures.
    public sealed record PolicyRecord(int Value);

    public static class EmitsCompilerTypes
    {
        private static readonly int[] Table = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };

        public static int Sum() => Table.Sum();

        public static Func<Task<int>> Later() => async () =>
        {
            await Task.Yield();
            return Table.Length;
        };
    }

    // Uses Routing only inside an async method body, which the compiler moves to a nested state machine type.
    public static class UsesRoutingInAsyncBody
    {
        public static async Task<string?> NameAsync()
        {
            await Task.Yield();
            return new RouteTable().ToString();
        }
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

// File-local types, which the compiler emits under a name containing '<' with no containing declared type.
namespace Iskender.Domain.Policy
{
    using Iskender.Domain.Routing;

    file sealed class FileLocalUsesRouting
    {
        public RouteTable? Route { get; set; }
    }
}

namespace Iskender.Domain
{
    file sealed class FileLocalInRoot;
}
