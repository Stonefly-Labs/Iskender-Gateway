using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>Rules M1 and M2: the module map follows docs/03, and every home exists.</summary>
public static class ModuleMapRules
{
    private const string DomainAssembly = "Iskender.Domain";

    private const string AdapterSeparator = ", with adapters in ";

    public static IEnumerable<Violation> Evaluate(RuleContext context)
    {
        foreach (var violation in CompareWithDocument(context))
        {
            yield return violation;
        }

        foreach (var violation in CheckHomes(context))
        {
            yield return violation;
        }
    }

    private static IEnumerable<Violation> CompareWithDocument(RuleContext context)
    {
        var source = context.Source;
        var path = Path.Combine(context.Root, source.ModuleTablePath);
        var tables = File.Exists(path) ? MarkdownTable.UnderHeading(File.ReadLines(path), source.ModuleTableHeading) : null;
        var documented = tables is { Count: > 0 }
            ? tables[0].Rows.Select(row => MarkdownTable.Plain(row[0])).Where(name => name.Length > 0).ToList()
            : null;
        if (documented is null || documented.Count == 0)
        {
            yield return source.Violate("C2", source.ModuleTablePath, source.ModuleTableHeading, "the module table cannot be read or yields no modules");
            yield break;
        }

        var mapped = source.Modules.Select(module => module.Module).ToList();

        foreach (var name in documented.Where(name => !mapped.Contains(name, StringComparer.Ordinal)))
        {
            yield return source.Violate("M1", source.ModuleTablePath, name, "has no entry in the module map");
        }

        foreach (var name in mapped.Where(name => !documented.Contains(name, StringComparer.Ordinal)))
        {
            yield return source.Violate("M1", RuleSource.RelativePath, name, "is in the module map but not in the docs table");
        }

        foreach (var group in mapped.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            yield return source.Violate("M1", RuleSource.RelativePath, group.Key, "has more than one entry in the module map");
        }

        foreach (var group in documented.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            yield return source.Violate("M1", source.ModuleTablePath, group.Key, "is listed more than once in the docs table");
        }
    }

    private static IEnumerable<Violation> CheckHomes(RuleContext context)
    {
        var source = context.Source;

        foreach (var module in source.Modules)
        {
            var parts = module.Assembly.Split(AdapterSeparator, StringSplitOptions.None);
            if (!IsProject(context, parts[0]))
            {
                yield return source.Violate("M2", module.Module, parts[0], "the map entry names a project that does not exist");
            }

            if (parts.Length > 1 && !source.Projects.Any(project => RuleSource.IsPattern(project.Name) && project.Name == parts[1]))
            {
                yield return source.Violate("M2", module.Module, parts[1], "the map entry names a pattern that is not approved");
            }

            var isDomain = string.Equals(parts[0], DomainAssembly, StringComparison.Ordinal);
            if (isDomain && !module.NamespaceRoot.StartsWith($"{DomainAssembly}.", StringComparison.Ordinal))
            {
                yield return source.Violate("M2", module.Module, module.NamespaceRoot, $"a module in {DomainAssembly} must have a child namespace of it");
            }
        }

        foreach (var group in source.Modules.GroupBy(module => module.NamespaceRoot, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            yield return source.Violate("M2", RuleSource.RelativePath, group.Key, "a namespace root must be unique across the map");
        }

        foreach (var port in source.PortHomes)
        {
            if (!IsProject(context, port.Home) && !IsDomainNamespace(source, port.Home))
            {
                yield return source.Violate("M2", port.Item, port.Home, "the port home is not a project or a namespace the map defines");
            }
        }
    }

    private static bool IsProject(RuleContext context, string name) =>
        context.Scan.Projects.Any(project => string.Equals(project.Name, name, StringComparison.Ordinal));

    private static bool IsDomainNamespace(RuleSource source, string home) =>
        string.Equals(home, source.ModuleNamespaces.Shared, StringComparison.Ordinal)
        || source.Modules.Any(module =>
            string.Equals(module.Assembly, DomainAssembly, StringComparison.Ordinal)
            && string.Equals(module.NamespaceRoot, home, StringComparison.Ordinal));
}
