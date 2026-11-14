namespace Iskender.Architecture.Tests.Rules;

/// <summary>Rules A1 to A12 and A20, evaluated from the rule source over the project graph.</summary>
public static class GraphRules
{
    /// <summary>The one project rule A1 names.</summary>
    private const string CoreContracts = "Iskender.Core.Contracts";

    public static IEnumerable<Violation> Evaluate(RuleContext context) => Evaluate(context.Source, context.Graph);

    public static IReadOnlyList<Violation> Evaluate(RuleSource source, ProjectGraph graph)
    {
        var violations = new List<Violation>();

        foreach (var reference in graph.ReferencesOf(CoreContracts))
        {
            violations.Add(source.Violate("A1", CoreContracts, reference, "must reference no other Iskender project"));
        }

        foreach (var entry in source.ForbiddenReach)
        {
            foreach (var node in graph.Nodes.Where(node => entry.From.Any(from => source.MatchesReach(from, node.Name, node.IsUnderTests))))
            {
                foreach (var (target, path) in graph.Reach(node.Name))
                {
                    if (entry.To.Any(to => source.MatchesReach(to, target, graph.IsUnderTests(target))))
                    {
                        violations.Add(source.Violate(entry.Rule, node.Name, string.Join(" -> ", path), "must not reach this project"));
                    }
                }
            }
        }

        foreach (var cycle in graph.Cycles())
        {
            violations.Add(source.Violate("A4", cycle[0], string.Join(" -> ", cycle.Skip(1)), "is a reference cycle"));
        }

        foreach (var node in graph.Nodes.Where(node => !node.IsUnderTests))
        {
            var definition = source.Resolve(node.Name);
            if (definition is null)
            {
                violations.Add(source.Violate("A5", node.Name, "(approved references)", "is not listed and matches no pattern"));
                continue;
            }

            foreach (var reference in node.References.Where(reference => !graph.IsApproved(definition, reference)))
            {
                violations.Add(source.Violate("A5", node.Name, reference, "not an approved reference"));
            }
        }

        foreach (var (project, required) in source.RequiredReferences)
        {
            foreach (var reference in required.Where(reference => !graph.ReferencesOf(project).Contains(reference, StringComparer.Ordinal)))
            {
                violations.Add(source.Violate("A12", project, reference, "is a required direct reference"));
            }
        }

        return Violation.Normalize(violations, source.RuleOrder);
    }
}
