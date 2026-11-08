using Iskender.Core.Contracts.Versioning;

// V1: a published type loses its version tag.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.V1Removed.Before
{
    [ContractVersion("fixture", 1)]
    public static class Widget
    {
        public static int Size => 1;
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.V1Removed.After
{
    public static class Widget
    {
        public static int Size => 1;
    }
}
