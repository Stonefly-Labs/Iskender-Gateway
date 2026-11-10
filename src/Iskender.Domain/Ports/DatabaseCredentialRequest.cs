using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>The configured secret reference and the login it is for. Owned by ISK-0004.</summary>
public sealed class DatabaseCredentialRequest
{
    public DatabaseCredentialRequest(SecretReference reference, DatabaseCredentialPurpose purpose)
    {
        Reference = reference ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        Purpose = Enum.IsDefined(purpose) ? purpose : throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    public SecretReference Reference { get; }

    public DatabaseCredentialPurpose Purpose { get; }
}
