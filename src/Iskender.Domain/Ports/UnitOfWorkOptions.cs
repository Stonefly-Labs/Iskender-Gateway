using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>How a unit of work is opened. An invalid value fails with <c>core.value.invalid</c>. Owned by ISK-0004.</summary>
public sealed record UnitOfWorkOptions
{
    public UnitOfWorkOptions(string module, string operation)
    {
        Module = module;
        Operation = operation;
    }

    /// <summary>The module opening the unit, a bounded log and metric label.</summary>
    public string Module
    {
        get;
        init => field = PortNames.RequireModule(value);
    }

    /// <summary>The operation kind, such as <c>registry.update</c>, a bounded log and metric label.</summary>
    public string Operation
    {
        get;
        init => field = PortNames.RequireOperation(value);
    }

    /// <summary>The transaction isolation level.</summary>
    public UnitOfWorkIsolation Isolation
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new GatewayException(ErrorCodes.ValueInvalid);
    } = UnitOfWorkIsolation.ReadCommitted;

    /// <summary>The bound on the whole unit, capped by the deadline (research R7). More than zero.</summary>
    public TimeSpan Timeout
    {
        get;
        init => field = value > TimeSpan.Zero ? value : throw new GatewayException(ErrorCodes.ValueInvalid);
    } = TimeSpan.FromSeconds(60);

    /// <summary>A deadline for a unit not opened from a request. The earlier of this and the scope's deadline applies.</summary>
    public DateTimeOffset? Deadline { get; init; }

    /// <summary>Whether <c>UnitOfWorkRetry</c> may re-run the whole unit after a transient conflict.</summary>
    public bool SafeToRerun { get; init; }
}
