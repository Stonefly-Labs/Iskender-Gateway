using Iskender.Core.Contracts.Errors;
using Iskender.Infrastructure.PostgreSql.Migrations;

namespace Iskender.Infrastructure.PostgreSql.Tests.Modules;

public class FixtureModulesTests
{
    [Fact]
    public void ReleaseOneIsAlphaOneToThreeThenBetaOneToThree()
    {
        Assert.Equal(["alpha/1", "alpha/2", "alpha/3", "beta/1", "beta/2", "beta/3"], FixtureModules.Release1.Migrations.Select(script => script.Identity.ToString()));
    }

    [Fact]
    public void ReleaseTwoAddsAlphaFourAndAlphaNextAddsSix()
    {
        Assert.Equal(["alpha/1", "alpha/2", "alpha/3", "alpha/4", "beta/1", "beta/2", "beta/3"], FixtureModules.Release2.Migrations.Select(script => script.Identity.ToString()));
        Assert.Equal([1, 2, 3, 4, 6], new MigrationSet([FixtureModules.AlphaNext]).Migrations.Select(script => script.Identity.Version));
    }

    [Fact]
    public void OnlyAlphaThreeIsDataLosing()
    {
        Assert.Equal(["alpha/3"], FixtureModules.Release2.Migrations.Where(script => script.DataLosing).Select(script => script.Identity.ToString()));
    }

    [Fact]
    public void TheEditedAlphaDiffersOnlyInVersionOne()
    {
        var shipped = FixtureModules.Release1;
        var edited = new MigrationSet([FixtureModules.AlphaEdited]);

        Assert.True(shipped.TryGetScript("alpha", 1, out var original));
        Assert.NotEqual(original.Checksum, edited.Migrations[0].Checksum);
        Assert.Equal(shipped.Migrations.Skip(1).Take(2).Select(script => script.Checksum), edited.Migrations.Skip(1).Select(script => script.Checksum));
    }

    public static TheoryData<string, string> StaticallyInvalid() => new()
    {
        { nameof(FixtureModules.MissingReverse), MigrationRefusal.MissingReverse },
        { nameof(FixtureModules.DuplicateVersion), MigrationRefusal.DuplicateVersion },
        { nameof(FixtureModules.CycleA), MigrationRefusal.DependencyCycle },
        { nameof(FixtureModules.CrossSchema), MigrationRefusal.CrossSchemaReference },
    };

    [Theory]
    [MemberData(nameof(StaticallyInvalid))]
    public void EachStaticallyInvalidSetIsRefusedBeforeAnythingIsApplied(string module, string reason)
    {
        MigrationModule[] modules = module == nameof(FixtureModules.CycleA)
            ? [FixtureModules.CycleA, FixtureModules.CycleB]
            : [FixtureModules.Alpha, (MigrationModule)typeof(FixtureModules).GetProperty(module)!.GetValue(null)!];

        var failure = Assert.Throws<GatewayException>(() => new MigrationSet(modules));

        Assert.Same(ErrorCodes.PersistenceSchemaIncompatible, failure.Code);
        Assert.Contains(failure.Error!.Diagnostics.Values, value => value.Key == "reason" && value.Value == reason);
    }

    [Fact]
    public void TheSetsThatFailOnlyInTheDatabaseValidate()
    {
        var set = new MigrationSet([FixtureModules.ForwardFails, FixtureModules.FailsFirst, FixtureModules.NoPolicy]);

        Assert.Equal(["fails_first/1", "forward_fails/1", "forward_fails/2", "no_policy/1"], set.Migrations.Select(script => script.Identity.ToString()));
    }

    [Fact]
    public void NoFixtureScriptShipsInAnAdapterAssembly()
    {
        Assert.DoesNotContain(typeof(PostgreSqlDataSources).Assembly.GetManifestResourceNames(), name => name.EndsWith(".sql", StringComparison.Ordinal));
        Assert.Equal(45, typeof(FixtureModules).Assembly.GetManifestResourceNames().Count(name => name.Contains(".Modules.", StringComparison.Ordinal)));
    }
}
