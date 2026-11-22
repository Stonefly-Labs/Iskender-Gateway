using Iskender.Core.Contracts.Identifiers;
using Iskender.Management.Security;
using Microsoft.AspNetCore.Http;

namespace Iskender.Identity.Tests.Fixture;

/// <summary>
/// The test-only principal resolver (operator-security contract, Admin routes rule 8, research R10, FR-043): it resolves
/// the principal ID in the <see cref="Header"/> request header, resolves nothing for a missing or malformed one, and can
/// be set to throw. It counts its calls, so a test can show the gate ran first. Only the operator fixture's hosts
/// register it; no production project references it.
/// </summary>
public sealed class TestOperatorPrincipalResolver : IOperatorPrincipalResolver
{
    /// <summary>The header holding the principal ID.</summary>
    public const string Header = "X-Iskender-Test-Principal";

    private int _calls;

    /// <summary>How many requests it was asked to resolve.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>When set, each call throws it.</summary>
    public Exception? Throw { get; set; }

    public ValueTask<OperatorPrincipalResolution> ResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (Throw is { } failure)
        {
            throw failure;
        }

        return ValueTask.FromResult(
            context.Request.Headers[Header].ToString() is { Length: > 0 } text && ResourceId.TryParse(text, ResourceTypes.Principal, out var principal, out _)
                ? OperatorPrincipalResolution.Resolved(principal!)
                : OperatorPrincipalResolution.Unresolved);
    }
}
