using Iskender.Core.Contracts.Errors;
using Iskender.Core.Contracts.Versioning;

namespace Iskender.Core.Contracts.Identifiers;

/// <summary>
/// Who owns a resource: an organization, a project in an organization, or the operator. The kinds never compare equal
/// and cannot be converted into one another (FR-010). A scope built from values is inert until a resolver attests it.
/// </summary>
[ContractVersion("core-contracts", 1)]
public abstract class OwnerScope : IEquatable<OwnerScope>
{
    private protected OwnerScope()
    {
    }

    public static bool operator ==(OwnerScope? left, OwnerScope? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(OwnerScope? left, OwnerScope? right) => !(left == right);

    public abstract bool Equals(OwnerScope? other);

    public sealed override bool Equals(object? obj) => Equals(obj as OwnerScope);

    public abstract override int GetHashCode();

    private protected static ResourceId Require(ResourceId? id, ResourceType type)
    {
        if (id is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        if (!ReferenceEquals(id.Type, type))
        {
            throw new GatewayException(ErrorCodes.ResourceIdWrongType);
        }

        return id;
    }
}

/// <summary>A resource owned by an organization.</summary>
[ContractVersion("core-contracts", 1)]
public sealed class OrganizationScope : OwnerScope
{
    public OrganizationScope(ResourceId organization)
    {
        Organization = Require(organization, ResourceTypes.Organization);
    }

    /// <summary>The organization's ID.</summary>
    public ResourceId Organization { get; }

    public override bool Equals(OwnerScope? other) => other is OrganizationScope scope && Organization.Equals(scope.Organization);

    public override int GetHashCode() => HashCode.Combine(nameof(OrganizationScope), Organization);
}

/// <summary>A resource owned by a project in an organization.</summary>
[ContractVersion("core-contracts", 1)]
public sealed class ProjectScope : OwnerScope
{
    public ProjectScope(ResourceId organization, ResourceId project)
    {
        Organization = Require(organization, ResourceTypes.Organization);
        Project = Require(project, ResourceTypes.Project);
    }

    /// <summary>The owning organization's ID.</summary>
    public ResourceId Organization { get; }

    /// <summary>The project's ID.</summary>
    public ResourceId Project { get; }

    public override bool Equals(OwnerScope? other) =>
        other is ProjectScope scope && Organization.Equals(scope.Organization) && Project.Equals(scope.Project);

    public override int GetHashCode() => HashCode.Combine(nameof(ProjectScope), Organization, Project);
}

/// <summary>The operator, which owns gateway-wide resources. There is one instance.</summary>
[ContractVersion("core-contracts", 1)]
public sealed class OperatorScope : OwnerScope
{
    private OperatorScope()
    {
    }

    /// <summary>The only operator scope.</summary>
    public static OperatorScope Instance { get; } = new();

    public override bool Equals(OwnerScope? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => nameof(OperatorScope).GetHashCode(StringComparison.Ordinal);
}
