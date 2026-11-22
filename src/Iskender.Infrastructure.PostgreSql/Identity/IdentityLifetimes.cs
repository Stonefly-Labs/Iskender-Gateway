using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Identity;

/// <summary>
/// The token lifetimes the identity stores use (operator-security contract, Tokens rule 3): bootstrap and recovery tokens
/// last <c>operator.bootstrap.tokenLifetime</c>, enrollment codes <c>operator.enrollment.codeLifetime</c>. The adapter
/// never references <c>Iskender.Management</c>, so it finds the <c>operator</c> section by its namespace among the
/// composed modules' sections and reads it with that registered schema. The section exists only where
/// <c>AddIskenderOperatorSecurity()</c> registered it. Owned by ISK-0010.
/// </summary>
internal sealed record IdentityLifetimes(TimeSpan TokenLifetime, TimeSpan EnrollmentCodeLifetime)
{
    /// <summary>The section's namespace.</summary>
    public const string Namespace = "operator";

    /// <summary>
    /// Reads the lifetimes from <paramref name="snapshot"/>, through the <c>operator</c> schema one of
    /// <paramref name="modules"/> registered. With none, it fails with <c>hosting.registration_missing</c>.
    /// </summary>
    public static IdentityLifetimes From(ConfigurationSnapshot snapshot, IEnumerable<CompositionModule> modules)
    {
        if (snapshot is null || modules is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var schema = modules.SelectMany(module => module.Sections).FirstOrDefault(section => string.Equals(section.Namespace, Namespace, StringComparison.Ordinal))
            ?? throw new GatewayException(ErrorCodes.HostingRegistrationMissing);
        var section = snapshot.GetSection(schema);
        return new(
            section.GetSection("bootstrap").GetDuration("tokenLifetime")!.Value,
            section.GetSection("enrollment").GetDuration("codeLifetime")!.Value);
    }
}
