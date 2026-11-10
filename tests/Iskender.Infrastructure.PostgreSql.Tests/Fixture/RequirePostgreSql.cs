namespace Iskender.Infrastructure.PostgreSql.Tests.Fixture;

/// <summary>
/// The skip-or-fail rule for container-backed tests (FR-035): without a container runtime a test is skipped with the
/// reason, unless the environment variable <c>CI</c> is <c>true</c>, when it fails instead.
/// </summary>
internal static class RequirePostgreSql
{
    public enum Outcome
    {
        Run,
        Skip,
        Fail,
    }

    public static Outcome Decide(string? unavailableReason, string? ci) =>
        unavailableReason is null ? Outcome.Run
        : string.Equals(ci, "true", StringComparison.OrdinalIgnoreCase) ? Outcome.Fail
        : Outcome.Skip;

    /// <summary>Returns when PostgreSQL is available, and otherwise skips or fails the calling test.</summary>
    public static void That(string? unavailableReason)
    {
        var message = $"PostgreSQL test container unavailable: {unavailableReason}";
        switch (Decide(unavailableReason, Environment.GetEnvironmentVariable("CI")))
        {
            case Outcome.Skip:
                Assert.Skip(message);
                break;
            case Outcome.Fail:
                Assert.Fail(message + " (CI=true, so container-backed tests must run)");
                break;
        }
    }
}
