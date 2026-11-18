namespace Iskender.Domain.Ports;

/// <summary>
/// The outcome of an enqueue. <see cref="Created"/> is <c>false</c> when an item with the same scope, kind, and key was
/// still retained, and <see cref="ItemId"/> is then that item's (work contract, The outbox rule 3). Owned by ISK-0009.
/// </summary>
public readonly record struct WorkEnqueueResult(Guid ItemId, bool Created);
