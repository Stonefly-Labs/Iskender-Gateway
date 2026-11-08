// S9: an implemented interface is removed.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S9.Before
{
    public interface IMeasured
    {
        int Size { get; }
    }

    public sealed class Widget : IMeasured
    {
        public int Size { get; } = 1;
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S9.After
{
    public interface IMeasured
    {
        int Size { get; }
    }

    public sealed class Widget
    {
        public int Size { get; } = 1;
    }
}
