namespace Iskender.Infrastructure.PostgreSql.Tests.Security;

/// <summary>
/// ISK-0009 FR-024: the <c>Directory</c> and <c>Work</c> namespaces and their migration scripts take no advisory lock.
/// Coordination there is only through PostgreSQL-issued leases and the fencing guard. The migration runner's own lock is
/// ISK-0004's and is outside both namespaces.
/// </summary>
public class NoAdvisoryLockInWorkTests
{
    private static readonly string[] Forbidden = ["pg_advisory", "pg_try_advisory", "IAdvisoryLock"];

    private static readonly string[] Folders = ["Directory", "Work"];

    [Fact]
    public void TheDirectoryAndWorkSourcesAndScriptsUseNoAdvisoryLock()
    {
        var files = Sources();

        Assert.Contains(files, file => file.Path.EndsWith("Directory/ScopeDirectory.cs", StringComparison.Ordinal));
        Assert.Contains(files, file => file.Path.EndsWith("Work/WorkDispatcher.cs", StringComparison.Ordinal));
        Assert.Contains(files, file => file.Path.EndsWith("Work/Scripts/0001_work.up.sql", StringComparison.Ordinal));
        Assert.Contains(files, file => file.Path.EndsWith("Directory/Scripts/0001_directory.up.sql", StringComparison.Ordinal));
        Assert.Empty(files.SelectMany(file => Violations(file.Path, file.Text)));
    }

    [Theory]
    [InlineData("SELECT pg_advisory_xact_lock(42)")]
    [InlineData("select PG_TRY_ADVISORY_LOCK(1)")]
    [InlineData("private readonly IAdvisoryLock _lock;")]
    public void EachForbiddenUseIsFound(string text) => Assert.Single(Violations("Sample.cs", text));

    private static IEnumerable<string> Violations(string path, string text) =>
        Forbidden.Where(name => text.Contains(name, StringComparison.OrdinalIgnoreCase)).Select(name => $"{path}: uses {name}");

    // Every .cs and .sql file of the adapter's Directory and Work folders, which hold exactly those namespaces.
    private static List<(string Path, string Text)> Sources()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Iskender.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("The repository root was not found.");
        }

        var adapter = Path.Combine(root, "src", "Iskender.Infrastructure.PostgreSql");
        return [.. Folders
            .SelectMany(folder => System.IO.Directory.EnumerateFiles(Path.Combine(adapter, folder), "*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".sql", StringComparison.Ordinal))
            .Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)))];
    }
}
