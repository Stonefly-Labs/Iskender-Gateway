using Iskender.Container.Tests.Fixture;

namespace Iskender.Container.Tests.LocalEnvironment;

/// <summary>
/// A clean copy of the tracked files, made into its own Git checkout (research R17, data-model Test fixtures): every
/// path <c>git ls-files</c> lists, with its working-tree content and executable bit, then <c>git init</c> and one commit
/// with a fixed test identity. No ignored or untracked file is copied. The directory is deleted on disposal.
/// </summary>
internal sealed class LocalCheckout : IAsyncDisposable
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(2);

    private LocalCheckout(string root, string head)
    {
        Root = root;
        Head = head;
    }

    /// <summary>The copy's root directory.</summary>
    public string Root { get; }

    /// <summary>The copy's full commit hash.</summary>
    public string Head { get; }

    public static async Task<LocalCheckout> CreateAsync()
    {
        var source = await ImageBuild.RepositoryRootAsync();
        var listed = await GitAsync(source, "ls-files", "-z");
        var root = Path.Combine(Path.GetTempPath(), $"iskender-local-{Guid.NewGuid():N}"[..27]);
        Directory.CreateDirectory(root);
        try
        {
            foreach (var path in listed.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var from = Path.Combine(source, path);
                if (!File.Exists(from))
                {
                    continue;
                }

                var to = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(to, File.GetUnixFileMode(from));
                }
            }

            await GitAsync(root, "init", "--quiet");
            await GitAsync(root, "add", "--all");
            await GitAsync(root, "-c", "user.name=iskender-test", "-c", "user.email=test@iskender.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "--no-verify", "-m", "ISK-0008 local checkout");
            return new LocalCheckout(root, (await GitAsync(root, "rev-parse", "HEAD")).Trim());
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    /// <summary>Runs <c>git</c> in <paramref name="directory"/> and returns its standard output, failing on a non-zero exit.</summary>
    public static async Task<string> GitAsync(string directory, params string[] arguments)
    {
        var result = await DockerCli.RunProcessAsync("git", ["-C", directory, .. arguments], GitTimeout, CancellationToken.None);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', arguments)} exited {result.ExitCode}: {result.StandardError}");
        return result.StandardOutput;
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
