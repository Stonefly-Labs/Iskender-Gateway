namespace Iskender.Domain.Ports;

/// <summary>
/// Resolves a database password for one physical connection open (FR-031, research R17). The host implements it, and
/// ISK-0004 ships no implementation outside test projects. Owned by ISK-0004.
/// </summary>
public interface IDatabaseCredentialProvider
{
    ValueTask<DatabaseCredential> GetCredentialAsync(DatabaseCredentialRequest request, CancellationToken cancellationToken);
}
