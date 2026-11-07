using System.Reflection;
using NetArchTest.Rules;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>
/// Rules A17 and A18: the module namespaces inside Iskender.Domain. NetArchTest's namespace selection matches by
/// prefix, so selection here uses an exact namespace or its children (research R7).
/// </summary>
public static class ModuleRules
{
    private const string DomainAssembly = "Iskender.Domain";

    public static IEnumerable<Violation> Evaluate(RuleContext context) =>
        Evaluate(context.Source, Assembly.Load(DomainAssembly));

    public static IReadOnlyList<Violation> Evaluate(RuleSource source, Assembly assembly)
    {
        var violations = new List<Violation>();
        var shared = source.ModuleNamespaces.Shared;
        var modules = source.Modules
            .Where(module => string.Equals(module.Assembly, DomainAssembly, StringComparison.Ordinal))
            .Select(module => module.NamespaceRoot)
            .ToList();

        foreach (var type in Types.InAssembly(assembly).GetTypes())
        {
            var home = type.Namespace ?? string.Empty;
            if (!modules.Append(shared).Any(module => BelongsTo(home, module)))
            {
                violations.Add(source.Violate("A18", type.FullName!, home.Length == 0 ? "(global namespace)" : home, "is not in a mapped namespace or the shared one"));
            }
        }

        foreach (var used in modules)
        {
            var dependents = Types.InAssembly(assembly).That().HaveDependencyOn(used).GetTypes().ToList();

            foreach (var type in dependents.Where(type => BelongsTo(type.Namespace, shared)))
            {
                violations.Add(source.Violate("A18", type.FullName!, used, "the shared namespace may use no module namespace"));
            }

            foreach (var module in modules.Where(module => module != used && !IsAllowed(source, module, used)))
            {
                foreach (var type in dependents.Where(type => BelongsTo(type.Namespace, module)))
                {
                    violations.Add(source.Violate("A17", type.FullName!, used, $"{module} may not use another module's namespace"));
                }
            }
        }

        return Violation.Normalize(violations, source.RuleOrder);
    }

    private static bool BelongsTo(string? typeNamespace, string root) =>
        string.Equals(typeNamespace, root, StringComparison.Ordinal)
        || (typeNamespace?.StartsWith($"{root}.", StringComparison.Ordinal) ?? false);

    private static bool IsAllowed(RuleSource source, string from, string to) =>
        source.ModuleNamespaces.AllowList.Any(entry =>
            string.Equals(entry.From, from, StringComparison.Ordinal) && string.Equals(entry.To, to, StringComparison.Ordinal));
}
