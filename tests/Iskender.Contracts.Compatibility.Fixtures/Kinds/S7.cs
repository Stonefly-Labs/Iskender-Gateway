// S7: an abstract member is added to a public abstract class.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S7.Before
{
    public abstract class Widget
    {
        public abstract int Size();
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S7.After
{
    public abstract class Widget
    {
        public abstract int Size();

        public abstract int Weight();
    }
}
