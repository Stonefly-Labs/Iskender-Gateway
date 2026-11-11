using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>The single-instance verification state (FR-007a, research R14). Owned by ISK-0005.</summary>
public enum SingleInstanceState
{
    /// <summary>The topology is <c>multi_replica</c>, so there is nothing to verify.</summary>
    NotApplicable,

    /// <summary>This process holds its plane's instance lease.</summary>
    Held,

    /// <summary>Another process holds its plane's instance lease.</summary>
    HeldByOther,

    /// <summary>This process held the lease and lost it.</summary>
    Lost,

    /// <summary>No PostgreSQL lease coordinator is configured, so the declaration cannot be checked.</summary>
    Unverified,
}

/// <summary>The snake-case code of each state. Owned by ISK-0005.</summary>
public static class SingleInstanceStates
{
    public static string ToCode(this SingleInstanceState state) => state switch
    {
        SingleInstanceState.NotApplicable => "not_applicable",
        SingleInstanceState.Held => "held",
        SingleInstanceState.HeldByOther => "held_by_other",
        SingleInstanceState.Lost => "lost",
        SingleInstanceState.Unverified => "unverified",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };
}
