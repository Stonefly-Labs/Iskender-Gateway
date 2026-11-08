using System.Reflection;
using Iskender.Contracts.Compatibility.Fixtures.Reader;
using Iskender.Contracts.Compatibility.Tests.Harness;
using Iskender.Contracts.Compatibility.Tests.Support;
using Iskender.Contracts.Compatibility.Tests.Wire;

namespace Iskender.Contracts.Compatibility.Tests;

/// <summary>
/// H8: every S, W, G, and V rule has a break-catalog test on a negative fixture, every H rule has a test, every
/// <c>[ProvesRule]</c> names a catalog rule, and every compatible fixture passes (FR-038, SC-002).
/// </summary>
public class RuleCoverageTests
{
    private const BindingFlags AllMethods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string FixtureRoot = Path.Combine(Repository.Root(), Repository.HarnessDirectory, "fixtures", "compatible");

    /// <summary>A rule's tests, and whether each is in the BreakCatalog category. A skipped test, or a method that is not a test, does not count.</summary>
    internal sealed record Proof(string RuleId, bool InBreakCatalog);

    internal static List<Proof> Proofs(IEnumerable<MethodInfo> methods) =>
        methods
            .Where(method => method.GetCustomAttribute<FactAttribute>() is { Skip: null })
            .SelectMany(method => method.GetCustomAttributes<ProvesRuleAttribute>().Select(attribute => new Proof(
                attribute.RuleId,
                method.GetCustomAttributes<TraitAttribute>().Any(trait => trait.Name == "Category" && trait.Value == "BreakCatalog"))))
            .ToList();

    internal static List<string> Gaps(IReadOnlyList<Rule> rules, IReadOnlyList<Proof> proofs)
    {
        var gaps = new List<string>();
        foreach (var rule in rules)
        {
            var mine = proofs.Where(proof => proof.RuleId == rule.Id).ToList();
            if (rule.Family is 'S' or 'W' or 'G' or 'V' && !mine.Any(proof => proof.InBreakCatalog))
            {
                gaps.Add($"{rule.Id}: no [ProvesRule] test with Category=BreakCatalog");
            }
            else if (mine.Count == 0)
            {
                gaps.Add($"{rule.Id}: no [ProvesRule] test");
            }
        }

        gaps.AddRange(proofs.Select(proof => proof.RuleId).Distinct().Where(id => !rules.Any(rule => rule.Id == id)).Select(id => $"{id}: a [ProvesRule] test names a rule the catalog does not list"));
        return gaps;
    }

    private static IEnumerable<MethodInfo> DiscoverableMethods() =>
        typeof(RuleCoverageTests).Assembly.GetTypes()
            .Where(type => !type.IsAbstract || type.IsSealed)
            .SelectMany(type => type.GetMethods(AllMethods));

    [Fact]
    [ProvesRule("H8")]
    public void EveryRuleHasATestAndEveryTestNamesARule()
    {
        Assert.Empty(Gaps(RuleCatalog.All, Proofs(DiscoverableMethods())));
    }

    [Fact]
    [ProvesRule("H8")]
    public void AGapFailsH8()
    {
        var rules = new[] { RuleCatalog.Get("S1"), RuleCatalog.Get("H1") };

        Assert.Equal(
            ["S1: no [ProvesRule] test with Category=BreakCatalog", "H1: no [ProvesRule] test", "X9: a [ProvesRule] test names a rule the catalog does not list"],
            Gaps(rules, [new Proof("S1", InBreakCatalog: false), new Proof("X9", InBreakCatalog: true)]));
        Assert.Empty(Gaps(rules, [new Proof("S1", InBreakCatalog: true), new Proof("H1", InBreakCatalog: false)]));
    }

    [Fact]
    public void SkippedTestsAndPlainMethodsDoNotCount()
    {
        Assert.Empty(Proofs(typeof(SyntheticCoverageMethods).GetMethods(AllMethods)));
    }

    [Fact]
    [ProvesRule("H8")]
    public void EveryCompatibleFixturePasses()
    {
        var failures = new List<string>();
        var assembly = typeof(Sample).Assembly;
        const string Prefix = "Iskender.Contracts.Compatibility.Fixtures.Compatible.";
        var surfacePairs = assembly.GetTypes()
            .Select(type => type.Namespace)
            .Where(name => name is not null && name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(".Before", StringComparison.Ordinal))
            .Select(name => name![Prefix.Length..^".Before".Length])
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (var pair in surfacePairs)
        {
            failures.AddRange(SurfaceRuleTests.Diff($"Compatible.{pair}").Breaks.Select(change => $"surface {pair}: {change.RuleId} {change.Item}"));
        }

        var wirePairs = Directory.GetDirectories(Path.Combine(FixtureRoot, "wire")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        foreach (var pair in wirePairs)
        {
            var violations = pair == "new-version"
                ? WireRuleTests.RunPair($"compatible/wire/{pair}", new WireVersions("error-envelope", 2, [1, 2]), new WireRuleTests.VersionTwoCodec())
                : WireRuleTests.RunPair($"compatible/wire/{pair}");
            failures.AddRange(violations.Select(violation => $"wire {pair}: {violation.RuleId} {violation.Item}"));
        }

        failures.AddRange(RegistryRuleTests.RunPair("compatible/registries").Select(change => $"registries: {change.RuleId} {change.Item}"));

        Assert.Equal(["AddedEnumValue", "AddedMember", "AddedType", "NewObsolete"], surfacePairs);
        Assert.Equal(["added-optional-field", "new-version", "safe-message-change"], wirePairs);
        Assert.Empty(failures);
    }

    /// <summary>
    /// Methods that look like proofs but must not count: one is skipped, and one is not a test. The class is abstract, so
    /// xUnit never runs them.
    /// </summary>
    public abstract class SyntheticCoverageMethods
    {
        [Fact(Skip = "Synthetic: a skipped test proves nothing.")]
        [ProvesRule("S1")]
        [Trait("Category", "BreakCatalog")]
        public abstract void Skipped();

        [ProvesRule("S2")]
        public abstract void NotATest();
    }
}
