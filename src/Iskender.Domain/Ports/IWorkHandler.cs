namespace Iskender.Domain.Ports;

/// <summary>
/// The one handler of a work kind (work contract, Dispatch and apply rules 7 to 9). Its writes inside the database are
/// committed at most once, together with the item's completion. Owned by ISK-0009.
/// </summary>
public interface IWorkHandler
{
    /// <summary>
    /// <paramref name="unit"/> is open in the item's scope, already fenced. The handler writes only through it and never
    /// commits or disposes it. A failure is classified by its code; <see cref="WorkFailures.Permanent"/> poisons the item.
    /// </summary>
    ValueTask HandleAsync(WorkDelivery delivery, IUnitOfWork unit, CancellationToken cancellationToken);
}
