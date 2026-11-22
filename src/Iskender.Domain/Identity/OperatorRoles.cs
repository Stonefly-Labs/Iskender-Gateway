using System.Diagnostics.CodeAnalysis;

namespace Iskender.Domain.Identity;

/// <summary>The operator roles this contract defines (operator-security contract, Principals and grants rule 2). Owned by ISK-0010.</summary>
public static class OperatorRoles
{
    /// <summary><c>system_operator</c>: holds every action in <see cref="OperatorActions.All"/>, and no tenant row.</summary>
    public static OperatorRole SystemOperator { get; } = new("system_operator", OperatorActions.All);

    /// <summary>Every defined role.</summary>
    public static IReadOnlyList<OperatorRole> All { get; } = Array.AsReadOnly([SystemOperator]);

    /// <summary>Finds a role by its stored name, compared ordinally. Anything else returns <c>false</c>.</summary>
    public static bool TryGet(string? name, [NotNullWhen(true)] out OperatorRole? role)
    {
        role = All.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        return role is not null;
    }
}
