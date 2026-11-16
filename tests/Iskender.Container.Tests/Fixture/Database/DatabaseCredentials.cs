using System.Collections.Concurrent;
using Iskender.Domain.Ports;

namespace Iskender.Container.Tests.Fixture.Database;

/// <summary>A credential provider that answers known secret reference names with fixed passwords and records each request.</summary>
public sealed class DatabaseCredentials(IReadOnlyDictionary<string, string> passwords) : IDatabaseCredentialProvider
{
    private readonly ConcurrentQueue<DatabaseCredentialRequest> _requests = new();

    public IReadOnlyList<DatabaseCredentialRequest> Requests => [.. _requests];

    public ValueTask<DatabaseCredential> GetCredentialAsync(DatabaseCredentialRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        return passwords.TryGetValue(request.Reference.Name, out var password)
            ? ValueTask.FromResult(new DatabaseCredential(password))
            : throw new InvalidOperationException("No test password for this reference.");
    }
}
