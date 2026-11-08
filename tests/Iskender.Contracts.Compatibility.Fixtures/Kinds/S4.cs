// S4: a return type changes.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S4.Before
{
    public static class Widget
    {
        public static int Size() => 1;
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S4.After
{
    public static class Widget
    {
        public static long Size() => 1;
    }
}
