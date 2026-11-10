using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;
using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Connections;

public class DataSourceTests(EmptyDatabase database) : IClassFixture<EmptyDatabase>
{
    private const string Planted = "planted-provider-text-7d1c";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void RequireMapsToVerifyFullAndDisableToDisable()
    {
        var document = "{\"iskender\":{\"persistence\":{\"host\":\"h\",\"database\":\"d\",\"username\":\"u\",\"password\":{\"secretRef\":\"r\"}TLS}}}";
        var require = TestSettings.Read(document.Replace("TLS", "", StringComparison.Ordinal), environment: "production");
        var disable = TestSettings.Read(document.Replace("TLS", ",\"tlsMode\":\"disable\"", StringComparison.Ordinal));

        Assert.Equal(SslMode.VerifyFull, PostgreSqlDataSources.ConnectionString(require, "u").SslMode);
        Assert.Equal(SslMode.Disable, PostgreSqlDataSources.ConnectionString(disable, "u").SslMode);
    }

    [Fact]
    public void TheConnectionStringCarriesThePoolBoundsAndTimeoutButNoPassword()
    {
        var settings = TestSettings.Read("{\"iskender\":{\"persistence\":{\"host\":\"h\",\"database\":\"d\",\"username\":\"u\",\"password\":{\"secretRef\":\"r\"},\"poolMinimum\":2,\"poolMaximum\":7,\"connectionTimeout\":\"9s\"}}}", "production");

        var builder = PostgreSqlDataSources.ConnectionString(settings, "u");

        Assert.Equal(2, builder.MinPoolSize);
        Assert.Equal(7, builder.MaxPoolSize);
        Assert.Equal(9, builder.Timeout);
        Assert.Null(builder.Password);
        Assert.False(builder.IncludeErrorDetail);
        Assert.False(builder.NoResetOnClose); // the pool resets a returned connection, so nothing outlives a unit (FR-008)
    }

    [Fact]
    public async Task MissingMigrationCredentialsFailBeforeConnecting()
    {
        var settings = TestSettings.Read("{\"iskender\":{\"persistence\":{\"host\":\"h\",\"database\":\"d\",\"username\":\"u\",\"password\":{\"secretRef\":\"r\"},\"migration\":{\"username\":\"m\"}}}}", "production");
        var provider = new TestCredentialProvider(new Dictionary<string, string>());
        await using var sources = new PostgreSqlDataSources(settings, provider);

        var failure = Assert.Throws<GatewayException>(() => sources.Migration);

        Assert.Same(ErrorCodes.PersistenceUnavailable, failure.Code);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task TheProviderIsAskedOncePerPhysicalOpenWithTheReferenceAndPurpose()
    {
        database.RequirePostgreSql();
        var provider = TestCredentialProvider.For(database.Fixture);
        await using var sources = new PostgreSqlDataSources(TestSettings.For(database), provider);

        await (await sources.Runtime.OpenConnectionAsync(Token)).DisposeAsync();
        await (await sources.Runtime.OpenConnectionAsync(Token)).DisposeAsync();
        Assert.Single(provider.Requests);

        sources.Runtime.Clear();
        await (await sources.Runtime.OpenConnectionAsync(Token)).DisposeAsync();
        await (await sources.Migration.OpenConnectionAsync(Token)).DisposeAsync();

        Assert.Equal(3, provider.Requests.Count);
        Assert.All(provider.Requests.Take(2), request =>
        {
            Assert.Equal(TestSettings.RuntimeSecret, request.Reference);
            Assert.Equal(DatabaseCredentialPurpose.Runtime, request.Purpose);
        });
        Assert.Equal(TestSettings.MigrationSecret, provider.Requests[2].Reference);
        Assert.Equal(DatabaseCredentialPurpose.Migration, provider.Requests[2].Purpose);
    }

    [Fact]
    public async Task NoProviderFailsAsAuthenticationWithPersistenceUnavailable()
    {
        database.RequirePostgreSql();
        await using var sources = new PostgreSqlDataSources(TestSettings.For(database), credentials: null);

        var result = await OpenFailure(sources);

        Assert.Same(ErrorCodes.PersistenceUnavailable, result.Failure.Code);
        Assert.Equal(PersistenceFailureClass.AuthenticationFailed, result.Failure.FailureClass);
    }

    [Fact]
    public async Task AThrowingProviderFailsAndNoneOfItsTextSurvives()
    {
        database.RequirePostgreSql();
        var provider = TestCredentialProvider.For(database.Fixture);
        provider.Failure = new InvalidOperationException(Planted);
        await using var sources = new PostgreSqlDataSources(TestSettings.For(database), provider);

        var caught = await Assert.ThrowsAnyAsync<Exception>(async () => await sources.Runtime.OpenConnectionAsync(Token));
        var result = FailureClassifier.Classify(caught, FailureSite.Statement, FiredToken.None);

        Assert.Equal(PersistenceFailureClass.AuthenticationFailed, result.Failure.FailureClass);
        Assert.DoesNotContain(Planted, result.ToException().ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Planted, Chain(caught), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWrongPasswordFailsWithPersistenceUnavailable()
    {
        database.RequirePostgreSql();
        var provider = new TestCredentialProvider(new Dictionary<string, string> { [TestSettings.RuntimeSecret.Name] = "wrong-password" });
        await using var sources = new PostgreSqlDataSources(TestSettings.For(database), provider);

        var result = await OpenFailure(sources);

        Assert.Same(ErrorCodes.PersistenceUnavailable, result.Failure.Code);
        Assert.Equal(PersistenceFailureClass.AuthenticationFailed, result.Failure.FailureClass);
    }

    private static async Task<Classification> OpenFailure(PostgreSqlDataSources sources)
    {
        var caught = await Assert.ThrowsAnyAsync<Exception>(async () => await sources.Runtime.OpenConnectionAsync(Token));
        return FailureClassifier.Classify(caught, FailureSite.Statement, FiredToken.None);
    }

    private static string Chain(Exception exception)
    {
        var text = new System.Text.StringBuilder();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            text.AppendLine(current.Message);
        }

        return text.ToString();
    }
}
