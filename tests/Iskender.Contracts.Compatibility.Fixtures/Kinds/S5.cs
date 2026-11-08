// S5: a nullable annotation changes, on a return and on a generic argument.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S5.Before
{
    public static class Widget
    {
        public static string? Find() => null;

        public static IReadOnlyList<string?> Names() => [];
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S5.After
{
    public static class Widget
    {
        public static string Find() => string.Empty;

        public static IReadOnlyList<string> Names() => [];
    }
}
