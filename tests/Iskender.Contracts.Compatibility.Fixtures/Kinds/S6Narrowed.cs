// S6: a public member's accessibility narrows to protected.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S6Narrowed.Before
{
    public class Widget
    {
        public virtual int Size() => 1;
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S6Narrowed.After
{
    public class Widget
    {
        protected virtual int Size() => 1;
    }
}
