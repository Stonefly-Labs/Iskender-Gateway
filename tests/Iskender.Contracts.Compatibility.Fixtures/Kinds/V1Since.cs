using Iskender.Core.Contracts.Versioning;

// V1: with the package at version 2, a new type is tagged with an older version and a new member with a later one.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.V1Since.Before
{
    [ContractVersion("fixture", 1)]
    public static class Widget
    {
        public static int Size => 1;
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.V1Since.After
{
    [ContractVersion("fixture", 1)]
    public static class Widget
    {
        public static int Size => 1;

        [ContractVersion("fixture", 7)]
        public static int Width => 3;

        [ContractVersion("fixture", 2)]
        public static int Depth => 2;
    }

    [ContractVersion("fixture", 1)]
    public static class Gadget
    {
        public static int Size => 1;
    }

    [ContractVersion("fixture", 2)]
    public static class Gizmo
    {
        public static int Size => 1;
    }
}
