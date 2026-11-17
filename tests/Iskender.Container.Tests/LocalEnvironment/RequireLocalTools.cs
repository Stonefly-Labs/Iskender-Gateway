using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// <see cref="RequireDocker"/>'s skip-or-fail rule extended to <c>bash</c> and <c>git</c> on <c>PATH</c>
/// (local-environment contract, Test fixtures rule 3). Owned by ISK-0008.
/// </summary>
internal static class RequireLocalTools
{
    /// <summary>The shell the scripts run under: macOS's <c>/bin/bash</c> 3.2 where it exists, so 3.2 is exercised.</summary>
    public static string Bash { get; } = File.Exists("/bin/bash") ? "/bin/bash" : "bash";

    /// <summary>Returns when <c>bash</c>, <c>git</c>, and a container runtime are available, and otherwise skips or fails.</summary>
    public static async Task ThatAsync()
    {
        RequireBashAndGit();
        await RequireDocker.ThatAsync();
    }

    /// <summary>Returns when <c>bash</c> and <c>git</c> are on <c>PATH</c>, for tests that need no container runtime.</summary>
    public static void RequireBashAndGit()
    {
        foreach (var tool in new[] { "bash", "git" })
        {
            if (Find(tool) is null)
            {
                Apply($"{tool} is not on PATH");
            }
        }
    }

    /// <summary>The full path of <paramref name="tool"/> on <c>PATH</c>, or <see langword="null"/>.</summary>
    public static string? Find(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, tool))
            .FirstOrDefault(File.Exists);

    private static void Apply(string reason)
    {
        switch (RequireDocker.Decide(reason, Environment.GetEnvironmentVariable("CI")))
        {
            case RequireDocker.Outcome.Skip:
                Assert.Skip(reason);
                break;
            case RequireDocker.Outcome.Fail:
                Assert.Fail(reason + " (CI=true, so ISK-0008's local environment tests must run)");
                break;
        }
    }
}
