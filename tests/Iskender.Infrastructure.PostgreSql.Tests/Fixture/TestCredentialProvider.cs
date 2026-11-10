using System.Collections.Concurrent;
using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Tests.Fixture;

/// <summary>
/// The only <see cref="IDatabaseCredentialProvider"/> in the repository (FR-031): returns passwords the fixture
/// generated, by secret reference name, and records every request so tests can count physical opens.
/// </summary>
internal sealed class TestCredentialProvider : IDatabaseCredentialProvider
{
    private readonly IReadOnlyDictionary<string, string> _passwords;
    private readonly ConcurrentQueue<DatabaseCredentialRequest> _requests = new();

    public TestCredentialProvider(IReadOnlyDictionary<string, string> passwords)
    {
        _passwords = passwords;
    }

    /// <summary>When set, every request throws this instead of answering.</summary>
    public Exception? Failure { get; set; }

    public IReadOnlyList<DatabaseCredentialRequest> Requests => [.. _requests];

    public ValueTask<DatabaseCredential> GetCredentialAsync(DatabaseCredentialRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        if (Failure is { } failure)
        {
            throw failure;
        }

        return _passwords.TryGetValue(request.Reference.Name, out var password)
            ? ValueTask.FromResult(new DatabaseCredential(password))
            : throw new InvalidOperationException("No test password for this reference.");
    }

    /// <summary>A provider whose secret references are the fixture's.</summary>
    public static TestCredentialProvider For(PostgreSqlFixture fixture) => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [TestSettings.RuntimeSecret.Name] = fixture.RuntimePassword,
        [TestSettings.MigrationSecret.Name] = fixture.MigratorPassword,
    });

    public static SecretReference Reference(string name) => new(name);
}
