// S9: a type's kind changes from struct to class.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S9Kind.Before
{
    public struct Widget
    {
        public int Size { get; }
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S9Kind.After
{
    public class Widget
    {
        public int Size { get; }
    }
}
