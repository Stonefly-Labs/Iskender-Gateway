using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// A coordinator password. <see cref="ToString"/> returns <c>[redacted]</c>, and it offers no equality, so it cannot leak
/// through formatting or comparison (contract Credentials rule 3). Owned by ISK-0005.
/// </summary>
public sealed class CoordinationCredential
{
    public CoordinationCredential(string password)
    {
        Password = password ?? throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    public string Password { get; }

    public override string ToString() => "[redacted]";
}
