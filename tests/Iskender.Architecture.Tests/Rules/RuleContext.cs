namespace Iskender.Architecture.Tests.Rules;

/// <summary>What a rule family reads: the repository root, the rule source, the scan, and the graph.</summary>
public sealed record RuleContext(string Root, RuleSource Source, RepositoryScanResult Scan, ProjectGraph Graph);
