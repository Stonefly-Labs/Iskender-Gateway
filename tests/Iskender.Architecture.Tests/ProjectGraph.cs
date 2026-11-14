namespace Iskender.Architecture.Tests;

/// <summary>
/// The ProjectReference graph from the repository scan. The compiler drops unused references from an empty
/// assembly, so this graph, not assembly metadata, is what enforces direction on the skeleton. Names resolve
/// through the rule source, and the test code holds no copy of its contents.
/// </summary>
public sealed class ProjectGraph
{
    private readonly RuleSource source;

    private readonly Dictionary<string, Node> nodes = new(StringComparer.OrdinalIgnoreCase);

    public ProjectGraph(RuleSource source, IEnumerable<Node> nodes)
    {
        this.source = source;

        var all = nodes.ToList();
        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in all)
        {
            canonical.TryAdd(node.Name, node.Name);
        }

        // Two project files can share a name, which A14 reports. Merging them keeps every edge visible, and a
        // production file among them keeps the merged node out of the test-project exemption.
        foreach (var group in all.GroupBy(node => canonical[node.Name], StringComparer.OrdinalIgnoreCase))
        {
            this.nodes[group.Key] = new Node(
                group.Key,
                group.All(node => node.IsUnderTests),
                [.. group.SelectMany(node => node.References)
                    .Select(reference => canonical.GetValueOrDefault(reference, reference))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)]);
        }
    }

    public IReadOnlyList<Node> Nodes => [.. nodes.Values.OrderBy(node => node.Name, StringComparer.Ordinal)];

    public static ProjectGraph From(RuleSource source, IEnumerable<ScannedProject> projects) =>
        new(source, projects.Select(project => new Node(
            project.Name,
            project.IsUnderTests,
            [.. project.References.Select(reference => reference.TargetName)])));

    public bool Contains(string project) => nodes.ContainsKey(project);

    /// <summary>Whether a project in the graph sits under <c>tests/</c>. A project outside the scan does not.</summary>
    public bool IsUnderTests(string project) => nodes.TryGetValue(project, out var node) && node.IsUnderTests;

    public IReadOnlyList<string> ReferencesOf(string project) =>
        nodes.TryGetValue(project, out var node) ? node.References : [];

    /// <summary>Direct check: whether a project's definition allows a reference to the target.</summary>
    public bool IsApproved(ProjectDefinition definition, string target) =>
        definition.References.Any(entry => source.Matches(entry, target));

    /// <summary>
    /// Everything a project reaches, each with one path to it, walking depth first and tolerating cycles. Direct
    /// references are recorded before the walk descends, so a direct edge is never reported as a longer path. A
    /// project never counts as reaching itself, since a cycle is A4's concern. A reference to a project that is
    /// not in the scan stays visible here.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Reach(string project)
    {
        var reached = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { project };

        void Walk(string current, IReadOnlyList<string> path)
        {
            var next = ReferencesOf(current).Where(seen.Add).ToList();
            foreach (var reference in next)
            {
                reached[reference] = [.. path, reference];
            }

            foreach (var reference in next)
            {
                Walk(reference, reached[reference]);
            }
        }

        Walk(project, []);
        return reached;
    }

    /// <summary>Reference cycles over all nodes, each listed from its smallest name and closing on it.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Cycles()
    {
        var cycles = new List<IReadOnlyList<string>>();
        var finished = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string project, List<string> path)
        {
            var seenAt = path.FindIndex(entry => string.Equals(entry, project, StringComparison.OrdinalIgnoreCase));
            if (seenAt >= 0)
            {
                var members = path.Skip(seenAt).ToList();
                var start = members.IndexOf(members.Min(StringComparer.Ordinal)!);
                cycles.Add([.. members.Skip(start), .. members.Take(start), members[start]]);
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

        foreach (var node in Nodes)
        {
            Visit(node.Name, []);
        }

        return cycles;
    }

    public sealed record Node(string Name, bool IsUnderTests, IReadOnlyList<string> References);
}
