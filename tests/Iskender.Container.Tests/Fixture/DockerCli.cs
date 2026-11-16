using System.Diagnostics;

namespace Iskender.Container.Tests.Fixture;

/// <summary>What one <c>docker</c> command returned.</summary>
internal sealed record DockerResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs the <c>docker</c> command line through <see cref="Process"/>, bounded by a timeout.</summary>
internal static class DockerCli
{
    /// <summary>
    /// Runs <c>docker</c> with <paramref name="arguments"/>, capturing its exit code and both output streams. A command
    /// still running at <paramref name="timeout"/> is killed, and the call throws <see cref="TimeoutException"/>.
    /// </summary>
    public static async Task<DockerResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? "",
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("docker did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(bound.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"docker {string.Join(' ', arguments)} did not finish within {timeout}.");
        }

        return new DockerResult(process.ExitCode, await output, await error);
    }
}
