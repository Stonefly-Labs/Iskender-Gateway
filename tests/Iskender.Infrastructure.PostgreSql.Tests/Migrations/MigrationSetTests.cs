using System.Text;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.PostgreSql.Migrations;
using Iskender.Infrastructure.PostgreSql.Tests.Fixture;

namespace Iskender.Infrastructure.PostgreSql.Tests.Migrations;

public class MigrationSetTests
{
    [Fact]
    public void ADiamondIsOrderedByDependencyWithTiesBrokenByOrdinalName()
    {
        // top depends on left and right, which both depend on base. "Left" sorts after "base" but before "right".
        var set = new MigrationSet([Module("top", ["right", "left"], 1), Module("right", ["base"], 1), Module("left", ["base"], 1), Module("base", [], 1)]);

        Assert.Equal(["base", "left", "right", "top"], set.Modules.Select(module => module.Name));
    }

    [Fact]
    public void IndependentModulesAreOrderedByOrdinalName()
    {
        var set = new MigrationSet([Module("b_2", [], 1), Module("b", [], 1), Module("a_z", [], 1), Module("b_10", [], 1)]);

        Assert.Equal(["a_z", "b", "b_10", "b_2"], set.Modules.Select(module => module.Name));
    }

    [Fact]
    public void MigrationsRunModuleByModuleThenVersionAscendingAndReverseIsTheExactReverse()
    {
        var set = new MigrationSet([Module("beta", ["alpha"], 3, 1, 2), Module("alpha", [], 2, 10, 1)]);

        string[] forward = ["alpha/1", "alpha/2", "alpha/10", "beta/1", "beta/2", "beta/3"];
        Assert.Equal(forward, set.Migrations.Select(script => script.Identity.ToString()));
        Assert.Equal(forward.Reverse(), set.Reversed.Select(script => script.Identity.ToString()));
    }

    [Fact]
    public void TheSameVersionInDifferentModulesIsAllowed()
    {
        var set = new MigrationSet([Module("alpha", [], 1), Module("beta", [], 1)]);

        Assert.True(set.TryGetScript("alpha", 1, out var alpha));
        Assert.True(set.TryGetScript("beta", 1, out var beta));
        Assert.NotEqual(alpha.Checksum, beta.Checksum);
        Assert.False(set.TryGetScript("alpha", 2, out _));
        Assert.True(set.TryGetModule("beta", out var module));
        Assert.Equal("isk_beta", module.Schema);
    }

    [Fact]
    public void AnEmptySetHasNothing()
    {
        var set = new MigrationSet([]);

        Assert.Empty(set.Modules);
        Assert.Empty(set.Migrations);
    }

    [Fact]
    public void AMissingReverseIsRefused() =>
        AssertRefused([Raw("alpha", [], ("0001_t.up.sql", "SELECT 1;"))], MigrationRefusal.MissingReverse, "alpha", 1);

    [Fact]
    public void AReverseWithoutItsForwardIsRefused() =>
        AssertRefused([Raw("alpha", [], ("0001_t.up.sql", "SELECT 1;"), ("0001_t.down.sql", "SELECT 1;"), ("0002_u.down.sql", "SELECT 1;"))], MigrationRefusal.MissingReverse, "alpha", 2);

    [Fact]
    public void ADuplicateVersionWithinAModuleIsRefused() =>
        AssertRefused(
            [Raw("alpha", [], ("0001_t.up.sql", "SELECT 1;"), ("0001_t.down.sql", "SELECT 1;"), ("0001_u.up.sql", "SELECT 2;"), ("0001_u.down.sql", "SELECT 2;"))],
            MigrationRefusal.DuplicateVersion,
            "alpha",
            1);

    [Fact]
    public void AnUnknownDependencyIsRefused() =>
        AssertRefused([Module("alpha", [], 1), Module("beta", ["gamma"], 1)], MigrationRefusal.UnknownDependency, "beta", null);

    [Fact]
    public void ADependencyCycleIsRefusedNamingAModuleOnIt() =>
        AssertRefused([Module("alpha", [], 1), Module("beta", ["delta", "alpha"], 1), Module("delta", ["gamma"], 1), Module("gamma", ["delta"], 1), Module("zeta", ["gamma"], 1)], MigrationRefusal.DependencyCycle, "delta", null);

    [Fact]
    public void ASelfDependencyIsACycle() =>
        AssertRefused([Module("alpha", ["alpha"], 1)], MigrationRefusal.DependencyCycle, "alpha", null);

    [Fact]
    public void InvalidUtf8IsRefused()
    {
        var module = new MigrationModule("alpha", new SchemaVersionRange(1, 1), () => [("0001_t.up.sql", [0x53, 0xFF]), ("0001_t.down.sql", "SELECT 1;"u8.ToArray())], []);

        AssertRefused([module], MigrationRefusal.InvalidEncoding, "alpha", 1);
    }

    [Theory]
    [InlineData("CREATE TABLE isk_beta.t ();", "SELECT 1;")]
    [InlineData("SELECT 1;", "DROP TABLE public.t;")]
    [InlineData("CREATE VIEW v AS SELECT * FROM \"isk_beta\".t;", "DROP VIEW v;")]
    public void AScriptNamingAnotherSchemaIsRefused(string forward, string reverse) =>
        AssertRefused([Module("beta", [], 1), Raw("alpha", [], ("0001_t.up.sql", forward), ("0001_t.down.sql", reverse))], MigrationRefusal.CrossSchemaReference, "alpha", 1);

    [Fact]
    public void AScriptNamingItsOwnSchemaIsAllowed()
    {
        var set = new MigrationSet([Raw("alpha", [], ("0001_t.up.sql", "CREATE TABLE isk_alpha.t ();"), ("0001_t.down.sql", "DROP TABLE isk_alpha.t;"))]);

        Assert.Single(set.Migrations);
    }

    [Fact]
    public void TwoModulesWithOneNameAreAProgrammingError()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new MigrationSet([Module("alpha", [], 1), Module("alpha", [], 2)])).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new MigrationSet([null!])).Code);
    }

    private static void AssertRefused(IEnumerable<MigrationModule> modules, string reason, string? module, int? version)
    {
        var logger = new CapturingDiagnostics.Logger();

        var failure = Assert.Throws<GatewayException>(() => new MigrationSet(modules, logger));

        Assert.Same(ErrorCodes.PersistenceSchemaIncompatible, failure.Code);
        Assert.Equal(new GatewayException(ErrorCodes.PersistenceSchemaIncompatible).Message, failure.Message);
        Assert.Null(failure.InnerException);
        var values = failure.Error!.Diagnostics.Values.ToDictionary(value => value.Key, value => value.Value);
        Assert.Equal(reason, values["reason"]);
        Assert.Equal(module, values.GetValueOrDefault("module"));
        Assert.Equal(version?.ToString(System.Globalization.CultureInfo.InvariantCulture), values.GetValueOrDefault("version"));

        var logged = Assert.Single(logger.Events, entry => entry.Id.Id == 4012);
        var fields = logged.Fields.ToDictionary(field => field.Key, field => field.Value?.ToString());
        Assert.Equal(reason, fields["Reason"]);
        Assert.Equal(module ?? "(none)", fields["Module"]);
    }

    private static MigrationModule Module(string name, string[] dependsOn, params int[] versions) =>
        Raw(name, dependsOn, [.. versions.SelectMany(version => new[]
        {
            ($"{version:D4}_step.up.sql", $"CREATE TABLE {name}_{version} ();"),
            ($"{version:D4}_step.down.sql", $"DROP TABLE {name}_{version};"),
        })]);

    private static MigrationModule Raw(string name, string[] dependsOn, params (string Name, string Text)[] files) =>
        new(name, new SchemaVersionRange(1, 10), () => files.Select(file => (file.Name, Encoding.UTF8.GetBytes(file.Text))), dependsOn);
}
