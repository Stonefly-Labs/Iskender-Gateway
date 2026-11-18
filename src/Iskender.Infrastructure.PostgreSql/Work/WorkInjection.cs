namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>The injection points of ISK-0009 FR-040, where the crash and duplicate tests block, throw, or signal.</summary>
internal enum WorkInjectionPoint
{
    BeforeAttemptCommit,
    AfterAttemptCommit,

    /// <summary>After the handler returned and before the apply's completion and commit.</summary>
    DuringHandler,
    AfterApplyCommit,
    BeforeFailureCommit,
    AfterFailureCommit,
}

/// <summary>
/// The test seam the dispatcher calls at each injection point. Production uses <see cref="NoWorkInjection"/>. It is
/// internal and visible only to the work tests and the crash host. Owned by ISK-0009.
/// </summary>
internal interface IWorkInjection
{
    ValueTask ReachedAsync(WorkInjectionPoint point, Guid itemId, CancellationToken cancellationToken);
}

/// <summary>The production injection: does nothing.</summary>
internal sealed class NoWorkInjection : IWorkInjection
{
    public static NoWorkInjection Instance { get; } = new();

    public ValueTask ReachedAsync(WorkInjectionPoint point, Guid itemId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
