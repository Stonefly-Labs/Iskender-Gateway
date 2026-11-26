using Iskender.Core.Contracts.Errors;

namespace Iskender.Management.Bootstrap;

/// <summary>
/// What one <c>bootstrap</c> run did: its exit code, and the failure's code, if any (container contract, Bootstrap command
/// rule 5; research R14). Owned by ISK-0010.
/// </summary>
public readonly record struct BootstrapOutcome(int ExitCode, ErrorCode? Code)
{
    /// <summary><c>succeeded</c> for exit code 0, otherwise <c>failed</c>.</summary>
    public string Outcome => ExitCode == 0 ? "succeeded" : "failed";
}
