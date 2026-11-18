namespace Iskender.Infrastructure.PostgreSql.Directory;

/// <summary>The directory module's one table descriptor, declared once (ISK-0009 data model "isk_directory.scopes"). Owned by ISK-0009.</summary>
internal static class DirectoryTables
{
    public static readonly ScopeDirectoryTable Scopes = new("directory", "scopes");
}
