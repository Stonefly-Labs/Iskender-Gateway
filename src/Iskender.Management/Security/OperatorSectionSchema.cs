using Iskender.Core.Contracts.Configuration;

namespace Iskender.Management.Security;

/// <summary>
/// The ISK-0003 section <c>iskender.operator</c>, version 1 (operator-security contract, Configuration, research R16):
/// exactly the three fields, defaults, and bounds of the contract, none development-only. Only
/// <c>AddIskenderOperatorSecurity()</c> registers it, so a data-plane or worker document holding it is rejected with
/// <c>config.namespace.unknown</c>. Owned by ISK-0010.
/// </summary>
public static class OperatorSectionSchema
{
    public const string Namespace = "operator";

    /// <summary>The registered schema, the only instance that can read the section from a snapshot.</summary>
    public static SectionSchema Schema { get; } = SectionSchema.Create(Namespace, "Iskender.Management", 1).WithFields(
        FieldSchema.Create("bootstrap", ConfigurationFieldKind.Section).WithFields(
            Duration("tokenLifetime", TimeSpan.FromMinutes(5), TimeSpan.FromHours(24)).WithDefault(TimeSpan.FromHours(1)),
            FieldSchema.Create("attemptsPerMinute", ConfigurationFieldKind.WholeNumber)
                .WithConstraints(FieldConstraints.None.WithIntegerRange(1, 600)).WithDefault(10L)),
        FieldSchema.Create("enrollment", ConfigurationFieldKind.Section).WithFields(
            Duration("codeLifetime", TimeSpan.FromHours(1), TimeSpan.FromDays(7)).WithDefault(TimeSpan.FromHours(24))));

    private static FieldSchema Duration(string name, TimeSpan minimum, TimeSpan maximum) =>
        FieldSchema.Create(name, ConfigurationFieldKind.Duration).WithConstraints(FieldConstraints.None.WithDurationRange(minimum, maximum));
}
