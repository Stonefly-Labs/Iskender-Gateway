using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// The level a coordinator declares for each primitive, or none when it does not offer it (contract Consistency rule 2).
/// An implementation declares what it can show, and when in doubt the weaker level. Owned by ISK-0005.
/// </summary>
public sealed record CoordinatorCapabilities
{
    private readonly Dictionary<CoordinationPrimitive, ConsistencyLevel> _levels;

    /// <summary>An undefined kind, primitive, or level fails with <c>core.value.invalid</c>.</summary>
    public CoordinatorCapabilities(CoordinatorKind kind, IReadOnlyDictionary<CoordinationPrimitive, ConsistencyLevel> levels)
    {
        if (!Enum.IsDefined(kind) || levels is null || levels.Any(pair => !Enum.IsDefined(pair.Key) || !Enum.IsDefined(pair.Value)))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Kind = kind;
        _levels = new(levels);
    }

    /// <summary>
    /// The local substitutes: <c>local</c> for every primitive except fencing, which they do not offer, because the
    /// fencing guard accepts only PostgreSQL-issued leases (contract Leases rule 6).
    /// </summary>
    public static CoordinatorCapabilities Local { get; } = new(CoordinatorKind.Local, new Dictionary<CoordinationPrimitive, ConsistencyLevel>
    {
        [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Local,
        [CoordinationPrimitive.Leases] = ConsistencyLevel.Local,
        [CoordinationPrimitive.CacheInvalidation] = ConsistencyLevel.Local,
    });

    /// <summary>
    /// The Redis adapter (research R9): <c>leased</c> for every primitive except fencing, which it does not offer, because
    /// the fencing guard accepts only PostgreSQL-issued leases (contract Leases rule 6).
    /// </summary>
    public static CoordinatorCapabilities Redis { get; } = new(CoordinatorKind.Redis, new Dictionary<CoordinationPrimitive, ConsistencyLevel>
    {
        [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Leased,
        [CoordinationPrimitive.Leases] = ConsistencyLevel.Leased,
        [CoordinationPrimitive.CacheInvalidation] = ConsistencyLevel.Leased,
    });

    /// <summary>The PostgreSQL coordinator (research R8): <c>strict</c> for every primitive.</summary>
    public static CoordinatorCapabilities PostgreSql { get; } = new(CoordinatorKind.PostgreSql, new Dictionary<CoordinationPrimitive, ConsistencyLevel>
    {
        [CoordinationPrimitive.AtomicOperations] = ConsistencyLevel.Strict,
        [CoordinationPrimitive.Leases] = ConsistencyLevel.Strict,
        [CoordinationPrimitive.Fencing] = ConsistencyLevel.Strict,
        [CoordinationPrimitive.CacheInvalidation] = ConsistencyLevel.Strict,
    });

    public CoordinatorKind Kind { get; }

    /// <summary>The declared level for <paramref name="primitive"/>, or <c>null</c> when it is not offered.</summary>
    public ConsistencyLevel? LevelOf(CoordinationPrimitive primitive) =>
        _levels.TryGetValue(primitive, out var level) ? level : null;

    public bool Equals(CoordinatorCapabilities? other) =>
        other is not null && Kind == other.Kind && CoordinationPrimitives.All.All(primitive => LevelOf(primitive) == other.LevelOf(primitive));

    public override int GetHashCode() =>
        HashCode.Combine(Kind, LevelOf(CoordinationPrimitive.AtomicOperations), LevelOf(CoordinationPrimitive.Leases), LevelOf(CoordinationPrimitive.Fencing), LevelOf(CoordinationPrimitive.CacheInvalidation));
}
