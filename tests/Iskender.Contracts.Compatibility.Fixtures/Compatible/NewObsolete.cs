// Compatible: an item is deprecated, which is reported as a deprecation.
namespace Iskender.Contracts.Compatibility.Fixtures.Compatible.NewObsolete.Before
{
    public static class Widget
    {
        public static int Size() => 1;
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Compatible.NewObsolete.After
{
    public static class Widget
    {
        [Obsolete("Deprecated in fixture@1. No replacement.")]
        public static int Size() => 1;
    }
}
