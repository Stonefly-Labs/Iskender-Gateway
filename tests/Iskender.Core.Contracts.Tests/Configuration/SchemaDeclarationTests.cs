using System.Reflection;
using Iskender.Core.Contracts.Configuration;

namespace Iskender.Core.Contracts.Tests.Configuration;

public class SchemaDeclarationTests
{
    [Fact]
    public void FieldBuilderReturnsNewInstancesAndLeavesTheOriginalUnchanged()
    {
        var field = FieldSchema.Create("connectionTimeout", ConfigurationFieldKind.WholeNumber);
        var required = field.Required();
        var ranged = required.WithConstraints(FieldConstraints.None.WithIntegerRange(1, 300));

        Assert.NotSame(field, required);
        Assert.False(field.IsRequired);
        Assert.True(required.IsRequired);
        Assert.Null(required.Constraints.MinimumInteger);
        Assert.Equal(1, ranged.Constraints.MinimumInteger);
        Assert.Equal(300, ranged.Constraints.MaximumInteger);
        Assert.True(ranged.IsRequired);
    }

    [Fact]
    public void FieldRecordsEveryDeclaration()
    {
        var field = FieldSchema.Create("mode", ConfigurationFieldKind.Enumeration)
            .WithConstraints(FieldConstraints.None.WithMembers("fast", "safe"))
            .WithDefault("safe")
            .DevelopmentOnly();

        Assert.Equal("mode", field.Name);
        Assert.Equal(ConfigurationFieldKind.Enumeration, field.Kind);
        Assert.Equal("safe", field.Default);
        Assert.True(field.IsDevelopmentOnly);
        Assert.Equal(["fast", "safe"], field.Constraints.Members);
        Assert.Empty(field.Fields);
        Assert.Null(field.Items);
    }

    [Fact]
    public void DefaultsKeepTheirTypes()
    {
        Assert.IsType<long>(FieldSchema.Create("a", ConfigurationFieldKind.WholeNumber).WithDefault(30).Default);
        Assert.IsType<decimal>(FieldSchema.Create("a", ConfigurationFieldKind.Number).WithDefault(1.5m).Default);
        Assert.IsType<bool>(FieldSchema.Create("a", ConfigurationFieldKind.Boolean).WithDefault(true).Default);
        Assert.IsType<TimeSpan>(FieldSchema.Create("a", ConfigurationFieldKind.Duration).WithDefault(TimeSpan.FromSeconds(30)).Default);
        Assert.IsType<Uri>(FieldSchema.Create("a", ConfigurationFieldKind.Uri).WithDefault(new Uri("https://example.test/")).Default);
        Assert.IsType<string>(FieldSchema.Create("a", ConfigurationFieldKind.Text).WithDefault("x").Default);
    }

    [Fact]
    public void SectionAndArrayFieldsHoldTheirChildren()
    {
        var host = FieldSchema.Create("host", ConfigurationFieldKind.Text);
        var pool = FieldSchema.Create("pool", ConfigurationFieldKind.Section).WithFields(host);
        var pools = FieldSchema.Create("pools", ConfigurationFieldKind.Array).WithItems(pool)
            .WithConstraints(FieldConstraints.None.WithItemCount(1, 4));

        Assert.Same(host, Assert.Single(pool.Fields));
        Assert.Same(pool, pools.Items);
        Assert.Equal(4, pools.Constraints.MaximumItems);
        Assert.Equal(2, pool.WithFields(host).Fields.Count);
        Assert.Single(pool.Fields);
    }

    [Fact]
    public void ConstraintsBuilderReturnsNewInstancesAndNoneStaysEmpty()
    {
        var pattern = new CharacterClassPattern(CharacterClasses.LowerCaseLetters | CharacterClasses.Digits, "-");
        var constraints = FieldConstraints.None
            .WithNumberRange(0.5m, 2m)
            .WithDurationRange(TimeSpan.Zero, TimeSpan.FromHours(1))
            .WithLength(1, 64)
            .WithAllowedCharacters(pattern)
            .WithAllowedSchemes("https", "http");

        Assert.Equal(0.5m, constraints.MinimumNumber);
        Assert.Equal(TimeSpan.FromHours(1), constraints.MaximumDuration);
        Assert.Equal(64, constraints.MaximumLength);
        Assert.Same(pattern, constraints.AllowedCharacters);
        Assert.Equal(["https", "http"], constraints.AllowedSchemes);
        Assert.Null(FieldConstraints.None.MinimumNumber);
        Assert.Empty(FieldConstraints.None.AllowedSchemes);
        Assert.Empty(FieldConstraints.None.Members);
    }

    [Fact]
    public void SectionBuilderAppendsFieldsToANewInstance()
    {
        var schema = SectionSchema.Create("persistence", "Iskender.Domain.Persistence", 1);
        var withField = schema.WithFields(FieldSchema.Create("host", ConfigurationFieldKind.Text));

        Assert.Equal("persistence", withField.Namespace);
        Assert.Equal("Iskender.Domain.Persistence", withField.OwnerModule);
        Assert.Equal(1, withField.Version);
        Assert.Empty(schema.Fields);
        Assert.Single(withField.Fields);
    }

    [Fact]
    public void NothingIsCheckedUntilTheRegistryIsBuilt()
    {
        var field = FieldSchema.Create("Bad Name", ConfigurationFieldKind.Section).Required().WithDefault(1);
        var schema = SectionSchema.Create("", "", 0).WithFields(field, field);

        Assert.Equal(2, schema.Fields.Count);
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("a-1", true)]
    [InlineData("", true)]
    [InlineData("1ab", false)]
    [InlineData("aB", false)]
    [InlineData("a_b", false)]
    [InlineData("é", false)]
    public void PatternMatchesByClassPunctuationAndFirstCharacter(string value, bool matches)
    {
        var pattern = new CharacterClassPattern(CharacterClasses.LowerCaseLetters | CharacterClasses.Digits, "-", CharacterClasses.LowerCaseLetters);

        Assert.Equal(matches, pattern.IsMatch(value));
    }

    [Fact]
    public void PatternDescribesItselfWithoutAnyValue()
    {
        Assert.Equal("a-z, 0-9, and \"-\", starting with a-z",
            new CharacterClassPattern(CharacterClasses.LowerCaseLetters | CharacterClasses.Digits, "-", CharacterClasses.LowerCaseLetters).ToString());
        Assert.Equal("a-z, A-Z, and 0-9", new CharacterClassPattern(CharacterClasses.LowerCaseLetters | CharacterClasses.UpperCaseLetters | CharacterClasses.Digits).ToString());
        Assert.Equal("0-9", new CharacterClassPattern(CharacterClasses.Digits).ToString());
    }

    [Theory]
    [InlineData(typeof(FieldSchema))]
    [InlineData(typeof(SectionSchema))]
    [InlineData(typeof(FieldConstraints))]
    [InlineData(typeof(CharacterClassPattern))]
    public void DeclarationsAreSealedWithNoPublicSetterOrConstructorAndExposeOnlyReadOnlyLists(Type type)
    {
        Assert.True(type.IsSealed);
        Assert.All(type.GetProperties(), property =>
        {
            Assert.True(property.SetMethod is null || !property.SetMethod.IsPublic);
            if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() != typeof(Nullable<>))
            {
                Assert.Equal(typeof(IReadOnlyList<>), property.PropertyType.GetGenericTypeDefinition());
            }
        });

        if (type != typeof(CharacterClassPattern))
        {
            Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }
    }

    [Fact]
    public void ListsCannotBeChangedThroughACast()
    {
        var schema = SectionSchema.Create("a", "m", 1).WithFields(FieldSchema.Create("b", ConfigurationFieldKind.Text));

        var list = Assert.IsAssignableFrom<IList<FieldSchema>>(schema.Fields);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(FieldSchema.Create("c", ConfigurationFieldKind.Text)));
    }
}
