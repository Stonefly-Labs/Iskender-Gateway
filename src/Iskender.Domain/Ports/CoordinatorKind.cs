using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>The three coordinator kinds this spec ships (TR-1). Owned by ISK-0005.</summary>
public enum CoordinatorKind
{
    Local,
    Redis,
    PostgreSql,
}

/// <summary>The code of each kind, as configuration and diagnostics write it. Owned by ISK-0005.</summary>
public static class CoordinatorKinds
{
    public static string ToCode(this CoordinatorKind kind) => kind switch
    {
        CoordinatorKind.Local => "local",
        CoordinatorKind.Redis => "redis",
        CoordinatorKind.PostgreSql => "postgresql",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };
}
