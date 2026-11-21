using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Domain.Tests.Ports;

public class WorkKindTests
{
    public static TheoryData<string, string> Valid => new()
    {
        { "a", "b" },
        { "a" + new string('9', 30), "b" + new string('_', 62) },
        { "config", "publish_v2" },
    };

    public static TheoryData<string?, string?> Invalid => new()
    {
        { null, "name" },
        { "module", null },
        { "", "name" },
        { "module", "" },
        { "a" + new string('9', 31), "name" },
        { "module", "b" + new string('_', 63) },
        { "Module", "name" },
        { "module", "Name" },
        { "1module", "name" },
        { "module", "1name" },
        { "module", "na.me" },
        { "mod.ule", "name" },
        { "module", "na-me" },
        { "module", "name\n" },
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void ValidModulesAndNamesAreAccepted(string module, string name)
    {
        var kind = new WorkKind(module, name);

        Assert.Equal((module, name, $"{module}.{name}"), (kind.Module, kind.Name, kind.ToString()));
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void InvalidModulesAndNamesAreRejected(string? module, string? name)
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new WorkKind(module!, name!)).Code);
    }

    [Theory]
    [InlineData("config.publish", true)]
    [InlineData("coordination.purge_expired", true)]
    [InlineData("config", false)]
    [InlineData(".publish", false)]
    [InlineData("config.", false)]
    [InlineData("config.pub.lish", false)]
    [InlineData("Config.publish", false)]
    [InlineData(null, false)]
    public void TryParseAcceptsOnlyTheModuleDotNameForm(string? text, bool parses)
    {
        Assert.Equal(parses, WorkKind.TryParse(text, out var kind));
        Assert.Equal(parses ? text : null, kind?.ToString());
    }

    [Fact]
    public void KindsHaveValueEquality()
    {
        Assert.Equal(new WorkKind("config", "publish"), new WorkKind("config", "publish"));
        Assert.NotEqual(new WorkKind("config", "publish"), new WorkKind("config", "retire"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1_000, true)]
    [InlineData(1_001, false)]
    public void ADeclarationTakesHighestPayloadVersionsOneToOneThousand(int highest, bool accepted)
    {
        var kind = new WorkKind("config", "publish");

        if (accepted)
        {
            Assert.Equal(highest, new WorkKindDeclaration(kind, highest).HighestPayloadVersion);
        }
        else
        {
            Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new WorkKindDeclaration(kind, highest)).Code);
        }
    }

    [Fact]
    public void ADeclarationRequiresAKind()
    {
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new WorkKindDeclaration(null!, 1)).Code);
    }
}
