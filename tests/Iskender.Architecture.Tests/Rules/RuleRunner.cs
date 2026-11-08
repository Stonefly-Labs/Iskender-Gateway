using System.Reflection;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>Runs every rule family and returns all violations in one sorted list (FR-031).</summary>
public static class RuleRunner
{
    private static readonly Func<RuleContext, IEnumerable<Violation>>[] Families =
    [
        GraphRules.Evaluate,
        ModuleMapRules.Evaluate,
        BoundaryRules.Evaluate,
        ModuleRules.Evaluate,
        BuildConventionRules.Evaluate,
        PackagingRules.Evaluate,
        ContractDocumentRules.Evaluate,
    ];

    public static IReadOnlyList<Violation> Run(string root) => Run(root, loadAssembly: null);

    /// <summary>Runs the rules, loading compiled assemblies with <paramref name="loadAssembly"/> if it is given.</summary>
    public static IReadOnlyList<Violation> Run(string root, Func<string, Assembly>? loadAssembly)
    {
        var load = RuleSource.Load(root);
        if (load.Source is null)
        {
            return load.Violations;
        }

        var source = load.Source;
        var scan = RepositoryScan.Run(root, [.. source.ProtectedSettings.Required.Select(setting => setting.Name)]);
        var context = new RuleContext(root, source, scan, ProjectGraph.From(source, scan.Projects));
        if (loadAssembly is not null)
        {
            context = context with { Load = loadAssembly };
        }

        var violations = scan.Problems
            .Select(problem => source.Violate("C2", problem.Source, problem.Target, problem.Detail))
            .Concat(Families.SelectMany(family => family(context)));

        return Violation.Normalize(violations, source.RuleOrder);
    }
}
