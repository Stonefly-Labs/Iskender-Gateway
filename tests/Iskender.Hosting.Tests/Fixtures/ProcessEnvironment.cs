namespace Iskender.Hosting.Tests.Fixtures;

/// <summary>
/// The collection for tests that set process environment variables, which every host in the process reads when it opts
/// in. Its tests run alone, and each restores the variables it set.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironment
{
    public const string Name = "Process environment";

    /// <summary>Sets <paramref name="variables"/> until the result is disposed, then restores each one's previous value.</summary>
    public static IDisposable Set(IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var previous = variables.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        foreach (var (name, value) in variables)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        return new Restore(previous);
    }

    private sealed class Restore(Dictionary<string, string?> previous) : IDisposable
    {
        public void Dispose()
        {
            foreach (var (name, value) in previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
