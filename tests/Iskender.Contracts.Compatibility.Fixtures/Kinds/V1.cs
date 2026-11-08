using Iskender.Core.Contracts.Versioning;

// V1: a new type has no tag, a member added after its type has none of its own, and a member's tag names another contract.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.V1.Before
{
    [ContractVersion("fixture", 1)]
    public static class Widget
    {
        public static int Size => 1;
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.V1.After
{
    [ContractVersion("fixture", 1)]
    public static class Widget
    {
        public static int Size => 1;

        public static int Depth => 2;

        [ContractVersion("other", 2)]
        public static int Width => 3;
    }

    public static class Gadget
    {
        public static int Size => 1;
    }
}
