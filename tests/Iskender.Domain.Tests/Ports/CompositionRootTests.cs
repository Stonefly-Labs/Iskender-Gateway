using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

/// <summary><see cref="CompositionRoot"/>'s name rule and its requirement name (research R2).</summary>
public class CompositionRootTests
{
    [Theory]
    [InlineData(CompositionPlane.DataPlane, "hosting")]
    [InlineData(CompositionPlane.ControlPlane, "control-plane")]
    public void ARootKeepsItsPlaneAndModule(CompositionPlane plane, string module)
    {
        var root = new CompositionRoot(plane, module);

        Assert.Equal((plane, module), (root.Plane, root.Module));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hosting")]
    [InlineData("1hosting")]
    [InlineData("control_plane")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklm")]
    [InlineData(null)]
    public void AModuleOutsideTheNameRuleFails(string? module)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CompositionRoot(CompositionPlane.DataPlane, module!)).Code);
    }

    [Fact]
    public void AnUndefinedPlaneFails()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new CompositionRoot((CompositionPlane)7, "hosting")).Code);
    }

    [Fact]
    public void TheRequirementEitherRootMeetsIsHosting()
    {
        Assert.Equal("hosting", CompositionRoot.Requirement);
    }
}
