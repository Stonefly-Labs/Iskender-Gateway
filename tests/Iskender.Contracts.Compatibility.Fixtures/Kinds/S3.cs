// S3: a parameter is renamed.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S3.Before
{
    public static class Widget
    {
        public static int Scale(int factor) => factor;
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S3.After
{
    public static class Widget
    {
        public static int Scale(int multiplier) => multiplier;
    }
}
