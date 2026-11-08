using Iskender.Contracts.Compatibility.Fixtures.Reader;
using Iskender.Contracts.Compatibility.Tests.Support;
using Iskender.Contracts.Compatibility.Tests.Surface;

namespace Iskender.Contracts.Compatibility.Tests;

/// <summary>Each S rule fails on its compiled negative fixture, and every compatible pair reports no break (FR-038).</summary>
public class SurfaceRuleTests
{
    private const string Fixtures = "Iskender.Contracts.Compatibility.Fixtures";

    internal static SurfaceDiff Diff(string pair)
    {
        var assembly = typeof(Sample).Assembly;
        return SurfaceClassifier.Compare(
            SurfaceReader.Read(assembly, $"{Fixtures}.{pair}.Before"),
            SurfaceReader.Read(assembly, $"{Fixtures}.{pair}.After"));
    }

    private static SurfaceChange Only(string kind)
    {
        var diff = Diff($"Kinds.{kind}");
        return Assert.Single(diff.Breaks);
    }

    [Fact]
    [ProvesRule("S1")]
    [Trait("Category", "BreakCatalog")]
    public void RemovingATypeFailsS1()
    {
        var change = Only("S1");

        Assert.Equal("S1", change.RuleId);
        Assert.Equal("type Widget", change.Item);
    }

    [Fact]
    [ProvesRule("S2")]
    [Trait("Category", "BreakCatalog")]
    public void RemovingAMemberFailsS2()
    {
        var change = Only("S2");

        Assert.Equal("S2", change.RuleId);
        Assert.Equal("method Widget::Count()", change.Item);
    }

    [Fact]
    [ProvesRule("S3")]
    [Trait("Category", "BreakCatalog")]
    public void RenamingAParameterFailsS3()
    {
        var change = Only("S3");

        Assert.Equal("S3", change.RuleId);
        Assert.Equal("method Widget::Scale(System.Int32)", change.Item);
    }

    [Fact]
    [ProvesRule("S4")]
    [Trait("Category", "BreakCatalog")]
    public void ChangingAReturnTypeFailsS4()
    {
        var change = Only("S4");

        Assert.Equal("S4", change.RuleId);
        Assert.Equal("method Widget::Size()", change.Item);
    }

    [Fact]
    [ProvesRule("S5")]
    [Trait("Category", "BreakCatalog")]
    public void ChangingANullableAnnotationFailsS5IncludingOnAGenericArgument()
    {
        var diff = Diff("Kinds.S5");

        Assert.All(diff.Breaks, change => Assert.Equal("S5", change.RuleId));
        Assert.Equal(["method Widget::Find()", "method Widget::Names()"], diff.Breaks.Select(change => change.Item).Order(StringComparer.Ordinal));
    }

    [Fact]
    [ProvesRule("S6")]
    [Trait("Category", "BreakCatalog")]
    public void SealingATypeFailsS6()
    {
        var change = Only("S6");

        Assert.Equal("S6", change.RuleId);
        Assert.Equal("type Widget", change.Item);
    }

    [Fact]
    [ProvesRule("S7")]
    [Trait("Category", "BreakCatalog")]
    public void AddingAnAbstractMemberFailsS7()
    {
        var change = Only("S7");

        Assert.Equal("S7", change.RuleId);
        Assert.Equal("method Widget::Weight()", change.Item);
    }

    [Fact]
    [ProvesRule("S8")]
    [Trait("Category", "BreakCatalog")]
    public void RenumberingAnEnumValueFailsS8()
    {
        var change = Only("S8");

        Assert.Equal("S8", change.RuleId);
        Assert.Equal("enum-value Level::High", change.Item);
    }

    [Fact]
    [ProvesRule("S9")]
    [Trait("Category", "BreakCatalog")]
    public void RemovingAnInterfaceFailsS9()
    {
        var change = Only("S9");

        Assert.Equal("S9", change.RuleId);
        Assert.Equal("type Widget", change.Item);
    }

    [Theory]
    [InlineData("AddedType")]
    [InlineData("AddedMember")]
    [InlineData("AddedEnumValue")]
    [InlineData("NewObsolete")]
    public void ACompatiblePairReportsNoBreak(string pair)
    {
        Assert.Empty(Diff($"Compatible.{pair}").Breaks);
    }

    [Fact]
    public void AdditionsAndDeprecationsAreReported()
    {
        Assert.Contains("type Gadget", Diff("Compatible.AddedType").Additions);
        Assert.Equal(["method Widget::Weight()"], Diff("Compatible.AddedMember").Additions);
        Assert.Equal(["enum-value Level::High"], Diff("Compatible.AddedEnumValue").Additions);

        var deprecation = Assert.Single(Diff("Compatible.NewObsolete").Deprecations);
        Assert.Equal("method Widget::Size()", deprecation.Item);
    }

    [Fact]
    public void LineLevelChangesAreClassified()
    {
        static IReadOnlyList<SurfaceChange> Breaks(string before, string after) => SurfaceClassifier.Compare([before], [after]).Breaks;

        Assert.Equal("S6", Assert.Single(Breaks("method A::M() | access=public; virtual; returns=System.Void", "method A::M() | access=public; returns=System.Void")).RuleId);
        Assert.Equal("S6", Assert.Single(Breaks("method A::M() | access=public; returns=System.Void", "method A::M() | access=protected; returns=System.Void")).RuleId);
        Assert.Equal("S4", Assert.Single(Breaks("method A::M(System.Int32) | access=public; returns=System.Void; params=x=2", "method A::M(System.Int32) | access=public; returns=System.Void; params=x")).RuleId);
        Assert.Equal("S4", Assert.Single(Breaks("method A::M(System.Int32&) | access=public; returns=System.Void; params=ref x", "method A::M(System.Int32&) | access=public; returns=System.Void; params=out x")).RuleId);
        Assert.Equal("S5", Assert.Single(Breaks("method A::M(System.String) | access=public; returns=System.Void; params=x?", "method A::M(System.String) | access=public; returns=System.Void; params=x!")).RuleId);
        Assert.Equal("S6", Assert.Single(Breaks("property A::P | access=public; type=System.Int32; get=public; set=public", "property A::P | access=public; type=System.Int32; get=public; set=protected")).RuleId);
        Assert.Equal("S9", Assert.Single(Breaks("type A | access=public; kind=class", "type A | access=public; kind=struct")).RuleId);
        Assert.Empty(Breaks("method A::M() | access=public; returns=System.Void; since=x@1", "method A::M() | access=public; returns=System.Void; since=x@1"));
    }
}
