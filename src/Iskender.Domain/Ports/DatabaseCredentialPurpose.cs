namespace Iskender.Domain.Ports;

/// <summary>Which login a credential is for. Owned by ISK-0004.</summary>
public enum DatabaseCredentialPurpose
{
    Runtime,
    Migration,
}
