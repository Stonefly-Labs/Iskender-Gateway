using System.Text;
using System.Text.Json;
using Iskender.Contracts.Compatibility.Tests.Harness;
using Iskender.Contracts.Compatibility.Tests.Support;
using Iskender.Contracts.Compatibility.Tests.Wire;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Contracts.Compatibility.Tests;

/// <summary>Each W rule fails on its before-and-after fixture set, and the compatible sets pass (FR-038, SC-007).</summary>
public class WireRuleTests
{
    private static readonly string Fixtures = Path.Combine(Repository.Root(), Repository.HarnessDirectory, "fixtures");

    private static readonly WireVersions EnvelopeV1 = WireVersions.From(WireContracts.ErrorEnvelope);

    internal static IReadOnlyList<Violation> RunPair(string pair, WireVersions? versions = null, IEnvelopeCodec? codec = null)
    {
        var before = new DirectoryFileSet(Path.Combine(Fixtures, pair, "before"), "before");
        var after = new DirectoryFileSet(Path.Combine(Fixtures, pair, "after"), "after");
        return new WireChecks(before, string.Empty, after, string.Empty).ErrorEnvelope(versions ?? EnvelopeV1, codec ?? EnvelopeFixtureCodec.Instance);
    }

    private static Violation Only(string rule)
    {
        var violation = Assert.Single(RunPair($"breaks/{rule}"));
        Assert.Equal(rule, violation.RuleId);
        return violation;
    }

    [Fact]
    [ProvesRule("W1")]
    [Trait("Category", "BreakCatalog")]
    public void RenamingAFieldFailsW1()
    {
        Assert.Equal("category", Only("W1").Item);
    }

    [Fact]
    [ProvesRule("W2")]
    [Trait("Category", "BreakCatalog")]
    public void ChangingAFieldTypeFailsW2()
    {
        Assert.Equal("schema_version", Only("W2").Item);
    }

    [Fact]
    [ProvesRule("W3")]
    [Trait("Category", "BreakCatalog")]
    public void MakingAnOptionalFieldRequiredFailsW3()
    {
        Assert.Equal("retry", Only("W3").Item);
    }

    [Fact]
    [ProvesRule("W4")]
    [Trait("Category", "BreakCatalog")]
    public void ATightenedReaderFailsW4()
    {
        var violations = RunPair("breaks/W4");

        Assert.NotEmpty(violations);
        Assert.All(violations, violation => Assert.Equal("W4", violation.RuleId));
        Assert.Contains(violations, violation => violation.Item == "error-envelope/v1/legacy.envelope.json" && violation.WhatChanged.Contains("core.value.invalid", StringComparison.Ordinal));
    }

    [Fact]
    [ProvesRule("W5")]
    [Trait("Category", "BreakCatalog")]
    public void WriterOutputThatDiffersFromItsFixtureFailsW5()
    {
        Assert.Equal("error-envelope/v1/minimal.envelope.json", Only("W5").Item);
    }

    [Fact]
    [ProvesRule("W6")]
    [Trait("Category", "BreakCatalog")]
    public void RemovingAFixtureFailsW6()
    {
        var violations = RunPair("breaks/W6");

        Assert.All(violations, violation => Assert.Equal("W6", violation.RuleId));
        Assert.Equal(
            ["error-envelope/v1/retry.envelope.json", "error-envelope/v1/retry.input.json", "error-envelope/v1/retry.read.json"],
            violations.Select(violation => violation.Item).Order(StringComparer.Ordinal));
    }

    [Fact]
    [ProvesRule("W6")]
    [Trait("Category", "BreakCatalog")]
    public void DroppingASupportedVersionFailsW6()
    {
        var violations = RunPair("breaks/W6", new WireVersions("error-envelope", 2, [2]));

        Assert.Contains(violations, violation => violation.RuleId == "W6" && violation.WhatChanged == "supported version dropped");
    }

    [Theory]
    [InlineData("compatible/wire/added-optional-field")]
    [InlineData("compatible/wire/safe-message-change")]
    public void ACompatibleWireChangePasses(string pair)
    {
        Assert.Empty(RunPair(pair));
    }

    [Fact]
    public void ANewVersionThatKeepsTheOldOneReadablePasses()
    {
        Assert.Empty(RunPair("compatible/wire/new-version", new WireVersions("error-envelope", 2, [1, 2]), new VersionTwoCodec()));
    }

    [Fact]
    [ProvesRule("H1")]
    public void ASupportedVersionWithNoFixtureFailsH1()
    {
        var violations = RunPair("compatible/wire/added-optional-field", new WireVersions("error-envelope", 2, [1, 2]), new VersionTwoCodec());

        var violation = Assert.Single(violations);
        Assert.Equal("H1", violation.RuleId);
        Assert.Equal("error-envelope/v2", violation.Item);
    }

    [Fact]
    public void TheRepositoryFixturesPassAgainstThemselves()
    {
        var checkout = new DirectoryFileSet(Repository.Root(), "the checkout");
        var root = Repository.HarnessDirectory + "/fixtures/wire";

        Assert.Empty(new WireChecks(checkout, root, checkout, root).RunAll());
    }

    [Fact]
    public void AResourceIdTextChangeFailsW4()
    {
        var root = Path.Combine(Repository.Root(), Repository.HarnessDirectory, "fixtures/wire");
        var current = new DirectoryFileSet(root, "the checkout");
        var changed = new InMemoryFileSet(
            "base",
            new Dictionary<string, string>
            {
                ["resource-id/v1/valid.txt"] = "ORG_0123456789ABCDEFGHJKMNPQRS\n",
                ["resource-id/v1/invalid.txt"] = File.ReadAllText(Path.Combine(root, "resource-id/v1/invalid.txt")),
            });

        var violations = new WireChecks(changed, string.Empty, current, string.Empty).ResourceId(WireVersions.From(WireContracts.ResourceId));

        Assert.Contains(violations, violation => violation.RuleId == "W4" && violation.Item.EndsWith("ORG_0123456789ABCDEFGHJKMNPQRS", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.RuleId == "W6" && violation.Item == "resource-id/v1/valid.txt");
    }

    /// <summary>
    /// A simulated version 2 that only changes <c>schema_version</c>, so a test can show what a version increase needs.
    /// It reads version 1 with the real codec.
    /// </summary>
    private sealed class VersionTwoCodec : IEnvelopeCodec
    {
        public byte[] Write(JsonElement input) =>
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(EnvelopeFixtureCodec.Instance.Write(input)).Replace("\"schema_version\":1", "\"schema_version\":2", StringComparison.Ordinal));

        public IReadOnlyDictionary<string, string?> Read(byte[] envelope)
        {
            var text = Encoding.UTF8.GetString(envelope);
            if (!text.Contains("\"schema_version\":2", StringComparison.Ordinal))
            {
                return EnvelopeFixtureCodec.Instance.Read(envelope);
            }

            var read = new Dictionary<string, string?>(EnvelopeFixtureCodec.Instance.Read(Encoding.UTF8.GetBytes(text.Replace("\"schema_version\":2", "\"schema_version\":1", StringComparison.Ordinal))), StringComparer.Ordinal)
            {
                ["schema_version"] = "2",
            };
            return read;
        }
    }
}

/// <summary>Files held in memory, for a base that exists only in a test.</summary>
public sealed class InMemoryFileSet(string name, IReadOnlyDictionary<string, string> files) : IFileSet
{
    public string Name { get; } = name;

    public byte[]? Read(string path) => files.TryGetValue(path, out var text) ? Encoding.UTF8.GetBytes(text) : null;

    public IReadOnlyList<string> List(string directory) =>
        files.Keys.Where(path => path.StartsWith(directory.TrimEnd('/') + "/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
}
