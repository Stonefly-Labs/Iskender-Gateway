using Iskender.Domain.Ports;

namespace Iskender.Domain.Identity;

/// <summary>
/// An action an admin route requires (operator-security contract, Principals and grants rule 2). The name follows the
/// operation pattern, 1 to 63 characters of <c>a-z</c>, <c>0-9</c>, <c>_</c>, and <c>.</c>, starting with a letter, so a
/// later spec adds an action without changing a type. Otherwise <c>core.value.invalid</c>. Owned by ISK-0010.
/// </summary>
public sealed record OperatorAction
{
    public OperatorAction(string name)
    {
        Name = PortNames.RequireOperation(name);
    }

    public string Name { get; }

    public override string ToString() => Name;
}
