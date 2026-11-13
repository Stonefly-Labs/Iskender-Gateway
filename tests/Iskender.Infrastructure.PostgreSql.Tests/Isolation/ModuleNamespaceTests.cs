using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Iskender.Infrastructure.PostgreSql.Tests.Isolation;

/// <summary>
/// Research R21 and contract Ownership rule 4 (FR-002a): string literals in a module namespace name only the module's own
/// schema and declared tables, and never <c>set_config</c> or an <c>iskender.</c> setting; the framework namespace
/// <c>.Coordination</c>, owned by ISK-0005, names only <c>isk_coordination</c>, and every other framework namespace only
/// <c>isk_migrations</c>. The module namespaces are the ISK-0001 module map's <c>Iskender.Domain</c> modules plus
/// the test modules. Each violation is shown to fail with an in-test sample.
/// </summary>
public partial class ModuleNamespaceTests
{
    private const string Adapter = "Iskender.Infrastructure.PostgreSql";
    private const string TestModules = "Iskender.Infrastructure.PostgreSql.Tests.Modules";

    // Each framework namespace and the one schema its literals may name.
    private static readonly Dictionary<string, string> FrameworkNamespaces = new(StringComparer.Ordinal)
    {
        [Adapter] = "isk_migrations",
        [Adapter + ".Migrations"] = "isk_migrations",
        [Adapter + ".Health"] = "isk_migrations",
        [Adapter + ".Diagnostics"] = "isk_migrations",
        [Adapter + ".Coordination"] = "isk_coordination",
    };

    [Fact]
    public void TheAdapterAndTheTestModulesFollowTheNamespaceRules()
    {
        var root = RepoRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src", Adapter), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tests", Adapter + ".Tests", "Modules"), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)))
            .ToList();

        var check = new Check(ModuleNames(root));

        Assert.Empty(check.Violations(files));
        Assert.Contains(files, file => Namespace(file.Item2) == TestModules + ".Alpha");
        Assert.Contains(files, file => Namespace(file.Item2) == TestModules + ".Beta");
        Assert.Contains(files, file => Namespace(file.Item2) == Adapter + ".Migrations");
        Assert.DoesNotContain("Coordination", check.Modules);
        Assert.Contains("Identity", check.Modules);
        Assert.Contains("Routing", check.Modules);
    }

    public static TheoryData<string, string> Violating() => new()
    {
        { "module alpha names isk_beta", Module("Alpha", "SELECT * FROM isk_beta.counters") },
        { "module alpha names isk_beta", Module("Alpha", "x", verbatim: "@\"SELECT * FROM ISK_BETA.counters\"") },
        { "module alpha names isk_beta", Module("Alpha", "x", verbatim: "\"\"\"\n        SELECT 1 FROM isk_beta.lock_log\n        \"\"\"") },
        { "module alpha names isk_beta", Module("Alpha", "x", verbatim: "$\"SELECT 1 FROM {Table} JOIN isk_beta.t ON {(true ? \"a\" : \"b\")}\"") },
        { "a framework namespace names isk_alpha", $"namespace {Adapter}.Migrations;\ninternal static class S {{ const string Sql = \"SELECT 1 FROM isk_alpha.widgets\"; }}" },
        { "a framework namespace names isk_coordination", $"namespace {Adapter}.Migrations;\ninternal static class S {{ const string Sql = \"SELECT 1 FROM isk_coordination.leases\"; }}" },
        { "a framework namespace names isk_coordination", $"namespace {Adapter};\ninternal static class S {{ const string Sql = \"SELECT 1 FROM isk_coordination.leases\"; }}" },
        { "a framework namespace names isk_migrations", $"namespace {Adapter}.Coordination;\ninternal static class S {{ const string Sql = \"SELECT 1 FROM isk_migrations.history\"; }}" },
        { "a framework namespace names isk_alpha", $"namespace {Adapter}.Coordination;\ninternal static class S {{ const string Sql = \"SELECT 1 FROM isk_alpha.widgets\"; }}" },
        { "a descriptor names module beta", Module("Alpha", "SELECT 1", descriptor: "internal static class T { public static readonly ScopeKeyedTable X = new(\"beta\", \"values\"); }") },
        { "module alpha names public", Module("Alpha", "SELECT * FROM isk_alpha.widgets JOIN public.t ON true") },
        { "module alpha names undeclared table secrets", Module("Alpha", "SELECT * FROM isk_alpha.secrets") },
        { "module alpha names set_config", Module("Alpha", "SELECT set_config('x', 'y', true) FROM isk_alpha.widgets") },
        { "module alpha names set_config or an iskender. setting", Module("Alpha", "SELECT current_setting('iskender.organization_id')") },
        { "a descriptor names module beta", Module("Alpha", "SELECT 1", descriptor: "internal static class T { public static readonly TenantTable X = new(\"beta\", \"counters\"); }") },
        { "a descriptor names module beta", Module("Alpha", "SELECT 1", descriptor: "internal static class T { public static readonly TenantTable X = new(\"beta\", \"notes\", projectOwned: true); }") },
        { "module identity names isk_routing", $"namespace {Adapter}.Identity;\ninternal static class S {{ const string Sql = \"SELECT 1 FROM isk_routing.routes\"; }}" },
    };

    [Theory]
    [MemberData(nameof(Violating))]
    public void EachViolationIsFound(string expected, string source)
    {
        var violations = new Check(["Identity", "Routing"]).Violations([("Sample.cs", source)]);

        Assert.Contains(violations, violation => violation.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void AReferenceOutsideALiteralAndADeclaredTableAreAllowed()
    {
        var source = Module("Alpha", "SELECT id FROM isk_alpha.widgets WHERE id = $1", extra: "// isk_beta.counters set_config iskender.x\n/* isk_beta.counters */ const char Quote = '\"';");

        Assert.Empty(new Check([]).Violations([("Sample.cs", source)]));
    }

    private static string Module(string module, string sql, string? verbatim = null, string? descriptor = null, string extra = "") =>
        $$"""
        namespace {{TestModules}}.{{module}};

        {{descriptor ?? $"internal static class T {{ public static readonly TenantTable Widgets = new(\"{module.ToLowerInvariant()}\", \"widgets\"); }}"}}

        internal static class R
        {
            {{extra}}
            private const string Sql = {{verbatim ?? "\"" + sql.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""}};
        }
        """;

    private static string RepoRoot()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Iskender.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("The repository root was not found.");
        }

        return root;
    }

    // The Iskender.Domain.<Module> namespace roots from the ISK-0001 module map, read only.
    private static string[] ModuleNames(string root)
    {
        using var rules = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "Iskender.Architecture.Tests", "module-dependency-rules.json")));
        return [.. rules.RootElement.GetProperty("modules").EnumerateArray()
            .Select(module => module.GetProperty("namespaceRoot").GetString()!)
            .Where(name => name.StartsWith("Iskender.Domain.", StringComparison.Ordinal))
            .Select(name => name["Iskender.Domain.".Length..].Split('.')[0])];
    }

    private static string? Namespace(string source) => NamespacePattern().Match(source) is { Success: true } match ? match.Groups["name"].Value : null;

    [GeneratedRegex(@"^namespace\s+(?<name>[\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex NamespacePattern();

    private sealed partial class Check(IEnumerable<string> productionModules)
    {
        public HashSet<string> Modules { get; } = productionModules.ToHashSet(StringComparer.Ordinal);

        public List<string> Violations(IReadOnlyList<(string Path, string Source)> files)
        {
            var violations = new List<string>();
            var literals = files.Select(file => (file.Path, Namespace: Namespace(file.Source), file.Source, Literals: StringLiterals(file.Source))).ToList();

            // The tables each module declares, from its TenantTable, OperatorTable, and ScopeKeyedTable descriptors.
            var declared = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var file in literals)
            {
                if (ModuleOf(file.Namespace) is not { } module)
                {
                    continue;
                }

                foreach (Match descriptor in DescriptorPattern().Matches(file.Source))
                {
                    if (descriptor.Groups["module"].Value != module)
                    {
                        violations.Add($"{file.Path}: a descriptor names module {descriptor.Groups["module"].Value}");
                    }

                    (declared.TryGetValue(module, out var tables) ? tables : declared[module] = new(StringComparer.Ordinal)).Add(descriptor.Groups["table"].Value);
                }
            }

            foreach (var file in literals)
            {
                var module = ModuleOf(file.Namespace);
                var framework = file.Namespace is not null && FrameworkNamespaces.TryGetValue(file.Namespace, out var allowed) ? allowed : null;
                foreach (var literal in file.Literals)
                {
                    foreach (Match schema in SchemaPattern().Matches(literal))
                    {
                        var name = schema.Value.ToLowerInvariant();
                        if (module is not null && name != "isk_" + module)
                        {
                            violations.Add($"{file.Path}: module {module} names {name}");
                        }

                        if (framework is not null && name != framework)
                        {
                            violations.Add($"{file.Path}: a framework namespace names {name}");
                        }
                    }

                    if (module is null)
                    {
                        continue;
                    }

                    if (PublicPattern().IsMatch(literal))
                    {
                        violations.Add($"{file.Path}: module {module} names public");
                    }

                    foreach (Match table in QualifiedPattern().Matches(literal))
                    {
                        if (table.Groups["schema"].Value.Equals("isk_" + module, StringComparison.OrdinalIgnoreCase)
                            && !(declared.TryGetValue(module, out var tables) && tables.Contains(table.Groups["table"].Value.ToLowerInvariant())))
                        {
                            violations.Add($"{file.Path}: module {module} names undeclared table {table.Groups["table"].Value}");
                        }
                    }

                    if (literal.Contains("set_config", StringComparison.OrdinalIgnoreCase) || literal.Contains("iskender.", StringComparison.Ordinal))
                    {
                        violations.Add($"{file.Path}: module {module} names set_config or an iskender. setting");
                    }
                }
            }

            return violations;
        }

        // The module a namespace belongs to, lowercased, or null for a namespace that is not a module's.
        private string? ModuleOf(string? name)
        {
            foreach (var prefix in new[] { TestModules + ".", Adapter + "." })
            {
                if (name is not null && name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var segment = name[prefix.Length..].Split('.')[0];
                    if (prefix.StartsWith(TestModules, StringComparison.Ordinal) || Modules.Contains(segment))
                    {
                        return segment.ToLowerInvariant();
                    }
                }
            }

            return null;
        }

        // The contents of every C# string literal: regular, verbatim, raw, and interpolated, skipping comments and chars.
        private static List<string> StringLiterals(string source)
        {
            var literals = new List<string>();
            var i = 0;
            while (i < source.Length)
            {
                if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    i = source.IndexOf('\n', i) is var end && end < 0 ? source.Length : end;
                }
                else if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    i = source.IndexOf("*/", i + 2, StringComparison.Ordinal) is var end && end < 0 ? source.Length : end + 2;
                }
                else if (source[i] == '\'')
                {
                    i++;
                    while (i < source.Length && source[i] != '\'')
                    {
                        i += source[i] == '\\' ? 2 : 1;
                    }

                    i++;
                }
                else if (source[i] == '"' || ((source[i] == '@' || source[i] == '$') && Prefix(source, i) is > 0))
                {
                    var start = i;
                    var verbatim = false;
                    while (source[i] is '@' or '$')
                    {
                        verbatim |= source[i] == '@';
                        i++;
                    }

                    i = ReadString(source, i, verbatim, start, literals);
                }
                else
                {
                    i++;
                }
            }

            return literals;
        }

        // The length of an @/$ prefix before a quote, or 0.
        private static int Prefix(string source, int i)
        {
            var j = i;
            while (j < source.Length && source[j] is '@' or '$')
            {
                j++;
            }

            return j < source.Length && source[j] == '"' ? j - i : 0;
        }

        private static int ReadString(string source, int i, bool verbatim, int start, List<string> literals)
        {
            var interpolated = source.AsSpan(start, i - start).Contains('$');
            var quotes = 0;
            while (i + quotes < source.Length && source[i + quotes] == '"')
            {
                quotes++;
            }

            var text = new StringBuilder();
            if (quotes >= 3)
            {
                var close = new string('"', quotes);
                var end = source.IndexOf(close, i + quotes, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end;
                literals.Add(source[(i + quotes)..end]);
                return Math.Min(source.Length, end + quotes);
            }

            i++;
            while (i < source.Length)
            {
                var c = source[i];
                if (!verbatim && c == '\\')
                {
                    text.Append(source, i, Math.Min(2, source.Length - i));
                    i += 2;
                }
                else if (c == '"' && verbatim && i + 1 < source.Length && source[i + 1] == '"')
                {
                    text.Append('"');
                    i += 2;
                }
                else if (c == '"')
                {
                    i++;
                    break;
                }
                else if (interpolated && c == '{' && i + 1 < source.Length && source[i + 1] != '{')
                {
                    // A hole: keep its text, and step over any string inside it.
                    var depth = 0;
                    do
                    {
                        if (source[i] == '"')
                        {
                            i = ReadString(source, i, verbatim: false, i, literals) - 1;
                        }
                        else
                        {
                            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };
                            text.Append(source[i]);
                        }

                        i++;
                    }
                    while (i < source.Length && depth > 0);
                }
                else
                {
                    text.Append(c);
                    i++;
                }
            }

            literals.Add(text.ToString());
            return i;
        }

        [GeneratedRegex(@"\bisk_[a-z0-9_]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex SchemaPattern();

        [GeneratedRegex(@"\bpublic\s*\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex PublicPattern();

        [GeneratedRegex(@"\b(?<schema>isk_[a-z0-9_]+)\s*\.\s*(?<table>[a-z_][a-z0-9_]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex QualifiedPattern();

        [GeneratedRegex(@"(?:TenantTable|OperatorTable|ScopeKeyedTable)\s*(?:\w+\s*=\s*new\s*)?\(\s*""(?<module>[a-z0-9_]+)""\s*,\s*""(?<table>[a-z0-9_]+)""\s*(?:,[^)]*)?\)", RegexOptions.CultureInvariant)]
        private static partial Regex DescriptorPattern();
    }
}
