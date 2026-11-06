using System.Xml.Linq;

namespace Iskender.Architecture.Tests;

/// <summary>
/// The ProjectReference graph parsed from .csproj files. The compiler drops unused references from an
/// empty assembly, so this graph, not assembly metadata, is what enforces direction on the skeleton.
/// </summary>
public sealed class ProjectGraph
{
    /// <summary>The approved direction from contracts/module-dependency-rules.md, as an allow-list.</summary>
    private static readonly Dictionary<string, string[]> ApprovedReferences = new()
    {
        ["Iskender.Core.Contracts"] = [],
        ["Iskender.Providers.Abstractions"] = ["Iskender.Core.Contracts"],
        ["Iskender.Policy.Sdk"] = ["Iskender.Core.Contracts"],
        ["Iskender.Domain"] = ["Iskender.Core.Contracts"],
        ["Iskender.Infrastructure"] = ["Iskender.Domain", "Iskender.Core.Contracts"],
        ["Iskender.Management"] = ["Iskender.Core.Contracts"],
        ["Iskender.Gateway.Host"] =
        [
            "Iskender.Core.Contracts",
            "Iskender.Domain",
            "Iskender.Infrastructure",
            "Iskender.Providers.Abstractions",
            "Iskender.Policy.Sdk",
            "Iskender.Management",
        ],
    };

    public ProjectGraph(IReadOnlyDictionary<string, IReadOnlyCollection<string>> edges)
    {
        Edges = edges;
    }

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Edges { get; }

    public static ProjectGraph Load(params string[] directories)
    {
        var edges = new Dictionary<string, IReadOnlyCollection<string>>();
        var paths = directories.SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories));
        foreach (var path in paths)
        {
            var references = XDocument.Load(path).Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .OfType<string>()
                .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
                .ToList();
            edges[Path.GetFileNameWithoutExtension(path)] = references;
        }

        return new ProjectGraph(edges);
    }

    /// <summary>Returns every violation of the approved-direction rules A1–A5, or none.</summary>
    public IReadOnlyList<string> Violations()
    {
        var violations = new List<string>();

        foreach (var reference in ReferencesOf("Iskender.Core.Contracts"))
        {
            violations.Add($"A1: Iskender.Core.Contracts must have no internal references but references {reference}.");
        }

        foreach (var (project, references) in Edges)
        {
            if (references.Contains("Iskender.Gateway.Host"))
            {
                violations.Add($"A2: {project} references Iskender.Gateway.Host, which must be referenced by none.");
            }
        }

        foreach (var project in new[] { "Iskender.Domain", "Iskender.Providers.Abstractions", "Iskender.Policy.Sdk", "Iskender.Management" })
        {
            if (ReferencesOf(project).Contains("Iskender.Infrastructure"))
            {
                violations.Add($"A3: {project} must not reference Iskender.Infrastructure.");
            }
        }

        violations.AddRange(Cycles().Select(cycle => $"A4: reference cycle {cycle}."));
        violations.AddRange(UnapprovedEdges());

        return violations;
    }

    private IEnumerable<string> UnapprovedEdges()
    {
        foreach (var (project, references) in Edges)
        {
            // Test projects may reference what they test. A2 still applies to them.
            if (project.EndsWith(".Tests", StringComparison.Ordinal))
            {
                continue;
            }

            if (!ApprovedReferences.TryGetValue(project, out var approved))
            {
                yield return $"A5: {project} is not in the approved dependency direction.";
                continue;
            }

            foreach (var reference in references.Except(approved))
            {
                yield return $"A5: {project} -> {reference} is not in the approved dependency direction.";
            }
        }
    }

    private IReadOnlyCollection<string> ReferencesOf(string project) =>
        Edges.TryGetValue(project, out var references) ? references : [];

    private List<string> Cycles()
    {
        var cycles = new List<string>();
        var finished = new HashSet<string>();

        void Visit(string project, List<string> path)
        {
            var seenAt = path.IndexOf(project);
            if (seenAt >= 0)
            {
                cycles.Add(string.Join(" -> ", path.Skip(seenAt).Append(project)));
                return;
            }

            if (!finished.Add(project))
            {
                return;
            }

            path.Add(project);
            foreach (var reference in ReferencesOf(project))
            {
                Visit(reference, path);
            }

            path.RemoveAt(path.Count - 1);
        }

        foreach (var project in Edges.Keys)
        {
            Visit(project, []);
        }

        return cycles;
    }
}
