using System.Reflection;
using System.Runtime.CompilerServices;
using Iskender.Infrastructure.PostgreSql;

namespace Iskender.Infrastructure.PostgreSql.Tests;

public class AdapterAssemblyTests
{
    // ISK-0009 grants the work test project its internals too (ISK-0009 T011, T021).
    [Fact]
    public void TheAdapterGrantsInternalsOnlyToItsTestProjects()
    {
        var grants = typeof(PostgreSqlAdapter).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(grant => grant.AssemblyName).Order(StringComparer.Ordinal);

        Assert.Equal(["Iskender.Infrastructure.PostgreSql.Tests", "Iskender.Work.Tests"], grants);
    }

    [Fact]
    public void TheAdapterReferencesNpgsqlAndNoOtherDataAccessLibrary()
    {
        var references = typeof(PostgreSqlAdapter).Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToList();

        Assert.Contains("Npgsql", references);
        Assert.DoesNotContain(references, name => name.StartsWith("Dapper", StringComparison.Ordinal) || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }
}
