// H7: a context type whose public members expose a service locator, a stream, and a credential type.
namespace Iskender.Contracts.Compatibility.Fixtures.Declarations.H7;

public sealed class ApiCredential;

public sealed class LeakyView
{
    public IServiceProvider? Services { get; }

    public static Stream Open() => Stream.Null;

    public static void Use(IReadOnlyList<ApiCredential> credentials) => _ = credentials;

    public int Count { get; }
}
