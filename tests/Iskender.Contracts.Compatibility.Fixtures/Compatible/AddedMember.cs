// Compatible: a public member is added.
namespace Iskender.Contracts.Compatibility.Fixtures.Compatible.AddedMember.Before
{
    public static class Widget
    {
        public static int Size() => 1;
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Compatible.AddedMember.After
{
    public static class Widget
    {
        public static int Size() => 1;

        public static int Weight() => 2;
    }
}
