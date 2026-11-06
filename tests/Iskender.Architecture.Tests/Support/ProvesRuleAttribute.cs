namespace Iskender.Architecture.Tests.Support;

/// <summary>Marks a test as a negative fixture for a rule. The meta-test requires one for every rule.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ProvesRuleAttribute(string ruleId) : Attribute
{
    public string RuleId { get; } = ruleId;
}
