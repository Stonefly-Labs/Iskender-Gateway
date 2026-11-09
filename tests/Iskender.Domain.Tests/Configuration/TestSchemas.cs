using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>
/// The schemas the fixtures validate against: <c>alpha</c> and <c>beta</c> for isolation, and <c>persistence</c>,
/// which declares every field type, every constraint, a required field, defaults, a nested object, an array of
/// objects, an array of scalars, a <c>secretRef</c> field, and a development-only field (User Story 1).
/// </summary>
internal static class TestSchemas
{
    public static readonly SectionSchema Alpha = SectionSchema.Create("alpha", "Tests.Alpha", 1).WithFields(
        FieldSchema.Create("name", ConfigurationFieldKind.Text).WithDefault("alpha"),
        FieldSchema.Create("count", ConfigurationFieldKind.WholeNumber));

    public static readonly SectionSchema Beta = SectionSchema.Create("beta", "Tests.Beta", 1).WithFields(
        FieldSchema.Create("name", ConfigurationFieldKind.Text),
        FieldSchema.Create("enabled", ConfigurationFieldKind.Boolean).WithDefault(false));

    public static readonly SectionSchema Persistence = SectionSchema.Create("persistence", "Tests.Persistence", 1).WithFields(
        FieldSchema.Create("host", ConfigurationFieldKind.Text).Required()
            .WithConstraints(FieldConstraints.None.WithLength(1, 253).WithAllowedCharacters(
                new CharacterClassPattern(CharacterClasses.LowerCaseLetters | CharacterClasses.Digits, ".-", CharacterClasses.LowerCaseLetters | CharacterClasses.Digits))),
        FieldSchema.Create("connectionTimeout", ConfigurationFieldKind.WholeNumber)
            .WithConstraints(FieldConstraints.None.WithIntegerRange(1, 300)).WithDefault(30),
        FieldSchema.Create("sampleRatio", ConfigurationFieldKind.Number)
            .WithConstraints(FieldConstraints.None.WithNumberRange(0m, 1m)).WithDefault(0.5m),
        FieldSchema.Create("readOnly", ConfigurationFieldKind.Boolean).WithDefault(false),
        FieldSchema.Create("mode", ConfigurationFieldKind.Enumeration)
            .WithConstraints(FieldConstraints.None.WithMembers("primary", "replica")).WithDefault("primary"),
        FieldSchema.Create("idleTimeout", ConfigurationFieldKind.Duration)
            .WithConstraints(FieldConstraints.None.WithDurationRange(TimeSpan.FromSeconds(1), TimeSpan.FromDays(1))).WithDefault(TimeSpan.FromMinutes(5)),
        FieldSchema.Create("endpoint", ConfigurationFieldKind.Uri)
            .WithConstraints(FieldConstraints.None.WithAllowedSchemes("https", "postgres")),
        FieldSchema.Create("password", ConfigurationFieldKind.SecretReference),
        FieldSchema.Create("trace", ConfigurationFieldKind.Boolean).WithDefault(false).DevelopmentOnly(),
        FieldSchema.Create("pool", ConfigurationFieldKind.Section).WithFields(
            FieldSchema.Create("size", ConfigurationFieldKind.WholeNumber).WithConstraints(FieldConstraints.None.WithIntegerRange(1, 100)).WithDefault(10),
            FieldSchema.Create("name", ConfigurationFieldKind.Text).Required()),
        FieldSchema.Create("replicas", ConfigurationFieldKind.Array)
            .WithConstraints(FieldConstraints.None.WithItemCount(0, 4))
            .WithItems(FieldSchema.Create("replica", ConfigurationFieldKind.Section).WithFields(
                FieldSchema.Create("host", ConfigurationFieldKind.Text).Required(),
                FieldSchema.Create("weight", ConfigurationFieldKind.WholeNumber).WithDefault(1))),
        FieldSchema.Create("tags", ConfigurationFieldKind.Array)
            .WithItems(FieldSchema.Create("tag", ConfigurationFieldKind.Text).WithConstraints(FieldConstraints.None.WithLength(1, 32))));

    public static ConfigurationSchemaRegistry Registry() => ConfigurationSchemaRegistry.Create([Alpha, Beta, Persistence]);
}
