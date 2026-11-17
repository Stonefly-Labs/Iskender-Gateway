using System.Diagnostics;

namespace Iskender.Container.Tests.Fixture;

/// <summary>What one <c>docker</c> command returned.</summary>
internal sealed record DockerResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs the <c>docker</c> command line, or another command, through <see cref="Process"/>, bounded by a timeout.</summary>
internal static class DockerCli
{
    /// <summary>
    /// Runs <c>docker</c> with <paramref name="arguments"/>, capturing its exit code and both output streams. A command
    /// still running at <paramref name="timeout"/> is killed, and the call throws <see cref="TimeoutException"/>.
    /// <paramref name="environment"/> adds variables to the command's environment.
    /// </summary>
    public static Task<DockerResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environment = null) =>
        RunProcessAsync(
            "docker",
            arguments,
            timeout,
            cancellationToken,
            workingDirectory,
            environment?.ToDictionary(pair => pair.Key, string? (pair) => pair.Value, StringComparer.Ordinal));

    /// <summary>
    /// Runs <paramref name="fileName"/> as <see cref="RunAsync"/> runs <c>docker</c> (ISK-0008's script runs). A
    /// <see langword="null"/> value in <paramref name="environment"/> removes that variable. Standard input is closed at
    /// once, after <paramref name="standardInput"/> is written when it is given.
    /// </summary>
    public static async Task<DockerResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken, string? workingDirectory = null, IReadOnlyDictionary<string, string?>? environment = null, string? standardInput = null)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? "",
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{fileName} did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
        }

        process.StandardInput.Close();
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(bound.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} {string.Join(' ', arguments)} did not finish within {timeout}.");
        }

        return new DockerResult(process.ExitCode, await output, await error);
    }
}
