// S6: a type becomes sealed.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S6.Before
{
    public class Widget
    {
        public int Size { get; set; }
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S6.After
{
    public sealed class Widget
    {
        public int Size { get; set; }
    }
}
