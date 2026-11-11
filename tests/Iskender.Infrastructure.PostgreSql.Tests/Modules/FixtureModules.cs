using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Migrations;

namespace Iskender.Infrastructure.PostgreSql.Tests.Modules;

/// <summary>
/// The test-only migration modules (data model Test fixtures, research R22). Their scripts are embedded in this test
/// assembly only, never in a <c>src/</c> project (FR-034). Release 1 is Alpha 1 to 3 and Beta 1 to 3; release 2 adds
/// Alpha 4; Alpha 6 is beyond Alpha's declared maximum of 5.
/// </summary>
internal static class FixtureModules
{
    private const string Prefix = "Iskender.Infrastructure.PostgreSql.Tests.Modules.";

    public static readonly SchemaVersionRange AlphaRange = new(1, 5);

    public static readonly SchemaVersionRange BetaRange = new(1, 3);

    /// <summary>Alpha 1 to 4, declared with the public, embedded-resource constructor.</summary>
    public static MigrationModule Alpha => new("alpha", AlphaRange, typeof(FixtureModules).Assembly, Prefix + "Alpha.");

    public static MigrationModule Beta => new("beta", BetaRange, typeof(FixtureModules).Assembly, Prefix + "Beta.", "alpha");

    /// <summary>Alpha as release 1 shipped it: versions 1 to 3.</summary>
    public static MigrationModule AlphaRelease1 => Declare("alpha", AlphaRange, [], Files("Alpha.").Where(file => Version(file.Name) <= 3));

    /// <summary>Alpha 1 to 4 plus the fixture-only 6.</summary>
    public static MigrationModule AlphaNext => Declare("alpha", AlphaRange, [], Files("Alpha.").Concat(Files("AlphaNext.")));

    /// <summary>Alpha whose version 1 forward script was edited after it was applied.</summary>
    public static MigrationModule AlphaEdited => Declare("alpha", AlphaRange, [], Files("Invalid.EditedAlpha.").Concat(Files("Alpha.").Where(file => Version(file.Name) is 2 or 3)));

    /// <summary>Release 1: Alpha 1 to 3 and Beta 1 to 3.</summary>
    public static MigrationSet Release1 => new([AlphaRelease1, Beta]);

    /// <summary>Release 2: Alpha 1 to 4 and Beta 1 to 3.</summary>
    public static MigrationSet Release2 => new([Alpha, Beta]);

    /// <summary>The first <paramref name="count"/> migrations of release 2, so a test can apply them one at a time.</summary>
    public static MigrationSet Release2First(int count)
    {
        var alpha = Declare("alpha", AlphaRange, [], Files("Alpha.").Where(file => Version(file.Name) <= Math.Min(count, 4)));
        var beta = Declare("beta", BetaRange, ["alpha"], Files("Beta.").Where(file => Version(file.Name) <= count - 4));
        return new MigrationSet(count > 4 ? [alpha, beta] : [alpha]);
    }

    public static MigrationModule MissingReverse => Invalid("missing_reverse", "MissingReverse");

    public static MigrationModule DuplicateVersion => Invalid("duplicate_version", "DuplicateVersion");

    public static MigrationModule CycleA => Invalid("cycle_a", "CycleA", "cycle_b");

    public static MigrationModule CycleB => Invalid("cycle_b", "CycleB", "cycle_a");

    public static MigrationModule CrossSchema => Invalid("cross_schema", "CrossSchema");

    /// <summary>Version 1 succeeds; version 2 fails at its second statement.</summary>
    public static MigrationModule ForwardFails => Invalid("forward_fails", "ForwardFails");

    /// <summary>Version 1, the module's first, fails at its second statement.</summary>
    public static MigrationModule FailsFirst => Invalid("fails_first", "FailsFirst");

    /// <summary>A tenant table with no row-level security or policy.</summary>
    public static MigrationModule NoPolicy => Invalid("no_policy", "NoPolicy");

    private static MigrationModule Invalid(string name, string folder, params string[] dependsOn) =>
        Declare(name, new SchemaVersionRange(1, 5), dependsOn, Files($"Invalid.{folder}."));

    private static MigrationModule Declare(string name, SchemaVersionRange range, string[] dependsOn, IEnumerable<(string Name, byte[] Content)> files)
    {
        var list = files.ToList();
        return new MigrationModule(name, range, () => list, dependsOn);
    }

    private static IEnumerable<(string Name, byte[] Content)> Files(string folder)
    {
        var assembly = typeof(FixtureModules).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix + folder, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            yield return (resource[(Prefix.Length + folder.Length)..], copy.ToArray());
        }
    }

    private static int Version(string name) => int.Parse(name.AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture);
}
