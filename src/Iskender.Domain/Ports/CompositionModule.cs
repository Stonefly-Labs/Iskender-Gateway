using System.Collections.Immutable;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// One module a host composed: its name, the configuration sections it registers, and the modules it requires. A
/// registration method adds one instance to the host's service collection, and Hosting builds the schema registry and
/// checks requirements from every instance there (hosting contract, Registration rules 6 and 7). Owned by ISK-0006.
/// </summary>
public sealed class CompositionModule
{
    private const int MaxNameLength = 64;

    private readonly ImmutableArray<SectionSchema> _sections;

    private readonly ImmutableArray<string> _requires;

    /// <summary>
    /// A name or requirement outside the name rule, a <c>null</c> or repeated section or requirement, or a <c>null</c>
    /// argument fails with <c>core.value.invalid</c>. Both lists are copied, so a later change to the caller's list has
    /// no effect.
    /// </summary>
    public CompositionModule(string name, IEnumerable<SectionSchema> sections, IEnumerable<string> requires)
    {
        if (!IsName(name) || sections is null || requires is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        _sections = [.. sections];
        _requires = [.. requires];
        if (_sections.Any(section => section is null) || _sections.Distinct().Count() != _sections.Length
            || !_requires.All(IsName) || _requires.Distinct(StringComparer.Ordinal).Count() != _requires.Length)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Name = name;
    }

    /// <summary>1 to 64 characters of <c>a-z</c>, <c>0-9</c>, and <c>-</c>, starting with a letter.</summary>
    public string Name { get; }

    /// <summary>The sections the module registers, with no repeats.</summary>
    public IReadOnlyList<SectionSchema> Sections => _sections;

    /// <summary>The names of the modules this one needs, with no repeats.</summary>
    public IReadOnlyList<string> Requires => _requires;

    private static bool IsName(string? name) =>
        name is { Length: >= 1 and <= MaxNameLength } && name[0] is >= 'a' and <= 'z'
        && name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}
