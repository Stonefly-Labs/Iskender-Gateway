namespace Iskender.Domain.Ports;

/// <summary>
/// Resolves the Redis password when the adapter builds its connection, and again after an authentication failure
/// (contract Credentials rule 1, research R11). The host implements it, and ISK-0005 ships no implementation outside test
/// projects. Owned by ISK-0005.
/// </summary>
public interface ICoordinationCredentialProvider
{
    ValueTask<CoordinationCredential> GetCredentialAsync(CoordinationCredentialRequest request, CancellationToken cancellationToken);
}
