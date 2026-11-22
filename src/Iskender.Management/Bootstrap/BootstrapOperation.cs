using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;

namespace Iskender.Management.Bootstrap;

/// <summary>
/// A <c>bootstrap</c> operation (container contract, Bootstrap command; research R14): <c>issue</c> issues a bootstrap
/// token, and <c>recover</c> a recovery token. Owned by ISK-0010.
/// </summary>
public enum BootstrapOperation
{
    /// <summary><c>bootstrap issue</c>.</summary>
    Issue,

    /// <summary><c>bootstrap recover</c>.</summary>
    Recover,
}

/// <summary>The names and token purposes of <see cref="BootstrapOperation"/>. Owned by ISK-0010.</summary>
public static class BootstrapOperations
{
    /// <summary>The operation's argument, <c>issue</c> or <c>recover</c>. An undefined value fails with <c>core.value.invalid</c>.</summary>
    public static string ToName(this BootstrapOperation operation) => operation switch
    {
        BootstrapOperation.Issue => "issue",
        BootstrapOperation.Recover => "recover",
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };

    /// <summary>The purpose of the token the operation issues. An undefined value fails with <c>core.value.invalid</c>.</summary>
    public static OperatorTokenPurpose ToPurpose(this BootstrapOperation operation) => operation switch
    {
        BootstrapOperation.Issue => OperatorTokenPurpose.Bootstrap,
        BootstrapOperation.Recover => OperatorTokenPurpose.Recovery,
        _ => throw new GatewayException(ErrorCodes.ValueInvalid),
    };
}
