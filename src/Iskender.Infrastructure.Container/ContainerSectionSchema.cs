using Iskender.Core.Contracts.Configuration;

namespace Iskender.Infrastructure.Container;

/// <summary>
/// The ISK-0003 section <c>iskender.container</c>: exactly the container contract's table of the <c>container</c> section.
/// No field is development-only. Owned by ISK-0007.
/// </summary>
public static class ContainerSectionSchema
{
    public const string Namespace = "container";

    /// <summary>The registered schema, the only instance that can read the section from a snapshot.</summary>
    public static SectionSchema Schema { get; } = SectionSchema.Create(Namespace, "Iskender.Infrastructure.Container", 1).WithFields(
        Port("trafficPort").WithDefault(8080L),
        Port("healthPort").WithDefault(8081L),
        Duration("shutdownDelay", TimeSpan.Zero, TimeSpan.FromMinutes(1)).WithDefault(TimeSpan.Zero),
        Duration("shutdownTimeout", TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5)).WithDefault(TimeSpan.FromSeconds(30)),
        Duration("readinessCacheDuration", TimeSpan.Zero, TimeSpan.FromSeconds(30)).WithDefault(TimeSpan.FromSeconds(2)));

    private static FieldSchema Port(string name) =>
        FieldSchema.Create(name, ConfigurationFieldKind.WholeNumber).WithConstraints(FieldConstraints.None.WithIntegerRange(1024, 65535));

    private static FieldSchema Duration(string name, TimeSpan minimum, TimeSpan maximum) =>
        FieldSchema.Create(name, ConfigurationFieldKind.Duration).WithConstraints(FieldConstraints.None.WithDurationRange(minimum, maximum));
}
