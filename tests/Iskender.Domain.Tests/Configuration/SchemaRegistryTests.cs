using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

public class SchemaRegistryTests
{
    private static FieldSchema Field(string name, ConfigurationFieldKind kind) => FieldSchema.Create(name, kind);

    private static SectionSchema Section(params FieldSchema[] fields) => SectionSchema.Create("persistence", "Iskender.Domain.Persistence", 1).WithFields(fields);

    private static Dictionary<string, string> AssertInvalid(params SectionSchema[] schemas)
    {
        var exception = Assert.Throws<GatewayException>(() => ConfigurationSchemaRegistry.Create(schemas));

        Assert.Equal(ErrorCodes.ConfigSchemaInvalid, exception.Code);
        Assert.Equal(ErrorCategory.Internal, exception.Code.Category);
        var values = exception.Error!.Diagnostics.Values.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Contains("namespace", values.Keys);
        Assert.Contains("field", values.Keys);
        Assert.Contains("rule", values.Keys);
        return values;
    }

    private static FieldSchema WithConstraints(ConfigurationFieldKind kind, Func<FieldConstraints, FieldConstraints> build) =>
        Field("value", kind).WithConstraints(build(FieldConstraints.None));

    [Fact]
    public void AValidRegistryKeepsEverySchemaInNamespaceOrder()
    {
        var beta = SectionSchema.Create("beta", "Beta", 1);
        var alpha = SectionSchema.Create("alpha", "Alpha", 2).WithFields(Field("name", ConfigurationFieldKind.Text));

        var registry = ConfigurationSchemaRegistry.Create([beta, alpha, TestSchemas.Persistence]);

        Assert.Equal(["alpha", "beta", "persistence"], registry.Schemas.Select(schema => schema.Namespace));
        Assert.Same(alpha, registry.Schemas[0]);
    }

    [Fact]
    public void TheFullCoverageSchemaIsValid()
    {
        Assert.Equal(3, TestSchemas.Registry().Schemas.Count);
    }

    [Fact]
    public void TwoRegistrationsOfOneNamespaceNameBothOwners()
    {
        var exception = Assert.Throws<GatewayException>(() => ConfigurationSchemaRegistry.Create(
            [SectionSchema.Create("persistence", "First.Module", 1), SectionSchema.Create("persistence", "Second.Module", 1)]));

        Assert.Equal(ErrorCodes.ConfigSchemaDuplicateNamespace, exception.Code);
        var owners = exception.Error!.Diagnostics.Values.Select(pair => pair.Value).ToList();
        Assert.Contains("First.Module", owners);
        Assert.Contains("Second.Module", owners);
        Assert.DoesNotContain("First.Module", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneModuleRegisteringANamespaceTwiceOrWithTwoVersionsIsADuplicate()
    {
        var schema = SectionSchema.Create("persistence", "Module", 1);

        Assert.Equal(ErrorCodes.ConfigSchemaDuplicateNamespace,
            Assert.Throws<GatewayException>(() => ConfigurationSchemaRegistry.Create([schema, schema])).Code);
        Assert.Equal(ErrorCodes.ConfigSchemaDuplicateNamespace,
            Assert.Throws<GatewayException>(() => ConfigurationSchemaRegistry.Create([schema, SectionSchema.Create("persistence", "Module", 2)])).Code);
    }

    [Fact]
    public void TheFirstFaultInNamespaceOrderIsReported()
    {
        var values = AssertInvalid(
            SectionSchema.Create("zeta", "Z", 1).WithFields(Field("Bad", ConfigurationFieldKind.Text)),
            SectionSchema.Create("alpha", "A", 0));

        Assert.Equal("alpha", values["namespace"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1abc")]
    [InlineData("-abc")]
    [InlineData("Abc")]
    [InlineData("a_b")]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234567")]
    public void AMalformedNamespaceIsInvalid(string ns)
    {
        AssertInvalid(SectionSchema.Create(ns, "Module", 1));
    }

    [Fact]
    public void ANamespaceOf32CharactersWithDigitsAndHyphensIsValid()
    {
        ConfigurationSchemaRegistry.Create([SectionSchema.Create("a-1" + new string('b', 29), "Module", 1)]);
    }

    [Fact]
    public void AnOwnerModuleOutside1To64CharactersOrAVersionBelow1IsInvalid()
    {
        AssertInvalid(SectionSchema.Create("a", "", 1));
        AssertInvalid(SectionSchema.Create("a", new string('m', 65), 1));
        AssertInvalid(SectionSchema.Create("a", "Module", 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Timeout")]
    [InlineData("1timeout")]
    [InlineData("time_out")]
    [InlineData("time-out")]
    public void AMalformedFieldNameIsInvalid(string name)
    {
        Assert.Equal("persistence." + name, AssertInvalid(Section(Field(name, ConfigurationFieldKind.Text)))["field"]);
    }

    [Fact]
    public void AFieldNameOf65CharactersIsInvalid()
    {
        AssertInvalid(Section(Field("a" + new string('b', 64), ConfigurationFieldKind.Text)));
    }

    [Fact]
    public void DuplicateOrCaseCollidingSiblingNamesAreInvalid()
    {
        AssertInvalid(Section(Field("host", ConfigurationFieldKind.Text), Field("host", ConfigurationFieldKind.Text)));
        AssertInvalid(Section(Field("hostName", ConfigurationFieldKind.Text), Field("hostname", ConfigurationFieldKind.Text)));
        AssertInvalid(Section(Field("pool", ConfigurationFieldKind.Section).WithFields(Field("aB", ConfigurationFieldKind.Text), Field("ab", ConfigurationFieldKind.Text))));
    }

    [Fact]
    public void TheSameNameUnderDifferentParentsIsValid()
    {
        ConfigurationSchemaRegistry.Create([Section(Field("host", ConfigurationFieldKind.Text), Field("pool", ConfigurationFieldKind.Section).WithFields(Field("host", ConfigurationFieldKind.Text)))]);
    }

    [Theory]
    [InlineData("secretRef")]
    [InlineData("secretref")]
    [InlineData("secretREF")]
    public void AFieldNamedSecretRefInAnyCaseIsInvalid(string name)
    {
        AssertInvalid(Section(Field(name, ConfigurationFieldKind.Text)));
    }

    [Fact]
    public void ARequiredFieldWithADefaultIsInvalid()
    {
        AssertInvalid(Section(Field("port", ConfigurationFieldKind.WholeNumber).Required().WithDefault(5432)));
    }

    [Fact]
    public void AFieldBothRequiredAndDevelopmentOnlyIsInvalid()
    {
        AssertInvalid(Section(Field("port", ConfigurationFieldKind.WholeNumber).Required().DevelopmentOnly()));
    }

    [Fact]
    public void ARequiredObjectFieldIsInvalid()
    {
        AssertInvalid(Section(Field("pool", ConfigurationFieldKind.Section).Required()));
    }

    [Fact]
    public void ADefaultOnAnObjectArrayOrSecretRefFieldIsInvalid()
    {
        AssertInvalid(Section(Field("pool", ConfigurationFieldKind.Section).WithDefault("x")));
        AssertInvalid(Section(Field("hosts", ConfigurationFieldKind.Array).WithItems(Field("item", ConfigurationFieldKind.Text)).WithDefault("x")));
        AssertInvalid(Section(Field("apiKey", ConfigurationFieldKind.SecretReference).WithDefault("x")));
    }

    [Fact]
    public void ADevelopmentOnlySecretRefIsInvalid()
    {
        AssertInvalid(Section(Field("apiKey", ConfigurationFieldKind.SecretReference).DevelopmentOnly()));
    }

    [Fact]
    public void AnArrayOfArraysOrAnArrayWithNoItemTypeIsInvalid()
    {
        AssertInvalid(Section(Field("grid", ConfigurationFieldKind.Array).WithItems(Field("row", ConfigurationFieldKind.Array).WithItems(Field("cell", ConfigurationFieldKind.Text)))));
        AssertInvalid(Section(Field("grid", ConfigurationFieldKind.Array)));
    }

    [Fact]
    public void AnArrayItemThatIsRequiredDevelopmentOnlyOrHasADefaultIsInvalid()
    {
        AssertInvalid(Section(Field("hosts", ConfigurationFieldKind.Array).WithItems(Field("item", ConfigurationFieldKind.Text).Required())));
        AssertInvalid(Section(Field("hosts", ConfigurationFieldKind.Array).WithItems(Field("item", ConfigurationFieldKind.Text).WithDefault("x"))));
        AssertInvalid(Section(Field("hosts", ConfigurationFieldKind.Array).WithItems(Field("item", ConfigurationFieldKind.Text).DevelopmentOnly())));
    }

    [Fact]
    public void FieldsOrItemsOnTheWrongTypeAreInvalid()
    {
        AssertInvalid(Section(Field("host", ConfigurationFieldKind.Text).WithFields(Field("a", ConfigurationFieldKind.Text))));
        AssertInvalid(Section(Field("host", ConfigurationFieldKind.Text).WithItems(Field("a", ConfigurationFieldKind.Text))));
    }

    [Fact]
    public void AnUndefinedFieldTypeIsInvalid()
    {
        AssertInvalid(Section(Field("host", (ConfigurationFieldKind)42)));
    }

    [Fact]
    public void AMinimumAboveItsMaximumIsInvalid()
    {
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.WholeNumber, c => c.WithIntegerRange(10, 1))));
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Number, c => c.WithNumberRange(2m, 1m))));
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Duration, c => c.WithDurationRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)))));
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Text, c => c.WithLength(5, 4))));
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Array, c => c.WithItemCount(3, 2)).WithItems(Field("item", ConfigurationFieldKind.Text))));
    }

    [Fact]
    public void AnEmptyOrRepeatingEnumerationIsInvalid()
    {
        AssertInvalid(Section(Field("mode", ConfigurationFieldKind.Enumeration)));
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Enumeration, c => c.WithMembers("a", "a"))));
    }

    [Fact]
    public void EnumerationMembersAreCaseSensitiveSoDifferingCaseIsValid()
    {
        ConfigurationSchemaRegistry.Create([Section(WithConstraints(ConfigurationFieldKind.Enumeration, c => c.WithMembers("Fast", "fast")))]);
    }

    [Fact]
    public void AStringMaximumLengthAbove65536IsInvalid()
    {
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Text, c => c.WithLength(null, 65_537))));
        ConfigurationSchemaRegistry.Create([Section(WithConstraints(ConfigurationFieldKind.Text, c => c.WithLength(null, 65_536)))]);
    }

    [Fact]
    public void ANegativeMinimumLengthOrItemCountIsInvalid()
    {
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Text, c => c.WithLength(-1, null))));
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Array, c => c.WithItemCount(-1, null)).WithItems(Field("item", ConfigurationFieldKind.Text))));
    }

    public static TheoryData<ConfigurationFieldKind, Func<FieldConstraints, FieldConstraints>> MisappliedConstraints => new()
    {
        { ConfigurationFieldKind.Text, c => c.WithIntegerRange(1, 2) },
        { ConfigurationFieldKind.WholeNumber, c => c.WithNumberRange(1m, 2m) },
        { ConfigurationFieldKind.Number, c => c.WithDurationRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)) },
        { ConfigurationFieldKind.Enumeration, c => c.WithLength(1, 2).WithMembers("a") },
        { ConfigurationFieldKind.Uri, c => c.WithAllowedCharacters(new CharacterClassPattern(CharacterClasses.Digits)) },
        { ConfigurationFieldKind.Text, c => c.WithMembers("a") },
        { ConfigurationFieldKind.Boolean, c => c.WithItemCount(1, 2) },
        { ConfigurationFieldKind.Text, c => c.WithAllowedSchemes("https") },
        { ConfigurationFieldKind.SecretReference, c => c.WithLength(1, 2) },
        { ConfigurationFieldKind.Section, c => c.WithIntegerRange(1, 2) },
    };

    [Theory]
    [MemberData(nameof(MisappliedConstraints))]
    public void AConstraintThatDoesNotApplyToTheTypeIsInvalid(ConfigurationFieldKind kind, Func<FieldConstraints, FieldConstraints> build)
    {
        AssertInvalid(Section(WithConstraints(kind, build)));
    }

    public static TheoryData<FieldSchema> DefaultsBreakingTheirConstraints => new()
    {
        Field("port", ConfigurationFieldKind.WholeNumber).WithConstraints(FieldConstraints.None.WithIntegerRange(1, 300)).WithDefault(301),
        Field("ratio", ConfigurationFieldKind.Number).WithConstraints(FieldConstraints.None.WithNumberRange(0m, 1m)).WithDefault(1.5m),
        Field("wait", ConfigurationFieldKind.Duration).WithConstraints(FieldConstraints.None.WithDurationRange(TimeSpan.Zero, TimeSpan.FromSeconds(1))).WithDefault(TimeSpan.FromSeconds(2)),
        Field("wait", ConfigurationFieldKind.Duration).WithDefault(TimeSpan.FromTicks(1)),
        Field("wait", ConfigurationFieldKind.Duration).WithDefault(TimeSpan.FromSeconds(-1)),
        Field("name", ConfigurationFieldKind.Text).WithConstraints(FieldConstraints.None.WithLength(1, 3)).WithDefault("abcd"),
        Field("name", ConfigurationFieldKind.Text).WithConstraints(FieldConstraints.None.WithAllowedCharacters(new CharacterClassPattern(CharacterClasses.Digits))).WithDefault("abc"),
        Field("name", ConfigurationFieldKind.Text).WithDefault("a\u0000b"),
        Field("mode", ConfigurationFieldKind.Enumeration).WithConstraints(FieldConstraints.None.WithMembers("a", "b")).WithDefault("c"),
        Field("endpoint", ConfigurationFieldKind.Uri).WithConstraints(FieldConstraints.None.WithAllowedSchemes("https")).WithDefault(new Uri("http://example.test/")),
        Field("endpoint", ConfigurationFieldKind.Uri).WithDefault(new Uri("relative", UriKind.Relative)),
        Field("port", ConfigurationFieldKind.WholeNumber).WithDefault("5432"),
        Field("enabled", ConfigurationFieldKind.Boolean).WithDefault(1),
        Field("ratio", ConfigurationFieldKind.Number).WithDefault(1),
    };

    [Theory]
    [MemberData(nameof(DefaultsBreakingTheirConstraints))]
    public void ADefaultThatBreaksItsOwnConstraintsOrTypeIsInvalid(FieldSchema field)
    {
        AssertInvalid(Section(field));
    }

    [Fact]
    public void AMalformedPatternIsInvalid()
    {
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Text, c => c.WithAllowedCharacters(new CharacterClassPattern(CharacterClasses.None)))));
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Text, c => c.WithAllowedCharacters(new CharacterClassPattern(CharacterClasses.Digits, "a")))));
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Text, c => c.WithAllowedCharacters(new CharacterClassPattern(CharacterClasses.Digits, "--")))));
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Text, c => c.WithAllowedCharacters(new CharacterClassPattern(CharacterClasses.Digits, " ")))));
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Text, c => c.WithAllowedCharacters(new CharacterClassPattern(CharacterClasses.Digits, null, CharacterClasses.None)))));
    }

    [Fact]
    public void AnInvalidSchemeIsInvalid()
    {
        AssertInvalid(Section(WithConstraints(ConfigurationFieldKind.Uri, c => c.WithAllowedSchemes("ht tp"))));
    }

    [Fact]
    public void ANestedFaultNamesItsFullPath()
    {
        var values = AssertInvalid(Section(Field("pools", ConfigurationFieldKind.Array).WithItems(
            Field("pool", ConfigurationFieldKind.Section).WithFields(Field("Host", ConfigurationFieldKind.Text)))));

        Assert.Equal("persistence.pools[].Host", values["field"]);
    }
}
