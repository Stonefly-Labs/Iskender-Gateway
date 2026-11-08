// S8: an enum value is renamed, keeping its number.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S8Renamed.Before
{
    public enum Level
    {
        Low = 1,
        High = 2,
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S8Renamed.After
{
    public enum Level
    {
        Low = 1,
        Top = 2,
    }
}
