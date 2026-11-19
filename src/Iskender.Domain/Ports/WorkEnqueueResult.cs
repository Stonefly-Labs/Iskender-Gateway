namespace Iskender.Domain.Ports;

/// <summary>
/// The outcome of an enqueue. <see cref="Created"/> is <c>false</c> when an item with the same scope, kind, and key was
/// still retained, and <see cref="ItemId"/> is then that item's (work contract, The outbox rule 3). Its text shows only
/// <see cref="Created"/>, never the item ID (FR-034). Owned by ISK-0009.
/// </summary>
public readonly record struct WorkEnqueueResult(Guid ItemId, bool Created)
{
    public override string ToString() => $"WorkEnqueueResult {{ Created = {Created} }}";
}
