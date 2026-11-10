namespace Iskender.Infrastructure.PostgreSql.Tests.Fixture;

/// <summary>
/// One database per test class, created from a template and dropped when the class ends, so classes run in parallel
/// without sharing rows (research R19). A test class takes a derived type as its class fixture.
/// </summary>
public abstract class ClassDatabase : IAsyncLifetime
{
    protected ClassDatabase(PostgreSqlFixture fixture, string template)
    {
        Fixture = fixture;
        Template = template;
    }

    public PostgreSqlFixture Fixture { get; }

    public string Template { get; }

    public string Name { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        if (Fixture.UnavailableReason is null)
        {
            Name = await Fixture.CreateDatabaseAsync(Template);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Name.Length > 0)
        {
            await Fixture.DropDatabaseAsync(Name);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Skips or fails the calling test unless the container is running.</summary>
    public void RequirePostgreSql() => Fixture.RequirePostgreSql();

    /// <summary>An unpooled connection string to this database as the runtime login.</summary>
    public string RuntimeConnectionString() => Fixture.ConnectionString(Name, PostgreSqlFixture.RuntimeLogin, Fixture.RuntimePassword);

    /// <summary>An unpooled connection string to this database as the migration login.</summary>
    public string MigratorConnectionString() => Fixture.ConnectionString(Name, PostgreSqlFixture.MigratorLogin, Fixture.MigratorPassword);
}

/// <summary>A class database copied from the empty template.</summary>
public sealed class EmptyDatabase(PostgreSqlFixture fixture) : ClassDatabase(fixture, PostgreSqlFixture.EmptyTemplate);
