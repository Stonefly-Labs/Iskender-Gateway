using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// What faulted a unit of work, recorded on <see cref="IUnitOfWork.Failure"/> for the retry helper and for diagnostics.
/// It holds no exception, SQL, or server text (research R9). Owned by ISK-0004.
/// </summary>
public sealed class PersistenceFailure
{
    public PersistenceFailure(ErrorCode code, PersistenceFailureClass failureClass, bool commitOutcomeUnknown = false)
    {
        Code = code ?? throw new GatewayException(ErrorCodes.ValueInvalid);
        FailureClass = Enum.IsDefined(failureClass) ? failureClass : throw new GatewayException(ErrorCodes.ValueInvalid);
        CommitOutcomeUnknown = commitOutcomeUnknown;
    }

    /// <summary>The code of the <see cref="GatewayException"/> that was thrown.</summary>
    public ErrorCode Code { get; }

    public PersistenceFailureClass FailureClass { get; }

    /// <summary>True only when the connection broke during <c>COMMIT</c>, so the commit may or may not have happened.</summary>
    public bool CommitOutcomeUnknown { get; }
}
