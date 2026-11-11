using Iskender.Core.Contracts.Identifiers;
using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Tests.Modules;

/// <summary>The owner IDs a fixture repository writes and filters by, read from the unit's own scope, never from a caller.</summary>
internal static class ScopeOwner
{
    /// <summary>The scope's organization ID, or <c>null</c> in the operator scope.</summary>
    public static string? Organization(IUnitOfWork unit) => unit.Scope.Owner switch
    {
        OrganizationScope organization => organization.Organization.Value,
        ProjectScope project => project.Organization.Value,
        _ => null,
    };

    /// <summary>The scope's project ID, or <c>null</c> outside a project scope.</summary>
    public static string? Project(IUnitOfWork unit) => unit.Scope.Owner is ProjectScope project ? project.Project.Value : null;
}
