namespace Iskender.Container.Tests.Fixture;

/// <summary>
/// The skip-or-fail rule for tests that run images, as <c>RequirePostgreSql</c> is: without a container runtime a test is
/// skipped with the reason, unless the environment variable <c>CI</c> is <c>true</c>, when it fails instead.
/// </summary>
internal static class RequireDocker
{
    private static readonly Lazy<Task<string?>> Unavailable = new(ProbeAsync);

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

    /// <summary>Returns when <c>docker version</c> answers, and otherwise skips or fails the calling test.</summary>
    public static async Task ThatAsync()
    {
        var reason = await Unavailable.Value;
        var message = $"Docker unavailable: {reason}";
        switch (Decide(reason, Environment.GetEnvironmentVariable("CI")))
        {
            case Outcome.Skip:
                Assert.Skip(message);
                break;
            case Outcome.Fail:
                Assert.Fail(message + " (CI=true, so Docker-backed tests must run)");
                break;
        }
    }

    private static async Task<string?> ProbeAsync()
    {
        try
        {
            var result = await DockerCli.RunAsync(["version", "--format", "{{.Server.Version}}"], TimeSpan.FromSeconds(30), CancellationToken.None);
            return result.ExitCode == 0 ? null : $"docker version exited {result.ExitCode}";
        }
        catch (Exception exception)
        {
            return $"{exception.GetType().Name}: {exception.Message}";
        }
    }
}
