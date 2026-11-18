using System.Diagnostics;
using Iskender.Domain.Ports;
using Iskender.Work.Tests.Fixture;

namespace Iskender.Work.Tests.Crash;

/// <summary>
/// One run of the crash host (ISK-0009 T025, research R18) against the fixture's database: started with a scope set and
/// an injection point, waited on for one of its fixed lines, and killed with <c>SIGKILL</c>.
/// </summary>
internal sealed class CrashHostProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly object _gate = new();
    private readonly List<(string Line, TaskCompletionSource Seen)> _waiters = [];
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    private CrashHostProcess(Process process)
    {
        _process = process;
        _process.OutputDataReceived += (_, line) => Seen(line.Data);
        _process.ErrorDataReceived += (_, _) => { };
    }

    /// <summary>Starts the host with <paramref name="scopes"/> and <paramref name="point"/>.</summary>
    public static CrashHostProcess Start(WorkFixture work, IEnumerable<string> scopes, string point, int maximumAttempts)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(HostPath);
        start.ArgumentList.Add($"Host={work.Postgres.Host};Port={work.Postgres.Port};Database={work.DatabaseName};Username={PostgreSqlFixture.RuntimeLogin};Password={work.Postgres.RuntimePassword}");
        start.ArgumentList.Add(string.Join(',', scopes));
        start.ArgumentList.Add(point);
        start.ArgumentList.Add(maximumAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var process = new CrashHostProcess(new Process { StartInfo = start, EnableRaisingEvents = true });
        process._process.Start();
        process._process.BeginOutputReadLine();
        process._process.BeginErrorReadLine();
        return process;
    }

    /// <summary>The crash host's assembly, built beside this one under the same configuration.</summary>
    public static string HostPath { get; } = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "Iskender.Work.CrashHost",
        "bin",
        new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name,
        new DirectoryInfo(AppContext.BaseDirectory).Name,
        "Iskender.Work.CrashHost.dll"));

    /// <summary>Waits, bounded, until the host prints <c>reached &lt;name&gt;</c>.</summary>
    public async Task ReachedAsync(string name)
    {
        var line = "reached " + name;
        TaskCompletionSource seen;
        lock (_gate)
        {
            if (_seen.Contains(line))
            {
                return;
            }

            seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((line, seen));
        }

        var exited = _process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var first = await Task.WhenAny(seen.Task, exited).WaitAsync(Eventually.Bound, TestContext.Current.CancellationToken);
        Assert.True(first == seen.Task, $"The crash host exited with {(_process.HasExited ? _process.ExitCode : -1)} before printing {line}.");
    }

    /// <summary>Kills the host with <c>SIGKILL</c> and waits for it to exit.</summary>
    public async Task KillAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        await _process.WaitForExitAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await KillAsync();
        _process.Dispose();
    }

    private void Seen(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_gate)
        {
            _seen.Add(line);
            foreach (var waiter in _waiters.Where(waiter => waiter.Line == line))
            {
                waiter.Seen.TrySetResult();
            }
        }
    }
}
