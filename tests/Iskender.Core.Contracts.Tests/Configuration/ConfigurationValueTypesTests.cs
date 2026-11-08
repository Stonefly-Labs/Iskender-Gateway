using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Tests.Configuration;

public class ConfigurationValueTypesTests
{
    [Fact]
    public void FieldKindsAreTheTenDeclaredTypes()
    {
        Assert.Equal(
            ["Text", "WholeNumber", "Number", "Boolean", "Enumeration", "Duration", "Uri", "Section", "Array", "SecretReference"],
            Enum.GetNames<ConfigurationFieldKind>());
    }

    [Fact]
    public void LayersAreOrderedLowestPrecedenceFirst()
    {
        Assert.Equal(["Defaults", "Base", "Overlay", "Environment"], Enum.GetNames<ConfigurationLayer>());
        Assert.True(ConfigurationLayer.Defaults < ConfigurationLayer.Base);
        Assert.True(ConfigurationLayer.Overlay < ConfigurationLayer.Environment);
    }

    [Theory]
    [InlineData(ConfigurationLayer.Defaults, "defaults")]
    [InlineData(ConfigurationLayer.Base, "base")]
    [InlineData(ConfigurationLayer.Overlay, "overlay")]
    [InlineData(ConfigurationLayer.Environment, "environment")]
    public void LocationRendersSourceLineColumnAndLayer(ConfigurationLayer layer, string text)
    {
        Assert.Equal($"gateway.yaml:12:5, {text}", new ConfigurationSourceLocation("gateway.yaml", 12, 5, layer).ToString());
    }

    [Fact]
    public void LocationHasValueEquality()
    {
        var a = new ConfigurationSourceLocation("a.json", 1, 2, ConfigurationLayer.Base);

        Assert.Equal(a, new ConfigurationSourceLocation("a.json", 1, 2, ConfigurationLayer.Base));
        Assert.Equal(a.GetHashCode(), new ConfigurationSourceLocation("a.json", 1, 2, ConfigurationLayer.Base).GetHashCode());
        Assert.NotEqual(a, new ConfigurationSourceLocation("a.json", 1, 2, ConfigurationLayer.Overlay));
        Assert.NotEqual(a, new ConfigurationSourceLocation("A.json", 1, 2, ConfigurationLayer.Base));
        Assert.True(a == new ConfigurationSourceLocation("a.json", 1, 2, ConfigurationLayer.Base));
        Assert.True(a != new ConfigurationSourceLocation("a.json", 2, 2, ConfigurationLayer.Base));
    }

    [Fact]
    public void NoneLocationIsNoneLineZeroColumnZeroDefaults()
    {
        Assert.Equal("(none):0:0, defaults", ConfigurationSourceLocation.None.ToString());
        Assert.Equal(ConfigurationLayer.Defaults, ConfigurationSourceLocation.None.Layer);
    }

    [Fact]
    public void LocationRejectsAnEmptySourceANegativePositionAndAnUnknownLayer()
    {
        Assert.Equal(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => new ConfigurationSourceLocation("", 1, 1, ConfigurationLayer.Base)).Code);
        Assert.Throws<GatewayException>(() => new ConfigurationSourceLocation(null!, 1, 1, ConfigurationLayer.Base));
        Assert.Throws<GatewayException>(() => new ConfigurationSourceLocation("a", -1, 1, ConfigurationLayer.Base));
        Assert.Throws<GatewayException>(() => new ConfigurationSourceLocation("a", 1, -1, ConfigurationLayer.Base));
        Assert.Throws<GatewayException>(() => new ConfigurationSourceLocation("a", 1, 1, (ConfigurationLayer)9));
    }

    [Fact]
    public void SectionVersionHasValueEqualityAndRendersNameAtVersion()
    {
        var version = new SectionVersion("persistence", 2);

        Assert.Equal(new SectionVersion("persistence", 2), version);
        Assert.NotEqual(new SectionVersion("persistence", 3), version);
        Assert.Equal("persistence@2", version.ToString());
        Assert.Throws<GatewayException>(() => new SectionVersion("", 1));
        Assert.Throws<GatewayException>(() => new SectionVersion("persistence", 0));
    }

    [Fact]
    public void LayersRecordSourcesOrNoneAndTheVariableCount()
    {
        var layers = new ConfigurationLayers("gateway.yaml", null, 3);

        Assert.Equal("gateway.yaml", layers.BaseSource);
        Assert.Null(layers.OverlaySource);
        Assert.Equal(3, layers.EnvironmentVariableCount);
        Assert.Equal(new ConfigurationLayers("gateway.yaml", null, 3), layers);
        Assert.NotEqual(new ConfigurationLayers("gateway.yaml", "o.yaml", 3), layers);
        Assert.Throws<GatewayException>(() => new ConfigurationLayers("", null, 0));
        Assert.Throws<GatewayException>(() => new ConfigurationLayers(null, null, -1));
    }

    [Theory]
    [InlineData(typeof(ConfigurationFieldKind))]
    [InlineData(typeof(ConfigurationLayer))]
    [InlineData(typeof(ConfigurationSourceLocation))]
    [InlineData(typeof(SectionVersion))]
    [InlineData(typeof(ConfigurationLayers))]
    public void TypesAreTaggedAtVersionTwo(Type type)
    {
        var tag = Assert.Single(type.GetCustomAttributes(typeof(ContractVersionAttribute), inherit: false).Cast<ContractVersionAttribute>());

        Assert.Equal("core-contracts", tag.ContractId);
        Assert.Equal(2, tag.Since);
    }

    [Theory]
    [InlineData(typeof(ConfigurationSourceLocation))]
    [InlineData(typeof(SectionVersion))]
    [InlineData(typeof(ConfigurationLayers))]
    public void ValueTypesAreSealedAndImmutable(Type type)
    {
        Assert.True(type.IsSealed);
        Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
    }
}
