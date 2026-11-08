using Iskender.Core.Contracts.Versioning;

// The surface reader's fixture. SurfaceReaderTests asserts its lines exactly, so any change here changes that test.
namespace Iskender.Contracts.Compatibility.Fixtures.Reader;

[ContractVersion("fixture", 1)]
public interface IShape
{
    string Name { get; }

    int Sides() => 0;
}

[ContractVersion("fixture", 1)]
public enum Color : byte
{
    Red = 1,
    Green = 2,
}

[ContractVersion("fixture", 1)]
public delegate void Changed(object? sender);

[ContractVersion("fixture", 1)]
public abstract class Sample : IShape
{
    public const int Limit = 3;

    public static readonly Sample? Default;

    protected Sample(string name)
    {
        Name = name;
    }

    public event Changed? OnChanged;

    public string Name { get; }

    public string? Label { get; protected set; }

    public int Order { get; init; }

    [ContractVersion("fixture", 2)]
    public IReadOnlyList<string?> Tags { get; } = [];

    public abstract Color Paint(Color color = Color.Green);

    public virtual bool TryFind(string key, out Sample? found, int depth = 2)
    {
        found = null;
        return key.Length > depth;
    }

    public static T Echo<T>(T value) => value;

    public Dictionary<string, List<int?>>? Map(string?[] keys) => keys.Length == 0 && Name.Length == 0 ? null : [];

    [Obsolete("Deprecated in fixture@2. No replacement.")]
    public void Old()
    {
        OnChanged?.Invoke(this);
    }

    public sealed class Nested
    {
        public int Value { get; set; }
    }
}

[ContractVersion("fixture", 1)]
public readonly struct Point(int x)
{
    public int X { get; } = x;
}

internal sealed class Hidden
{
    public int Value { get; set; }
}
