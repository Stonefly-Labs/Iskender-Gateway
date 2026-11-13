using System.Collections.Concurrent;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.Redis.Tests.Fixture;

/// <summary>
/// The only <see cref="ICoordinationCredentialProvider"/> in the repository (contract Credentials rule 2): returns the
/// password the fixture generated, by secret reference name, counts every request, and can throw a planted failure.
/// </summary>
internal sealed class TestCoordinationCredentialProvider(IReadOnlyDictionary<string, string> passwords) : ICoordinationCredentialProvider
{
    private readonly ConcurrentQueue<CoordinationCredentialRequest> _requests = new();

    /// <summary>When set, every request throws this instead of answering.</summary>
    public Exception? Failure { get; set; }

    /// <summary>When set, every request answers with this password instead of the fixture's.</summary>
    public string? Override { get; set; }

    public IReadOnlyList<CoordinationCredentialRequest> Requests => [.. _requests];

    public int Calls => _requests.Count;

    public ValueTask<CoordinationCredential> GetCredentialAsync(CoordinationCredentialRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Enqueue(request);
        if (Failure is { } failure)
        {
            throw failure;
        }

        return Override is { } replacement ? ValueTask.FromResult(new CoordinationCredential(replacement))
            : passwords.TryGetValue(request.Reference.Name, out var password) ? ValueTask.FromResult(new CoordinationCredential(password))
            : throw new InvalidOperationException("No test password for this reference.");
    }

    /// <summary>A provider whose one secret reference, <see cref="TestCoordinationSettings.PasswordSecret"/>, is the fixture's adapter password.</summary>
    public static TestCoordinationCredentialProvider For(RedisFixture fixture) =>
        new(new Dictionary<string, string>(StringComparer.Ordinal) { [TestCoordinationSettings.PasswordSecret.Name] = fixture.Password });
}
