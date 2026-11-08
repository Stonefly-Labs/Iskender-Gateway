using Iskender.Core.Contracts.Versioning;

// V3: a published version tag changes.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.V3.Before
{
    [ContractVersion("fixture", 1)]
    public static class Widget;
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.V3.After
{
    [ContractVersion("fixture", 2)]
    public static class Widget;
}
