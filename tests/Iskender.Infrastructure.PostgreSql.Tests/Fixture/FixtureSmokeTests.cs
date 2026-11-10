using Npgsql;

namespace Iskender.Infrastructure.PostgreSql.Tests.Fixture;

public class FixtureSmokeTests(EmptyDatabase database) : IClassFixture<EmptyDatabase>
{
    [Fact]
    public async Task TheRuntimeLoginCanRunSelectOneInAClassDatabase()
    {
        database.RequirePostgreSql();

        await using var connection = new NpgsqlConnection(database.RuntimeConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1, current_user, current_database()", connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(PostgreSqlFixture.RuntimeLogin, reader.GetString(1));
        Assert.Equal(database.Name, reader.GetString(2));
    }
}
