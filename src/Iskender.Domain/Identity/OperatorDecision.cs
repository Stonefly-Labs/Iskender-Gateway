namespace Iskender.Domain.Identity;

/// <summary>An authorization decision. The default value is <see cref="Denied"/>, so an unset decision denies. Owned by ISK-0010.</summary>
public enum OperatorDecision
{
    /// <summary>A role the principal is granted holds the action.</summary>
    Allowed = 1,

    /// <summary>No grant holds the action: the default.</summary>
    Denied = 0,
}
