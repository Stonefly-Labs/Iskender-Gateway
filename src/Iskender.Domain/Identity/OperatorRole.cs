using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Identity;

/// <summary>
/// A role granted at the operator scope, and the actions it holds (operator-security contract, Principals and grants rule
/// 2). The name follows the operation pattern, and a <c>null</c> action list or action fails with
/// <c>core.value.invalid</c>. Roles are mapped to actions in code, never read from a token. Owned by ISK-0010.
/// </summary>
public sealed record OperatorRole
{
    public OperatorRole(string name, IReadOnlyList<OperatorAction> actions)
    {
        Name = PortNames.RequireOperation(name);
        Actions = actions is null || actions.Any(action => action is null)
            ? throw new GatewayException(ErrorCodes.ValueInvalid)
            : Array.AsReadOnly(actions.ToArray());
    }

    public string Name { get; }

    public IReadOnlyList<OperatorAction> Actions { get; }

    /// <summary>Whether the role holds <paramref name="action"/>.</summary>
    public bool Holds(OperatorAction action) => Actions.Contains(action);

    public override string ToString() => Name;
}
