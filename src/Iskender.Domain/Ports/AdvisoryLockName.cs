namespace Iskender.Domain.Ports;

/// <summary>
/// A transaction-scoped advisory lock's module and name. The key also includes the unit's own scope (research R14).
/// Owned by ISK-0004.
/// </summary>
public sealed record AdvisoryLockName
{
    /// <summary>A module name and a lock name. An invalid value fails with <c>core.value.invalid</c>.</summary>
    public AdvisoryLockName(string module, string name)
    {
        Module = PortNames.RequireModule(module);
        Name = PortNames.RequireOperation(name);
    }

    public string Module { get; }

    public string Name { get; }
}
