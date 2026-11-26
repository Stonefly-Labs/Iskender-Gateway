namespace Iskender.Domain.Identity;

/// <summary>The operator actions this contract defines (operator-security contract, Principals and grants rule 2). Owned by ISK-0010.</summary>
public static class OperatorActions
{
    /// <summary><c>installation.read</c>: read the installation state at <c>GET &lt;prefix&gt;/installation</c>.</summary>
    public static OperatorAction InstallationRead { get; } = new("installation.read");

    /// <summary>Every defined action.</summary>
    public static IReadOnlyList<OperatorAction> All { get; } = Array.AsReadOnly([InstallationRead]);
}
