using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Tests.Fixture;

/// <summary>The adapter wired to a class database: data sources, a unit-of-work factory, the test credentials, and a capturing logger.</summary>
internal sealed class TestStore : IAsyncDisposable
{
    private TestStore(PostgreSqlDataSources sources, TestCredentialProvider credentials, TimeProvider timeProvider)
    {
        Sources = sources;
        Credentials = credentials;
        Factory = new PostgreSqlUnitOfWorkFactory(sources, timeProvider, Logger);
    }

    public PostgreSqlDataSources Sources { get; }

    public TestCredentialProvider Credentials { get; }

    public CapturingDiagnostics.Logger Logger { get; } = new();

    public PostgreSqlUnitOfWorkFactory Factory { get; }

    public static TestStore For(ClassDatabase database, IReadOnlyDictionary<string, object>? overrides = null, TimeProvider? timeProvider = null)
    {
        var credentials = TestCredentialProvider.For(database.Fixture);
        return new TestStore(new PostgreSqlDataSources(TestSettings.For(database, overrides), credentials), credentials, timeProvider ?? TimeProvider.System);
    }

    public static UnitOfWorkOptions Options(string operation = "test.op", UnitOfWorkIsolation isolation = UnitOfWorkIsolation.ReadCommitted) =>
        new("alpha", operation) { Isolation = isolation };

    public ValueTask<IUnitOfWork> OpenAsync(UnitOfWorkScope scope, UnitOfWorkOptions? options = null) =>
        Factory.OpenAsync(scope, options ?? Options(), TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => Sources.DisposeAsync();
}
