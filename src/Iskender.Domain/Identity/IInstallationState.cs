namespace Iskender.Domain.Identity;

/// <summary>
/// Reads the installation state (operator-security contract, Installation state). Each call opens its own operator-scope
/// unit of work and reads the store, with no cache, and throws the persistence failure as a <c>GatewayException</c> when
/// it cannot read. A caller may keep an <c>initialized</c> answer, because the state never moves back, and never keeps
/// another. Owned by ISK-0010.
/// </summary>
public interface IInstallationState
{
    ValueTask<InstallationStatus> ReadAsync(CancellationToken cancellationToken);
}
