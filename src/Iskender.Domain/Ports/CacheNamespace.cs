namespace Iskender.Domain.Ports;

/// <summary>
/// A versioned cache namespace: a module and a namespace name. Bumping its generation makes every entry in it a miss
/// (contract Versioned cache rule 1). An entry is named by a namespace and a name. Owned by ISK-0005.
/// </summary>
public sealed record CacheNamespace
{
    /// <summary>A module name and a namespace name, with the patterns of <see cref="CoordinationName"/>. An invalid value fails with <c>core.value.invalid</c>.</summary>
    public CacheNamespace(string module, string name)
    {
        Module = PortNames.RequireModule(module);
        Name = PortNames.RequireOperation(name);
    }

    public string Module { get; }

    public string Name { get; }
}
