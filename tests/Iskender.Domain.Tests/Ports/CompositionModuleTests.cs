using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class CompositionModuleTests
{
    private static readonly SectionSchema First = SectionSchema.Create("first", "Iskender.Tests", 1);

    private static readonly SectionSchema Second = SectionSchema.Create("second", "Iskender.Tests", 1);

    [Fact]
    public void AValidModuleKeepsItsValues()
    {
        var module = new CompositionModule("coordination", [First, Second], ["hosting"]);

        Assert.Equal("coordination", module.Name);
        Assert.Equal([First, Second], module.Sections);
        Assert.Equal(["hosting"], module.Requires);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("persistence-postgresql")]
    [InlineData("a1-")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijkl")]
    public void ANameWithinTheRuleIsAccepted(string name)
    {
        Assert.Equal(name, new CompositionModule(name, [], []).Name);
        Assert.Equal([name], new CompositionModule("hosting", [], [name]).Requires);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1hosting")]
    [InlineData("-hosting")]
    [InlineData("Hosting")]
    [InlineData("hosting_module")]
    [InlineData("hosting.module")]
    [InlineData("hosting module")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklm")]
    public void ANameOutsideTheRuleFails(string name)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CompositionModule(name, [], [])).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CompositionModule("hosting", [], [name])).Code);
    }

    [Fact]
    public void NullArgumentsFail()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CompositionModule(null!, [], [])).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CompositionModule("hosting", null!, [])).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CompositionModule("hosting", [], null!)).Code);
    }

    [Fact]
    public void ANullSectionOrRequirementFails()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CompositionModule("hosting", [First, null!], [])).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CompositionModule("hosting", [], ["coordination", null!])).Code);
    }

    [Fact]
    public void ARepeatedSectionOrRequirementFails()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CompositionModule("hosting", [First, First], [])).Code);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CompositionModule("hosting", [], ["coordination", "coordination"])).Code);
    }

    [Fact]
    public void BothListsAreCopied()
    {
        var sections = new List<SectionSchema> { First };
        var requires = new List<string> { "hosting" };
        var module = new CompositionModule("coordination", sections, requires);

        sections.Add(Second);
        requires.Add("persistence-postgresql");

        Assert.Equal([First], module.Sections);
        Assert.Equal(["hosting"], module.Requires);
    }
}
