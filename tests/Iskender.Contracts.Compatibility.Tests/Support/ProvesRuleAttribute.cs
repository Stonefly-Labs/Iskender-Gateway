namespace Iskender.Contracts.Compatibility.Tests.Support;

/// <summary>Marks a test as proving a harness rule on a negative fixture. Rule H8 requires one for every rule.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ProvesRuleAttribute(string ruleId) : Attribute
{
    public string RuleId { get; } = ruleId;
}
