using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// The kind of a work item: the module that produces it and a name, shown as <c>&lt;module&gt;.&lt;name&gt;</c> (work
/// contract, Work items rule 1). Owned by ISK-0009.
/// </summary>
public sealed partial record WorkKind
{
    /// <summary>
    /// <paramref name="module"/> matches the module pattern <c>[a-z][a-z0-9_]{0,30}</c>, and <paramref name="name"/>
    /// matches <c>[a-z][a-z0-9_]{0,62}</c>. Otherwise <c>core.value.invalid</c>.
    /// </summary>
    public WorkKind(string module, string name)
    {
        Module = PortNames.RequireModule(module);
        Name = name is not null && NamePattern().IsMatch(name) ? name : throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    public string Module { get; }

    public string Name { get; }

    /// <summary>Reads <c>&lt;module&gt;.&lt;name&gt;</c>. Anything else returns <c>false</c>.</summary>
    public static bool TryParse(string? text, out WorkKind? kind)
    {
        kind = null;
        var dot = text?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        if (dot <= 0)
        {
            return false;
        }

        var module = text![..dot];
        var name = text[(dot + 1)..];
        if (!PortNames.IsModule(module) || !NamePattern().IsMatch(name))
        {
            return false;
        }

        kind = new WorkKind(module, name);
        return true;
    }

    public override string ToString() => $"{Module}.{Name}";

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,62}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
