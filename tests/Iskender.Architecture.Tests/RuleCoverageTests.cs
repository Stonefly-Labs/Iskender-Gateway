using System.Reflection;
using Iskender.Architecture.Tests.Support;

namespace Iskender.Architecture.Tests;

/// <summary>Every rule has at least one negative fixture, and every fixture names a real rule (FR-029, SC-003).</summary>
public class RuleCoverageTests
{
    private const BindingFlags AllMethods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly RuleSource Source = RuleSource.Load(RepositoryPaths.Root()).Source!;

    /// <summary>A rule with no fixture unless it is retired, then a fixture naming a rule the source does not list.</summary>
    private static List<string> Gaps(IReadOnlyCollection<RuleDefinition> rules, IReadOnlyCollection<string> proved)
    {
        var unproved = rules
            .Where(rule => !rule.Retired && !proved.Contains(rule.Id, StringComparer.Ordinal))
            .Select(rule => $"{rule.Id}: no [ProvesRule] test");
        var unknown = proved
            .Where(id => !rules.Any(rule => string.Equals(rule.Id, id, StringComparison.Ordinal)))
            .Select(id => $"{id}: a [ProvesRule] test names a rule the source does not list");

        return [.. unproved.Concat(unknown)];
    }

    /// <summary>Only a test that runs proves anything, so a method that is not a test, or a skipped one, does not count.</summary>
    private static List<string> ProvedIds(IEnumerable<MethodInfo> methods) =>
        methods
            .Where(method => method.GetCustomAttribute<FactAttribute>() is { Skip: null })
            .SelectMany(method => method.GetCustomAttributes<ProvesRuleAttribute>())
            .Select(attribute => attribute.RuleId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>The methods xUnit can run. It skips an abstract class, though a static class counts, which is abstract and sealed.</summary>
    private static IEnumerable<MethodInfo> DiscoverableMethods() =>
        typeof(RuleCoverageTests).Assembly.GetTypes()
            .Where(type => !type.IsAbstract || type.IsSealed)
            .SelectMany(type => type.GetMethods(AllMethods));

    private static RuleDefinition Rule(string id, bool retired = false) => new(id, "Statement.", "Source", retired);

    [Fact]
    public void EveryRuleHasAFixtureAndEveryFixtureNamesARule()
    {
        Assert.Empty(Gaps(Source.Rules, ProvedIds(DiscoverableMethods())));
    }

    [Fact]
    public void TheSyntheticMethodsDoNotCountAsFixtures()
    {
        Assert.DoesNotContain(DiscoverableMethods(), method => method.DeclaringType == typeof(SyntheticCoverageMethods));
    }

    [Fact]
    public void TheSourceListsTheContractsRules()
    {
        Assert.Equal(35, Source.Rules.Count);
    }

    [Fact]
    public void ARuleWithNoFixtureIsAGap()
    {
        var gap = Assert.Single(Gaps([Rule("A1"), Rule("A2")], ["A1"]));

        Assert.StartsWith("A2:", gap, StringComparison.Ordinal);
    }

    [Fact]
    public void AFixtureNamingAnUnlistedRuleIsAGap()
    {
        var gap = Assert.Single(Gaps([Rule("A1")], ["A1", "Z9"]));

        Assert.StartsWith("Z9:", gap, StringComparison.Ordinal);
    }

    [Fact]
    public void ARetiredRuleNeedsNoFixtureAndMayKeepOne()
    {
        Assert.Empty(Gaps([Rule("A1", retired: true)], []));
        Assert.Empty(Gaps([Rule("A1", retired: true)], ["A1"]));
    }

    [Fact]
    public void ASkippedOrNonTestMethodProvesNothing()
    {
        var methods = typeof(SyntheticCoverageMethods).GetMethods(AllMethods);

        Assert.Equal(["A1", "A2"], ProvedIds(methods).Order(StringComparer.Ordinal));
    }
}

/// <summary>
/// Input for <see cref="RuleCoverageTests.ASkippedOrNonTestMethodProvesNothing"/>. The class is abstract, so xUnit
/// discovers none of these and none of them runs.
/// </summary>
public abstract class SyntheticCoverageMethods
{
    [Fact]
    [ProvesRule("A1")]
    public abstract void Counted();

    [Theory]
    [InlineData(1)]
    [ProvesRule("A2")]
    public abstract void CountedTheory(int value);

    [Fact(Skip = "Synthetic: a skipped test proves nothing.")]
    [ProvesRule("A3")]
    public abstract void Skipped();

    [ProvesRule("A4")]
    public abstract void NotATest();
}
