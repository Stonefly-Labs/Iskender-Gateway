using Iskender.Core.Contracts.Errors;

namespace Iskender.Management.Migrations;

/// <summary>
/// What one <c>migrate</c> run did: its exit code, the operation, the outcome (<c>succeeded</c>, <c>failed</c>,
/// <c>current</c>, <c>behind</c>, or <c>ahead</c>), the count applied or reversed, and the failure's code, if any
/// (container contract, Command line rule 2; research R14). Owned by ISK-0007.
/// </summary>
public sealed record MigrationOutcome(int ExitCode, string Operation, string Outcome, int Count, ErrorCode? Code);
