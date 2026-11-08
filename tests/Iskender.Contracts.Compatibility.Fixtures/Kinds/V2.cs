using Iskender.Core.Contracts.Versioning;

// V2: a member is added, so the package version must rise by one.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.V2.Before
{
    [ContractVersion("fixture", 1)]
    public static class Widget
    {
        public static int Size => 1;
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.V2.After
{
    [ContractVersion("fixture", 1)]
    public static class Widget
    {
        public static int Size => 1;

        [ContractVersion("fixture", 2)]
        public static int Depth => 2;
    }
}
