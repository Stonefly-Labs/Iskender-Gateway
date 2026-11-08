using System.Reflection;

namespace Iskender.Architecture.Tests.Rules;

/// <summary>
/// What a rule family reads: the repository root, the rule source, the scan, and the graph. <c>Load</c> loads a
/// compiled assembly by name, and a test may replace it.
/// </summary>
public sealed record RuleContext(string Root, RuleSource Source, RepositoryScanResult Scan, ProjectGraph Graph)
{
    public Func<string, Assembly> Load { get; init; } = Assembly.Load;
}
