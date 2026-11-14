using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Hosting.Requests;

/// <summary>
/// Holds one request's <see cref="GatewayExecutionContext"/>, set once by <see cref="RequestBootstrap"/>. Scoped, so a
/// context is never shared between requests (data-model, Hosting). Owned by ISK-0006.
/// </summary>
internal sealed class RequestContextHolder
{
    private GatewayExecutionContext? _context;

    /// <summary>
    /// The request's context, or <c>null</c> outside an Iskender request. A <c>null</c> value fails with
    /// <c>core.value.invalid</c>, and a second set fails with <c>core.context.already_resolved</c>.
    /// </summary>
    public GatewayExecutionContext? Context
    {
        get => Volatile.Read(ref _context);
        set
        {
            if (value is null)
            {
                throw new GatewayException(ErrorCodes.ValueInvalid);
            }

            if (Interlocked.CompareExchange(ref _context, value, null) is not null)
            {
                throw new GatewayException(ErrorCodes.ContextAlreadyResolved);
            }
        }
    }

    /// <summary>The request's context. Fails with <c>core.context.not_resolved</c> outside an Iskender request.</summary>
    public GatewayExecutionContext Required => Context ?? throw new GatewayException(ErrorCodes.ContextNotResolved);
}
