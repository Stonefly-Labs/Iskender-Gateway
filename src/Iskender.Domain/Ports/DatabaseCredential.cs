using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// A database password for one connection open. <see cref="ToString"/> returns <c>[redacted]</c>, and it offers no
/// equality, so it cannot leak through formatting or comparison. Owned by ISK-0004.
/// </summary>
public sealed class DatabaseCredential
{
    public DatabaseCredential(string password)
    {
        Password = password ?? throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    public string Password { get; }

    public override string ToString() => "[redacted]";
}
