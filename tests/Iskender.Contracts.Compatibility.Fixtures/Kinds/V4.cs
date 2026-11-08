using Iskender.Core.Contracts.Versioning;

// V4: deprecations that do not state their version and replacement, or name another contract.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.V4.Before
{
    [ContractVersion("fixture", 1)]
    public static class Widget
    {
        public static int Size => 1;

        public static int Depth => 2;
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.V4.After
{
    [ContractVersion("fixture", 1)]
    public static class Widget
    {
        [Obsolete("Use Depth instead.")]
        public static int Size => 1;

        [Obsolete("Deprecated in other@1. No replacement.")]
        public static int Depth => 2;
    }
}
