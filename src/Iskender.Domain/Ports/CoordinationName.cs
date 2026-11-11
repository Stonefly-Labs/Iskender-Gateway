namespace Iskender.Domain.Ports;

/// <summary>
/// The module and name of a piece of coordination state, such as a counter or a lease. No port accepts a raw key: the
/// stored key is derived from the scope, the module, and the name (contract Scope and keys rules 2 and 3). Owned by ISK-0005.
/// </summary>
public sealed record CoordinationName
{
    /// <summary>A module name and a state name, with the patterns of <see cref="AdvisoryLockName"/>. An invalid value fails with <c>core.value.invalid</c>.</summary>
    public CoordinationName(string module, string name)
    {
        Module = PortNames.RequireModule(module);
        Name = PortNames.RequireOperation(name);
    }

    public string Module { get; }

    public string Name { get; }
}
