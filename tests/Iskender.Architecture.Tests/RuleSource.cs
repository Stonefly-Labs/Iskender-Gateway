using System.Text.Json;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public sealed record ModuleHome(string Module, string Assembly, string NamespaceRoot);

public sealed record PortHome(string Item, string Home, string Owner);

public sealed record ProjectDefinition(string Name, string Role, IReadOnlyList<string> References, IReadOnlyList<string> Denied);

public sealed record TestProjectsDefinition(string Root, string Role);

public sealed record ForbiddenReach(string Rule, IReadOnlyList<string> From, IReadOnlyList<string> To);

public sealed record ModuleAllowance(string From, string To);

public sealed record ModuleNamespaces(string Shared, IReadOnlyList<ModuleAllowance> AllowList);

public sealed record DeniedCategory(IReadOnlyList<string> Prefixes, IReadOnlyList<string> FrameworkReferences, IReadOnlyList<string> Sdks);

public sealed record ApprovedSdk(string Sdk, string UsedBy);

public sealed record RequiredSetting(string Name, string Value, string File);

public sealed record ProtectedSettings(IReadOnlyList<RequiredSetting> Required, IReadOnlyList<string> Forbidden, IReadOnlyList<string> ForbiddenItems);

public sealed record ListedSuppression(string Id, string Where, string Reason);

public sealed record RuleDefinition(string Id, string Statement, string Source, bool Retired);

/// <summary>The outcome of loading the rule source: the source, or the C2 violations that stopped it.</summary>
public sealed record RuleSourceLoad(RuleSource? Source, IReadOnlyList<Violation> Violations);

/// <summary>
/// The single machine-readable source of the rules, the allow-list, and the module map (FR-002). The test code
/// holds no copy of its contents. A failure names the field, and the run does not continue on partial data.
/// </summary>
public sealed class RuleSource
{
    public const string RelativePath = "tests/Iskender.Architecture.Tests/module-dependency-rules.json";

    private static readonly HashSet<string> Roles = new(["public contract", "embeddable", "executable", "test"], StringComparer.Ordinal);

    public required string ContractPath { get; init; }

    public required int ContractVersion { get; init; }

    public required string ModuleTablePath { get; init; }

    public required string ModuleTableHeading { get; init; }

    public required IReadOnlyList<ModuleHome> Modules { get; init; }

    public required IReadOnlyList<PortHome> PortHomes { get; init; }

    public required IReadOnlyList<ProjectDefinition> Projects { get; init; }

    public required TestProjectsDefinition TestProjects { get; init; }

    public required IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredReferences { get; init; }

    public required IReadOnlyList<ForbiddenReach> ForbiddenReach { get; init; }

    public required ModuleNamespaces ModuleNamespaces { get; init; }

    public required IReadOnlyDictionary<string, DeniedCategory> DeniedPackages { get; init; }

    public required IReadOnlyList<ApprovedSdk> ApprovedSdks { get; init; }

    public required ProtectedSettings ProtectedSettings { get; init; }

    public required IReadOnlyList<ListedSuppression> Suppressions { get; init; }

    public required IReadOnlyList<RuleDefinition> Rules { get; init; }

    public IReadOnlyList<string> RuleOrder => [.. Rules.Select(rule => rule.Id)];

    public static bool IsPattern(string entry) => entry.EndsWith('>') && entry.Contains(".<", StringComparison.Ordinal);

    public static RuleSourceLoad Load(string root)
    {
        var path = Path.Combine(root, RelativePath);
        if (!File.Exists(path))
        {
            return Failed("(file)", "the rule source is missing");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            return Failed("(file)", $"the rule source is not valid JSON: {exception.Message}");
        }

        using (document)
        {
            var reader = new Reader(document.RootElement);
            var groundingRoot = GroundingManifest.Read(root, "grounding", "root");
            var specificationsRoot = GroundingManifest.Read(root, "speckit", "specificationsRoot");
            if (groundingRoot is null)
            {
                reader.Fail("grounding.root", $"does not resolve from {GroundingManifest.RelativePath}");
            }

            if (specificationsRoot is null)
            {
                reader.Fail("speckit.specificationsRoot", $"does not resolve from {GroundingManifest.RelativePath}");
            }

            var source = reader.Read(root, groundingRoot, specificationsRoot);
            return reader.Problems.Count == 0 && source is not null
                ? new RuleSourceLoad(source, [])
                : new RuleSourceLoad(null, Violation.Normalize(reader.Problems.Select(ToViolation), ["C2"]));
        }

        static RuleSourceLoad Failed(string field, string detail) =>
            new(null, [ToViolation((field, detail))]);

        static Violation ToViolation((string Field, string Detail) problem) =>
            new("C2", RelativePath, problem.Field, problem.Detail, RelativePath, null);
    }

    /// <summary>
    /// Whether a project matches a source entry. <c>*</c> matches everything. An exact entry wins, and only then
    /// is a pattern tried, whose segment contains no dot. So <c>Iskender.Providers.Abstractions</c> never matches
    /// <c>Iskender.Providers.&lt;Provider&gt;</c>, and <c>Iskender.Providers.Foo.Bar</c> matches nothing.
    /// </summary>
    public bool Matches(string entry, string project)
    {
        if (entry == "*")
        {
            return true;
        }

        if (!IsPattern(entry))
        {
            return string.Equals(entry, project, StringComparison.Ordinal);
        }

        var prefix = entry[..entry.LastIndexOf('<')];
        if (!project.StartsWith(prefix, StringComparison.Ordinal)
            || Projects.Any(definition => string.Equals(definition.Name, project, StringComparison.Ordinal)))
        {
            return false;
        }

        var segment = project[prefix.Length..];
        return segment.Length > 0 && !segment.Contains('.', StringComparison.Ordinal);
    }

    /// <summary>The definition a project name resolves to: an exact entry, then a pattern, or null.</summary>
    public ProjectDefinition? Resolve(string project) =>
        Projects.FirstOrDefault(definition => string.Equals(definition.Name, project, StringComparison.Ordinal))
        ?? Projects.FirstOrDefault(definition => IsPattern(definition.Name) && Matches(definition.Name, project));

    public Violation Violate(string rule, string source, string target, string detail) =>
        new(rule, source, target, detail, ContractPath, ContractVersion);

    /// <summary>Reads the JSON field by field, collecting every problem with the name of its field.</summary>
    private sealed class Reader(JsonElement document)
    {
        public List<(string Field, string Detail)> Problems { get; } = [];

        public void Fail(string field, string detail) => Problems.Add((field, detail));

        public RuleSource? Read(string root, string? groundingRoot, string? specificationsRoot)
        {
            var contract = Object(document, "contract", "contract");
            var contractRelative = Text(contract, "path", "contract.path");
            var version = Number(contract, "version", "contract.version");
            if (version is < 2)
            {
                Fail("contract.version", "must be at least 2");
            }

            var contractPath = $"{specificationsRoot}/{contractRelative}";
            if (specificationsRoot is not null && contractRelative is not null && !File.Exists(Path.Combine(root, contractPath)))
            {
                Fail("contract.path", $"{contractPath} does not exist");
            }

            var moduleTable = Object(document, "moduleTable", "moduleTable");
            var moduleFile = Text(moduleTable, "file", "moduleTable.file");
            var heading = Text(moduleTable, "heading", "moduleTable.heading");
            var moduleTablePath = $"{groundingRoot}/{moduleFile}";
            if (groundingRoot is not null && moduleFile is not null && !File.Exists(Path.Combine(root, moduleTablePath)))
            {
                Fail("moduleTable.file", $"{moduleTablePath} does not exist");
            }

            var modules = Each(document, "modules", "modules", (element, field) =>
                new ModuleHome(Text(element, "module", $"{field}.module")!, Text(element, "assembly", $"{field}.assembly")!, Text(element, "namespaceRoot", $"{field}.namespaceRoot")!));
            var portHomes = Each(document, "portHomes", "portHomes", (element, field) =>
                new PortHome(Text(element, "item", $"{field}.item")!, Text(element, "home", $"{field}.home")!, Text(element, "owner", $"{field}.owner")!));

            // A project's references may be empty: Iskender.Core.Contracts references nothing internal (rule A1).
            var projects = Each(document, "projects", "projects", (element, field) =>
                new ProjectDefinition(
                    Text(element, "name", $"{field}.name")!,
                    Role(element, field),
                    Strings(element, "references", $"{field}.references", mayBeEmpty: true),
                    Strings(element, "denied", $"{field}.denied", mayBeEmpty: false)));

            var testObject = Object(document, "testProjects", "testProjects");
            var testProjects = new TestProjectsDefinition(Text(testObject, "root", "testProjects.root")!, Role(testObject, "testProjects"));

            var requiredReferences = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var requiredObject = Object(document, "requiredReferences", "requiredReferences");
            if (requiredObject is { } requiredElement)
            {
                foreach (var property in requiredElement.EnumerateObject())
                {
                    requiredReferences[property.Name] = Strings(requiredElement, property.Name, $"requiredReferences.{property.Name}", mayBeEmpty: false);
                }

                if (requiredReferences.Count == 0)
                {
                    Fail("requiredReferences", "must not be empty");
                }
            }

            var forbiddenReach = Each(document, "forbiddenReach", "forbiddenReach", (element, field) =>
                new ForbiddenReach(
                    Text(element, "rule", $"{field}.rule")!,
                    Strings(element, "from", $"{field}.from", mayBeEmpty: false),
                    Strings(element, "to", $"{field}.to", mayBeEmpty: false)));

            var namespacesObject = Object(document, "moduleNamespaces", "moduleNamespaces");
            var moduleNamespaces = new ModuleNamespaces(
                Text(namespacesObject, "shared", "moduleNamespaces.shared")!,
                Each(namespacesObject, "allowList", "moduleNamespaces.allowList", (element, field) =>
                    new ModuleAllowance(Text(element, "from", $"{field}.from")!, Text(element, "to", $"{field}.to")!), mayBeEmpty: true));

            var deniedPackages = new Dictionary<string, DeniedCategory>(StringComparer.Ordinal);
            var deniedObject = Object(document, "deniedPackages", "deniedPackages");
            if (deniedObject is { } deniedElement)
            {
                foreach (var property in deniedElement.EnumerateObject())
                {
                    var field = $"deniedPackages.{property.Name}";
                    deniedPackages[property.Name] = new DeniedCategory(
                        Strings(property.Value, "prefixes", $"{field}.prefixes", mayBeEmpty: false),
                        Strings(property.Value, "frameworkReferences", $"{field}.frameworkReferences", mayBeEmpty: true),
                        Strings(property.Value, "sdks", $"{field}.sdks", mayBeEmpty: true));
                }

                if (deniedPackages.Count == 0)
                {
                    Fail("deniedPackages", "must not be empty");
                }
            }

            var approvedSdks = Each(document, "approvedSdks", "approvedSdks", (element, field) =>
                new ApprovedSdk(Text(element, "sdk", $"{field}.sdk")!, Text(element, "usedBy", $"{field}.usedBy")!));

            var settingsObject = Object(document, "protectedSettings", "protectedSettings");
            var protectedSettings = new ProtectedSettings(
                Each(settingsObject, "required", "protectedSettings.required", (element, field) =>
                    new RequiredSetting(Text(element, "name", $"{field}.name")!, Text(element, "value", $"{field}.value")!, Text(element, "file", $"{field}.file")!)),
                Strings(settingsObject, "forbidden", "protectedSettings.forbidden", mayBeEmpty: false),
                Strings(settingsObject, "forbiddenItems", "protectedSettings.forbiddenItems", mayBeEmpty: false));

            var suppressions = Each(document, "suppressions", "suppressions", (element, field) =>
                new ListedSuppression(Text(element, "id", $"{field}.id")!, Text(element, "where", $"{field}.where")!, Text(element, "reason", $"{field}.reason")!));

            var rules = Each(document, "rules", "rules", (element, field) =>
                new RuleDefinition(Text(element, "id", $"{field}.id")!, Text(element, "statement", $"{field}.statement")!, Text(element, "source", $"{field}.source")!, Flag(element, "retired", $"{field}.retired")));

            foreach (var duplicate in rules.GroupBy(rule => rule.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                Fail("rules[].id", $"{duplicate.Key} is not unique");
            }

            foreach (var category in projects.SelectMany(project => project.Denied).Where(category => !deniedPackages.ContainsKey(category)))
            {
                Fail("projects[].denied", $"category {category} is not in deniedPackages");
            }

            foreach (var entry in forbiddenReach)
            {
                if (!rules.Any(rule => string.Equals(rule.Id, entry.Rule, StringComparison.Ordinal)))
                {
                    Fail("forbiddenReach[].rule", $"{entry.Rule} is not in rules");
                }

                if (entry.To.Contains("*"))
                {
                    Fail("forbiddenReach[].to", "* is valid only in from");
                }
            }

            if (Problems.Count > 0)
            {
                return null;
            }

            return new RuleSource
            {
                ContractPath = contractPath,
                ContractVersion = version!.Value,
                ModuleTablePath = moduleTablePath,
                ModuleTableHeading = heading!,
                Modules = modules,
                PortHomes = portHomes,
                Projects = projects,
                TestProjects = testProjects,
                RequiredReferences = requiredReferences,
                ForbiddenReach = forbiddenReach,
                ModuleNamespaces = moduleNamespaces,
                DeniedPackages = deniedPackages,
                ApprovedSdks = approvedSdks,
                ProtectedSettings = protectedSettings,
                Suppressions = suppressions,
                Rules = rules,
            };
        }

        private JsonElement? Object(JsonElement? parent, string name, string field)
        {
            if (parent is not { ValueKind: JsonValueKind.Object } element || !element.TryGetProperty(name, out var value))
            {
                Fail(field, "is missing");
                return null;
            }

            if (value.ValueKind != JsonValueKind.Object)
            {
                Fail(field, "must be an object");
                return null;
            }

            return value;
        }

        private string? Text(JsonElement? parent, string name, string field)
        {
            if (parent is not { ValueKind: JsonValueKind.Object } element || !element.TryGetProperty(name, out var value))
            {
                Fail(field, "is missing");
                return null;
            }

            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                Fail(field, "must be a non-empty string");
                return null;
            }

            return value.GetString();
        }

        private int? Number(JsonElement? parent, string name, string field)
        {
            if (parent is not { ValueKind: JsonValueKind.Object } element || !element.TryGetProperty(name, out var value))
            {
                Fail(field, "is missing");
                return null;
            }

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            {
                Fail(field, "must be a whole number");
                return null;
            }

            return number;
        }

        private bool Flag(JsonElement? parent, string name, string field)
        {
            if (parent is not { ValueKind: JsonValueKind.Object } element || !element.TryGetProperty(name, out var value))
            {
                Fail(field, "is missing");
                return false;
            }

            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                Fail(field, "must be true or false");
                return false;
            }

            return value.GetBoolean();
        }

        private string Role(JsonElement? parent, string field)
        {
            var role = Text(parent, "role", $"{field}.role");
            if (role is not null && !Roles.Contains(role))
            {
                Fail($"{field}.role", $"{role} is not a role");
            }

            return role ?? string.Empty;
        }

        private List<string> Strings(JsonElement? parent, string name, string field, bool mayBeEmpty)
        {
            var items = Array(parent, name, field, mayBeEmpty);
            var result = new List<string>();
            foreach (var item in items)
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
                    Fail(field, "must hold only non-empty strings");
                }
                else
                {
                    result.Add(item.GetString()!);
                }
            }

            return result;
        }

        private List<T> Each<T>(JsonElement? parent, string name, string field, Func<JsonElement, string, T> read, bool mayBeEmpty = false)
        {
            var result = new List<T>();
            foreach (var (item, index) in Array(parent, name, field, mayBeEmpty).Select((item, index) => (item, index)))
            {
                result.Add(read(item, $"{field}[{index}]"));
            }

            return result;
        }

        private List<JsonElement> Array(JsonElement? parent, string name, string field, bool mayBeEmpty)
        {
            if (parent is not { ValueKind: JsonValueKind.Object } element || !element.TryGetProperty(name, out var value))
            {
                Fail(field, "is missing");
                return [];
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                Fail(field, "must be an array");
                return [];
            }

            var items = value.EnumerateArray().ToList();
            if (items.Count == 0 && !mayBeEmpty)
            {
                Fail(field, "must not be empty");
            }

            return items;
        }
    }
}
