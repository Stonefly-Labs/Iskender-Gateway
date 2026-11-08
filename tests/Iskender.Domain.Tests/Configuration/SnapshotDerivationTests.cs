using System.Reflection;
using Iskender.Core.Contracts.Configuration;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>
/// Quickstart scenario 13 and research R10: no production type outside <c>Iskender.Domain.Registry</c> derives from the
/// snapshot views. It scans the production assemblies of this repository that this test project can reach.
/// </summary>
public class SnapshotDerivationTests
{
    private const string AllowedNamespace = "Iskender.Domain.Registry";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Iskender.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }

    /// <summary>Each project under <c>src/</c> whose assembly is in the test output.</summary>
    private static List<Assembly> ReachableProductionAssemblies() =>
        Directory.GetDirectories(Path.Combine(RepositoryRoot(), "src"))
            .Select(Path.GetFileName)
            .Where(name => File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".dll")))
            .Select(name => Assembly.Load(new AssemblyName(name!)))
            .ToList();

    [Fact]
    public void OnlyTheRegistryDerivesFromTheSnapshotViews()
    {
        var assemblies = ReachableProductionAssemblies();

        var outsiders = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(ConfigurationSnapshot).IsAssignableFrom(type) || typeof(ConfigurationSection).IsAssignableFrom(type))
            .Where(type => type != typeof(ConfigurationSnapshot) && type != typeof(ConfigurationSection))
            .Where(type => type.Namespace != AllowedNamespace)
            .Select(type => type.FullName);

        Assert.Empty(outsiders);
    }

    [Fact]
    public void TheScanReachesTheContractsAndTheEngineAndFindsTheRegistryTypes()
    {
        var assemblies = ReachableProductionAssemblies();

        Assert.Contains(assemblies, assembly => assembly.GetName().Name == "Iskender.Core.Contracts");
        Assert.Contains(assemblies, assembly => assembly.GetName().Name == "Iskender.Domain");
        Assert.Contains(typeof(Iskender.Domain.Registry.ConfigurationValidator).Assembly.GetTypes(),
            type => type.IsSubclassOf(typeof(ConfigurationSnapshot)) && type.Namespace == AllowedNamespace);
    }
}
