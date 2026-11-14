using Iskender.Core.Contracts.Configuration;

namespace Iskender.Hosting.Configuration;

/// <summary>
/// The ISK-0003 section <c>iskender.hosting</c> (hosting contract, Configuration; research R13): exactly one field,
/// <c>requestTimeout</c>, with a default, so a configuration with no section is valid. Owned by ISK-0006.
/// </summary>
internal static class HostingSectionSchema
{
    public const string Namespace = "hosting";

    /// <summary>The registered schema, the only instance that can read the section from a snapshot.</summary>
    public static SectionSchema Schema { get; } = SectionSchema.Create(Namespace, "Iskender.Hosting", 1).WithFields(
        FieldSchema.Create("requestTimeout", ConfigurationFieldKind.Duration)
            .WithConstraints(FieldConstraints.None.WithDurationRange(TimeSpan.FromSeconds(1), TimeSpan.FromHours(1)))
            .WithDefault(TimeSpan.FromSeconds(100)));
}
