// S2: a public member is removed.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S2.Before
{
    public static class Widget
    {
        public static int Size() => 1;

        public static int Count() => 2;
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S2.After
{
    public static class Widget
    {
        public static int Size() => 1;
    }
}
