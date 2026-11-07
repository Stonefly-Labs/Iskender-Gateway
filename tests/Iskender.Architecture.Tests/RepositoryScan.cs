using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

public sealed record ScannedProperty(string Name, string Value, string DefinedIn, bool IsConditional, bool InTarget);

/// <summary>An item in a build file. <c>IsTaskOutput</c> means a task's <c>Output</c> element creates it, so its value is unknown.</summary>
public sealed record ScannedItem(string Type, string Value, IReadOnlyDictionary<string, string> Metadata, string DefinedIn, bool IsConditional, bool InTarget, bool IsTaskOutput = false);

/// <summary>A reference found in a build file. <c>ResolvedPath</c> is null if it names no file in the repository scan.</summary>
public sealed record ScannedReference(string Include, string TargetName, string? ResolvedPath, string DefinedIn, bool HasUnresolvedProperty);

/// <summary>An import found in a build file. <c>ResolvedPath</c> is null if it names no file in the repository.</summary>
public sealed record ScannedImport(string Project, string? ResolvedPath, string DefinedIn, bool HasUnresolvedProperty);

public sealed record ScannedSdk(string Name, string DefinedIn);

public sealed record ScanProblem(string Source, string Target, string Detail);

/// <summary>A root build file, read as it stands. B3 checks required settings here and B4 forbidden ones.</summary>
public sealed record RootBuildFile(string Name, IReadOnlyList<ScannedProperty> Properties, IReadOnlyList<ScannedItem> Items);

public sealed record ScannedProject
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public required bool IsUnderTests { get; init; }

    public required IReadOnlyList<ScannedReference> References { get; init; }

    public required IReadOnlyList<ScannedImport> Imports { get; init; }

    public required IReadOnlyList<ScannedItem> PackageReferences { get; init; }

    public required IReadOnlyList<ScannedItem> AssemblyReferences { get; init; }

    public required IReadOnlyList<ScannedItem> FrameworkReferences { get; init; }

    public required IReadOnlyList<ScannedSdk> Sdks { get; init; }

    public required IReadOnlyList<ScannedProperty> Properties { get; init; }

    public required IReadOnlyList<ScannedItem> Items { get; init; }

    public required bool HasLockFile { get; init; }

    public required IReadOnlyList<string> LockFilePackages { get; init; }
}

public sealed record RepositoryScanResult(
    string Root,
    IReadOnlyList<ScannedProject> Projects,
    IReadOnlyList<string> SolutionProjects,
    IReadOnlyList<RootBuildFile> RootFiles,
    IReadOnlyList<ScanProblem> Problems);

/// <summary>
/// Reads every project file as XML without MSBuild, because evaluation reports only the active branch and the
/// checks exist to catch what a condition hides (research R4). Every element counts, under any condition and
/// inside targets. The root build files count as imports of every project, and a project's <c>.user</c> file as an
/// import of that project, since MSBuild imports them implicitly.
/// </summary>
public static partial class RepositoryScan
{
    public const string SolutionFile = "Iskender.sln";

    public const string RootBuildProps = "Directory.Build.props";

    public const string RootPackagesProps = "Directory.Packages.props";

    public const string RootBuildTargets = "Directory.Build.targets";

    private const string LockFileName = "packages.lock.json";

    private const string UserFileSuffix = ".user";

    /// <summary>The value of a property or item a task output creates. It is not empty, so a suppression setting still reports it.</summary>
    private const string TaskOutputValue = "(task output)";

    private static readonly string[] RootBuildFiles = [RootBuildProps, RootPackagesProps, RootBuildTargets];

    /// <param name="root">The repository root.</param>
    /// <param name="requiredSettings">
    /// Names B3 checks in the root Directory.Build.props and Directory.Packages.props, which therefore do not
    /// count as project settings when they appear there. A forbidden setting or item there still counts.
    /// </param>
    public static RepositoryScanResult Run(string root, IReadOnlyCollection<string>? requiredSettings = null)
    {
        var problems = new List<ScanProblem>();
        var required = new HashSet<string>(requiredSettings ?? [], StringComparer.OrdinalIgnoreCase);
        var files = new FileReader(root, problems);

        var projectPaths = RepositoryPaths.EnumerateFiles(root)
            .Where(RepositoryPaths.IsProjectFile)
            .Select(path => RepositoryPaths.Relative(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        if (projectPaths.Count == 0)
        {
            problems.Add(new ScanProblem("repository", "project files", "no project file was found"));
        }

        var knownProjects = new HashSet<string>(projectPaths, StringComparer.OrdinalIgnoreCase);
        var projects = projectPaths.Select(path => ReadProject(root, path, knownProjects, required, files, problems)).ToList();
        var rootFiles = RootBuildFiles
            .Where(name => File.Exists(Path.Combine(root, name)))
            .Select(name => files.Parse(name))
            .Where(parsed => parsed is not null)
            .Select(parsed => new RootBuildFile(parsed!.Path, parsed.Properties, parsed.Items))
            .ToList();

        return new RepositoryScanResult(root, projects, ReadSolution(root, problems), rootFiles, problems);
    }

    /// <summary>Whether a project path sits under <c>tests/</c>, which alone gives the test role (FR-014).</summary>
    public static bool IsUnderTests(string path) => path.StartsWith("tests/", StringComparison.Ordinal);

    private static ScannedProject ReadProject(
        string root,
        string path,
        HashSet<string> knownProjects,
        HashSet<string> required,
        FileReader files,
        List<ScanProblem> problems)
    {
        var projectDirectory = Path.GetDirectoryName(Path.Combine(root, path))!;
        var parsedFiles = new List<ParsedBuildFile>();
        var imports = new List<ScannedImport>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Follow(string relative)
        {
            if (!visited.Add(relative))
            {
                return;
            }

            var parsed = files.Parse(relative);
            if (parsed is null)
            {
                return;
            }

            parsedFiles.Add(parsed);
            foreach (var import in parsed.Imports)
            {
                var resolved = ResolveImport(root, import, projectDirectory);
                imports.Add(resolved);
                if (resolved.ResolvedPath is not null)
                {
                    Follow(resolved.ResolvedPath);
                }
            }
        }

        Follow(path);

        // MSBuild imports this from beside the project file whenever it exists, with no Import in the project.
        var userFile = path + UserFileSuffix;
        if (File.Exists(Path.Combine(root, userFile)))
        {
            Follow(userFile);
        }

        foreach (var name in RootBuildFiles.Where(name => File.Exists(Path.Combine(root, name))))
        {
            Follow(name);
        }

        var items = parsedFiles.SelectMany(file => file.Items).ToList();
        var declaredItems = items.Where(item => !item.IsTaskOutput).ToList();
        var properties = parsedFiles
            .SelectMany(file => file.Properties)
            .Where(property => !(required.Contains(property.Name) && property.DefinedIn is RootBuildProps or RootPackagesProps))
            .ToList();

        var (hasLockFile, lockFilePackages) = ReadLockFile(root, path, problems);

        return new ScannedProject
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Path = path,
            IsUnderTests = IsUnderTests(path),
            References = declaredItems
                .Where(item => IsType(item, "ProjectReference"))
                .SelectMany(item => ToReferences(root, item, projectDirectory, knownProjects))
                .ToList(),
            Imports = imports,
            PackageReferences = declaredItems.Where(item => IsType(item, "PackageReference") || IsType(item, "GlobalPackageReference")).SelectMany(SplitValues).ToList(),
            AssemblyReferences = declaredItems.Where(item => IsType(item, "Reference")).SelectMany(SplitValues).ToList(),
            FrameworkReferences = declaredItems.Where(item => IsType(item, "FrameworkReference")).SelectMany(SplitValues).ToList(),
            Sdks = parsedFiles.SelectMany(file => file.Sdks).ToList(),
            Properties = properties,
            Items = items,
            HasLockFile = hasLockFile,
            LockFilePackages = lockFilePackages,
        };
    }

    private static bool IsType(ScannedItem item, string type) =>
        string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<ScannedItem> SplitValues(ScannedItem item) =>
        item.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => item with { Value = value });

    private static IEnumerable<ScannedReference> ToReferences(string root, ScannedItem item, string projectDirectory, HashSet<string> knownProjects)
    {
        foreach (var include in item.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var substituted = SubstituteKnownProperties(root, include, item.DefinedIn, projectDirectory);
            var unresolved = HasMsBuildExpression(substituted);
            string? resolved = null;
            if (!unresolved && !substituted.Contains('*', StringComparison.Ordinal))
            {
                var relative = ToRepositoryRelative(root, projectDirectory, substituted);
                if (relative is not null && knownProjects.Contains(relative))
                {
                    resolved = relative;
                }
            }

            yield return new ScannedReference(include, FileNameWithoutExtension(substituted), resolved, item.DefinedIn, unresolved);
        }
    }

    private static ScannedImport ResolveImport(string root, RawImport import, string projectDirectory)
    {
        var substituted = SubstituteKnownProperties(root, import.Project, import.DefinedIn, projectDirectory);
        if (HasMsBuildExpression(substituted))
        {
            return new ScannedImport(import.Project, null, import.DefinedIn, true);
        }

        var definingDirectory = Path.GetDirectoryName(Path.Combine(root, import.DefinedIn))!;
        var relative = substituted.Contains('*', StringComparison.Ordinal) ? null : ToRepositoryRelative(root, definingDirectory, substituted);
        return new ScannedImport(
            import.Project,
            relative is not null && File.Exists(Path.Combine(root, relative)) ? relative : null,
            import.DefinedIn,
            false);
    }

    private static string SubstituteKnownProperties(string root, string value, string definedIn, string projectDirectory)
    {
        var thisFileDirectory = Path.GetDirectoryName(Path.Combine(root, definedIn))! + "/";
        return value
            .Replace("$(MSBuildThisFileDirectory)", thisFileDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("$(MSBuildProjectDirectory)", projectDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasMsBuildExpression(string value) =>
        value.Contains("$(", StringComparison.Ordinal) || value.Contains("@(", StringComparison.Ordinal) || value.Contains("%(", StringComparison.Ordinal);

    /// <summary>A repository-relative path with forward slashes, or null if it leaves the repository.</summary>
    private static string? ToRepositoryRelative(string root, string baseDirectory, string path)
    {
        var full = Path.GetFullPath(Path.Combine(baseDirectory, path.Replace('\\', '/')));
        var relative = RepositoryPaths.Relative(root, full);
        return relative.StartsWith("../", StringComparison.Ordinal) || relative == ".." || Path.IsPathRooted(relative) ? null : relative;
    }

    private static string FileNameWithoutExtension(string path) =>
        Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));

    private static (bool HasLockFile, IReadOnlyList<string> Packages) ReadLockFile(string root, string projectPath, List<ScanProblem> problems)
    {
        var directory = Path.GetDirectoryName(projectPath)!.Replace('\\', '/');
        var relative = directory.Length == 0 ? LockFileName : $"{directory}/{LockFileName}";
        var path = Path.Combine(root, relative);
        if (!File.Exists(path))
        {
            return (false, []);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var packages = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
            {
                foreach (var package in framework.Value.EnumerateObject())
                {
                    if (!string.Equals(package.Value.GetProperty("type").GetString(), "Project", StringComparison.OrdinalIgnoreCase))
                    {
                        packages.Add(package.Name);
                    }
                }
            }

            return (true, [.. packages]);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            problems.Add(new ScanProblem(relative, "lock file", $"cannot be read: {exception.Message}"));
            return (true, []);
        }
    }

    private static List<string> ReadSolution(string root, List<ScanProblem> problems)
    {
        var path = Path.Combine(root, SolutionFile);
        if (!File.Exists(path))
        {
            problems.Add(new ScanProblem(SolutionFile, "solution file", "is missing"));
            return [];
        }

        try
        {
            return File.ReadLines(path)
                .Select(line => SolutionEntry().Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups[1].Value.Replace('\\', '/'))
                .Where(RepositoryPaths.IsProjectFile)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problems.Add(new ScanProblem(SolutionFile, "solution file", $"cannot be read: {exception.Message}"));
            return [];
        }
    }

    [GeneratedRegex("""^Project\("\{[^}]*\}"\)\s*=\s*"[^"]*",\s*"([^"]*)",""")]
    private static partial Regex SolutionEntry();

    private sealed record RawImport(string Project, string DefinedIn);

    private sealed record ParsedBuildFile(
        string Path,
        IReadOnlyList<ScannedProperty> Properties,
        IReadOnlyList<ScannedItem> Items,
        IReadOnlyList<RawImport> Imports,
        IReadOnlyList<ScannedSdk> Sdks);

    /// <summary>Parses each build file once, and reports each unreadable file once.</summary>
    private sealed class FileReader(string root, List<ScanProblem> problems)
    {
        private readonly Dictionary<string, ParsedBuildFile?> parsed = new(StringComparer.OrdinalIgnoreCase);

        public ParsedBuildFile? Parse(string relative)
        {
            if (parsed.TryGetValue(relative, out var existing))
            {
                return existing;
            }

            ParsedBuildFile? result = null;
            try
            {
                result = Read(relative, XDocument.Load(Path.Combine(root, relative)));
            }
            catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
            {
                problems.Add(new ScanProblem(relative, "build file", $"cannot be parsed: {exception.Message}"));
            }

            parsed[relative] = result;
            return result;
        }

        private static ParsedBuildFile Read(string relative, XDocument document)
        {
            var properties = new List<ScannedProperty>();
            var items = new List<ScannedItem>();
            var imports = new List<RawImport>();
            var sdks = new List<ScannedSdk>();

            AddSdks(sdks, document.Root?.Attribute("Sdk")?.Value, relative);

            foreach (var element in document.Descendants())
            {
                var name = element.Name.LocalName;
                var parent = element.Parent?.Name.LocalName;
                if (Is(parent, "PropertyGroup"))
                {
                    properties.Add(new ScannedProperty(name, element.Value.Trim(), relative, IsConditional(element), InTarget(element)));
                }
                else if (Is(parent, "ItemGroup") || Is(parent, "ItemDefinitionGroup"))
                {
                    items.Add(new ScannedItem(
                        name,
                        element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value ?? element.Attribute("Remove")?.Value ?? string.Empty,
                        Metadata(element),
                        relative,
                        IsConditional(element),
                        InTarget(element)));
                }
                else if (Is(name, "Output") && InTarget(element))
                {
                    var property = element.Attribute("PropertyName")?.Value;
                    if (property is not null)
                    {
                        properties.Add(new ScannedProperty(property, TaskOutputValue, relative, IsConditional(element), true));
                    }

                    var item = element.Attribute("ItemName")?.Value;
                    if (item is not null)
                    {
                        items.Add(new ScannedItem(item, TaskOutputValue, new Dictionary<string, string>(), relative, IsConditional(element), true, true));
                    }
                }
                else if (Is(name, "Import"))
                {
                    var sdk = element.Attribute("Sdk")?.Value;
                    if (sdk is not null)
                    {
                        AddSdks(sdks, sdk, relative);
                    }
                    else
                    {
                        foreach (var project in (element.Attribute("Project")?.Value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            imports.Add(new RawImport(project, relative));
                        }
                    }
                }
                else if (Is(name, "Sdk"))
                {
                    AddSdks(sdks, element.Attribute("Name")?.Value, relative);
                }
            }

            return new ParsedBuildFile(relative, properties, items, imports, sdks);
        }

        private static void AddSdks(List<ScannedSdk> sdks, string? value, string relative)
        {
            foreach (var sdk in (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                sdks.Add(new ScannedSdk(sdk.Split('/')[0], relative));
            }
        }

        private static Dictionary<string, string> Metadata(XElement element)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var attribute in element.Attributes())
            {
                metadata[attribute.Name.LocalName] = attribute.Value;
            }

            foreach (var child in element.Elements())
            {
                metadata[child.Name.LocalName] = child.Value.Trim();
            }

            return metadata;
        }

        private static bool Is(string? name, string expected) =>
            string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);

        private static bool IsConditional(XElement element) =>
            element.AncestorsAndSelf().Any(ancestor => ancestor.Attribute("Condition") is not null);

        private static bool InTarget(XElement element) =>
            element.Ancestors().Any(ancestor => Is(ancestor.Name.LocalName, "Target"));
    }
}
