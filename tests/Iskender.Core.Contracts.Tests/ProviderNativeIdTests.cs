using System.Reflection;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Identifiers;

namespace Iskender.Core.Contracts.Tests;

public class ProviderNativeIdTests
{
    [Fact]
    public void ValuesAtTheBoundsAreAccepted()
    {
        var shortest = new ProviderNativeId("a", "b");
        var longest = new ProviderNativeId(new string('p', 64), new string('v', 256));

        Assert.Equal("a", shortest.Provider);
        Assert.Equal("b", shortest.Value);
        Assert.Equal(64, longest.Provider.Length);
        Assert.Equal(256, longest.Value.Length);
    }

    public static TheoryData<string?, string?> OutOfBounds() => new()
    {
        { null, "v" },
        { "", "v" },
        { new string('p', 65), "v" },
        { "openai", null },
        { "openai", "" },
        { "openai", new string('v', 257) },
        { "openai", "resp\u0000abc" },
        { "openai", "line\nbreak" },
        { "open\tai", "v" },
    };

    [Theory]
    [MemberData(nameof(OutOfBounds))]
    public void ValuesOutsideTheLimitsFailWithValueInvalid(string? provider, string? value)
    {
        var exception = Assert.Throws<GatewayException>(() => new ProviderNativeId(provider!, value!));

        Assert.Equal("core.value.invalid", exception.Code.Id);
    }

    [Fact]
    public void EqualityIsOrdinalOnProviderAndValue()
    {
        Assert.Equal(new ProviderNativeId("openai", "resp_1"), new ProviderNativeId("openai", "resp_1"));
        Assert.NotEqual(new ProviderNativeId("openai", "resp_1"), new ProviderNativeId("azure", "resp_1"));
        Assert.NotEqual(new ProviderNativeId("openai", "resp_1"), new ProviderNativeId("openai", "RESP_1"));
        Assert.True(new ProviderNativeId("openai", "resp_1") == new ProviderNativeId("openai", "resp_1"));
        Assert.True(new ProviderNativeId("openai", "resp_1") != new ProviderNativeId("azure", "resp_1"));
    }

    [Fact]
    public void ThereIsNoConversionToOrFromResourceId()
    {
        Type[] types = [typeof(ProviderNativeId), typeof(ResourceId)];

        foreach (var type in types)
        {
            var conversions = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name is "op_Implicit" or "op_Explicit");
            Assert.Empty(conversions);

            var crossing = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.ReturnType == (type == typeof(ResourceId) ? typeof(ProviderNativeId) : typeof(ResourceId)));
            Assert.Empty(crossing);
        }
    }
}
