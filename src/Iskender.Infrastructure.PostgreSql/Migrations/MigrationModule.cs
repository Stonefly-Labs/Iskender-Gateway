using System.Reflection;
using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Migrations;

/// <summary>
/// A module's migrations (contract Migrations rules 1 and 2, research R11): its name, its schema <c>isk_&lt;name&gt;</c>,
/// the modules it depends on, its supported schema range, and the embedded resources under a prefix that hold
/// <c>&lt;version&gt;_&lt;slug&gt;.up.sql</c> and <c>.down.sql</c>. Owned by ISK-0004.
/// </summary>
public sealed partial class MigrationModule
{
    private readonly Func<IEnumerable<(string Name, byte[] Content)>> _resources;

    /// <summary>
    /// <paramref name="resourcePrefix"/> is the manifest resource name prefix, ending in <c>.</c>, under which every
    /// <c>.sql</c> resource is one of this module's scripts. An invalid name or dependency fails with
    /// <c>core.value.invalid</c>.
    /// </summary>
    public MigrationModule(string name, SchemaVersionRange supportedRange, Assembly assembly, string resourcePrefix, params IEnumerable<string> dependsOn)
        : this(name, supportedRange, Embedded(assembly, resourcePrefix), dependsOn)
    {
    }

    internal MigrationModule(string name, SchemaVersionRange supportedRange, Func<IEnumerable<(string Name, byte[] Content)>> resources, IEnumerable<string> dependsOn)
    {
        Name = IsName(name) ? name : throw new GatewayException(ErrorCodes.ValueInvalid);
        SupportedRange = supportedRange ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        _resources = resources;
        DependsOn = [.. (dependsOn ?? throw new GatewayException(ErrorCodes.ValueInvalid)).Select(dependency => IsName(dependency) ? dependency : throw new GatewayException(ErrorCodes.ValueInvalid))];
    }

    public string Name { get; }

    public string Schema => "isk_" + Name;

    public IReadOnlyList<string> DependsOn { get; }

    public SchemaVersionRange SupportedRange { get; }

    /// <summary>A module name: 1 to 31 characters of <c>a-z</c>, <c>0-9</c>, and <c>_</c>, starting with a letter.</summary>
    internal static bool IsName(string? value) => value is not null && NamePattern().IsMatch(value);

    /// <summary>The module's script files, unpaired. A <c>.sql</c> resource whose name is not a script name fails with <c>core.value.invalid</c>.</summary>
    internal IReadOnlyList<MigrationFile> ReadFiles() =>
        [.. _resources().Select(resource => MigrationFile.Parse(resource.Name, resource.Content) ?? throw new GatewayException(ErrorCodes.ValueInvalid))];

    private static Func<IEnumerable<(string Name, byte[] Content)>> Embedded(Assembly assembly, string resourcePrefix)
    {
        if (assembly is null || string.IsNullOrEmpty(resourcePrefix) || !resourcePrefix.EndsWith('.'))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        return () => assembly.GetManifestResourceNames()
            .Where(resource => resource.StartsWith(resourcePrefix, StringComparison.Ordinal) && resource.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(resource => (resource[resourcePrefix.Length..], Read(assembly, resource)));
    }

    private static byte[] Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,30}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}

/// <summary>One script file of a module, before pairing: its version, slug, direction, and raw bytes.</summary>
internal sealed partial record MigrationFile(int Version, string Slug, bool Forward, byte[] Content)
{
    /// <summary>Parses <c>&lt;version&gt;_&lt;slug&gt;.up.sql</c> or <c>.down.sql</c>, with a 4-digit positive version; anything else is null.</summary>
    public static MigrationFile? Parse(string name, byte[] content)
    {
        var match = NamePattern().Match(name);
        if (!match.Success)
        {
            return null;
        }

        var version = int.Parse(match.Groups["version"].ValueSpan, provider: System.Globalization.CultureInfo.InvariantCulture);
        return version == 0 ? null : new MigrationFile(version, match.Groups["slug"].Value, match.Groups["direction"].ValueSpan is "up", content);
    }

    [GeneratedRegex(@"^(?<version>[0-9]{4})_(?<slug>[a-z][a-z0-9_]*)\.(?<direction>up|down)\.sql\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
