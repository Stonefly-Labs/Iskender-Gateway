using System.Reflection;
using System.Runtime.CompilerServices;
using Iskender.Infrastructure.PostgreSql;

namespace Iskender.Infrastructure.PostgreSql.Tests;

public class AdapterAssemblyTests
{
    [Fact]
    public void TheAdapterGrantsInternalsOnlyToThisTestProject()
    {
        var grants = typeof(PostgreSqlAdapter).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(grant => grant.AssemblyName);

        Assert.Equal(["Iskender.Infrastructure.PostgreSql.Tests"], grants);
    }

    [Fact]
    public void TheAdapterReferencesNpgsqlAndNoOtherDataAccessLibrary()
    {
        var references = typeof(PostgreSqlAdapter).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToList();

        Assert.Contains("Npgsql", references);
        Assert.DoesNotContain(references, name => name.StartsWith("Dapper", StringComparison.Ordinal) || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }
}
