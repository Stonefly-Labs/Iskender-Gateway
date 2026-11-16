namespace Iskender.Container.Tests.Fixture.Database;

/// <summary>
/// A database of its own on <see cref="DatabaseServer"/>, created from the empty template and dropped afterwards: as a
/// class fixture, or with <see cref="CreateAsync"/> for one test.
/// </summary>
public sealed class PlaneDatabase(DatabaseServer server) : IAsyncLifetime
{
    public DatabaseServer Server { get; } = server;

    public string Name { get; private set; } = "";

    public static async Task<PlaneDatabase> CreateAsync(DatabaseServer server)
    {
        var database = new PlaneDatabase(server);
        await database.InitializeAsync();
        return database;
    }

    public async ValueTask InitializeAsync()
    {
        if (Server.UnavailableReason is null)
        {
            Name = await Server.CreateDatabaseAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Name.Length > 0)
        {
            await Server.DropDatabaseAsync(Name);
        }
    }

    public void RequirePostgreSql() => Server.RequirePostgreSql();

    /// <summary>A document with only a <c>persistence</c> section naming this database; see <see cref="DatabaseDocuments.Document"/>.</summary>
    public string Document(IReadOnlyDictionary<string, object>? overrides = null, bool withMigration = true) =>
        DatabaseDocuments.Document(this, overrides, withMigration);

    public string RuntimeConnectionString() => Server.ConnectionString(Name, DatabaseServer.RuntimeLogin, Server.RuntimePassword);
}
