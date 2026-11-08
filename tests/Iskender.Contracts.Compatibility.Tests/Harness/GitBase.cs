using System.Diagnostics;

namespace Iskender.Contracts.Compatibility.Tests.Harness;

/// <summary>
/// The comparison base: <c>ISKENDER_COMPAT_BASE</c> when set, otherwise the merge base of <c>HEAD</c> and <c>origin/main</c>
/// (research R3). Files are read at the base with <c>git show</c>, from the repository root, with no network access.
/// </summary>
public sealed class GitBase : IFileSet
{
    public const string BaseVariable = "ISKENDER_COMPAT_BASE";

    private readonly string _root;

    private GitBase(string root, string commit)
    {
        _root = root;
        Commit = commit;
        Name = commit[..Math.Min(7, commit.Length)];
    }

    /// <summary>The full commit hash.</summary>
    public string Commit { get; }

    /// <summary>The short hash, for messages.</summary>
    public string Name { get; }

    /// <summary>
    /// Resolves the base. <paramref name="requested"/> overrides <c>ISKENDER_COMPAT_BASE</c> for tests; null reads the
    /// variable. An unresolvable base fails with H1.
    /// </summary>
    public static GitBase? Resolve(string root, out Violation? failure, string? requested = null)
    {
        requested ??= Environment.GetEnvironmentVariable(BaseVariable);
        failure = null;

        string? commit;
        if (!string.IsNullOrEmpty(requested))
        {
            commit = Git(root, "rev-parse", "--verify", "--quiet", requested + "^{commit}");
        }
        else
        {
            commit = Git(root, "merge-base", "HEAD", "origin/main");
        }

        if (commit is null)
        {
            var named = string.IsNullOrEmpty(requested) ? "the merge base of HEAD and origin/main" : $"{BaseVariable}={requested}";
            failure = new Violation(
                "H1", "public-contracts", RuleCatalog.ContractVersion, "base", $"{named} cannot be resolved; run git fetch origin main",
                "the repository", "no base");
            return null;
        }

        return new GitBase(root, commit.Trim());
    }

    public byte[]? Read(string path) => GitBytes(_root, "show", $"{Commit}:{path}");

    public IReadOnlyList<string> List(string directory)
    {
        var output = Git(_root, "ls-tree", "-r", "--name-only", Commit, "--", directory.TrimEnd('/') + "/");
        return output is null
            ? []
            : output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>The repository root, from <c>git rev-parse --show-toplevel</c>.</summary>
    public static string? RepositoryRoot(string start) => Git(start, "rev-parse", "--show-toplevel")?.Trim();

    private static string? Git(string root, params string[] arguments) =>
        GitBytes(root, arguments) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;

    private static byte[]? GitBytes(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("git could not be started.");
        using var output = new MemoryStream();
        var errors = process.StandardError.ReadToEndAsync();
        process.StandardOutput.BaseStream.CopyTo(output);
        process.WaitForExit();
        _ = errors.Result;

        return process.ExitCode == 0 ? output.ToArray() : null;
    }
}
