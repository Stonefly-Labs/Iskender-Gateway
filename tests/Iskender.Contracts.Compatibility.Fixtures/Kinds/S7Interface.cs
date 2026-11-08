// S7: a member without a default implementation is added to a public interface.
namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S7Interface.Before
{
    public interface IWidget
    {
        int Size();
    }
}

namespace Iskender.Contracts.Compatibility.Fixtures.Kinds.S7Interface.After
{
    public interface IWidget
    {
        int Size();

        int Weight();
    }
}
