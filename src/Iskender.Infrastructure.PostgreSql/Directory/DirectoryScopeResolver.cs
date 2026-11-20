using System.Diagnostics.CodeAnalysis;
using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Directory;

/// <summary>
/// The one framework-internal attestation that turns a scope directory row's <c>listed_scope</c> into a unit of work
/// scope (ISK-0009 TR-5, research R4), for the dispatcher and the jobs only, and the only code that reads that text. It
/// accepts exactly <c>operator</c>, <c>org:&lt;organization id&gt;</c>, or <c>project:&lt;organization id&gt;/&lt;project
/// id&gt;</c>, each ID a <see cref="ResourceId"/> of the right type whose canonical text is the input's. Anything else is
/// malformed and yields no scope, never a wider one. Rule H6 of the public contracts and versioning contract, version 2,
/// allows it to derive from <see cref="OwnershipResolver"/>. Owned by ISK-0009.
/// </summary>
internal sealed class DirectoryScopeResolver : OwnershipResolver
{
    private const string OperatorText = "operator";
    private const string OrganizationPrefix = "org:";
    private const string ProjectPrefix = "project:";

    private DirectoryScopeResolver()
    {
    }

    /// <summary>The scope <paramref name="listed"/> names, or <c>false</c> with no scope when it is malformed.</summary>
    public static bool TryResolve(string? listed, [NotNullWhen(true)] out UnitOfWorkScope? scope)
    {
        scope = null;
        if (listed is null)
        {
            return false;
        }

        if (listed == OperatorText)
        {
            scope = UnitOfWorkScope.Operator;
            return true;
        }

        if (listed.StartsWith(OrganizationPrefix, StringComparison.Ordinal))
        {
            if (!TryCanonical(listed[OrganizationPrefix.Length..], ResourceTypes.Organization, out var organization))
            {
                return false;
            }

            scope = UnitOfWorkScope.ForResource(Attest(organization, new OrganizationScope(organization)));
            return true;
        }

        if (listed.StartsWith(ProjectPrefix, StringComparison.Ordinal))
        {
            var body = listed[ProjectPrefix.Length..];
            var separator = body.IndexOf('/', StringComparison.Ordinal);
            if (separator < 0
                || !TryCanonical(body[..separator], ResourceTypes.Organization, out var organization)
                || !TryCanonical(body[(separator + 1)..], ResourceTypes.Project, out var project))
            {
                return false;
            }

            scope = UnitOfWorkScope.ForResource(Attest(project, new ProjectScope(organization, project)));
            return true;
        }

        return false;
    }

    private static bool TryCanonical(string text, ResourceType type, [NotNullWhen(true)] out ResourceId? id)
    {
        if (ResourceId.TryParse(text, type, out id, out _) && id.Value == text)
        {
            return true;
        }

        id = null;
        return false;
    }
}
