using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>The configured secret reference for the coordinator's password. Owned by ISK-0005.</summary>
public sealed class CoordinationCredentialRequest
{
    public CoordinationCredentialRequest(SecretReference reference)
    {
        Reference = reference ?? throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    public SecretReference Reference { get; }
}
