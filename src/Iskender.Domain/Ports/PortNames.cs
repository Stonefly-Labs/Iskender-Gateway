using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>The name rules the persistence ports share. Owned by ISK-0004.</summary>
internal static partial class PortNames
{
    /// <summary>A module name: 1 to 31 characters of <c>a-z</c>, <c>0-9</c>, and <c>_</c>, starting with a letter.</summary>
    public static bool IsModule(string? value) => value is not null && ModulePattern().IsMatch(value);

    /// <summary>An operation or lock name: 1 to 63 characters of <c>a-z</c>, <c>0-9</c>, <c>_</c>, and <c>.</c>, starting with a letter.</summary>
    public static bool IsOperation(string? value) => value is not null && OperationPattern().IsMatch(value);

    public static string RequireModule(string? value) => IsModule(value) ? value! : throw new GatewayException(ErrorCodes.ValueInvalid);

    public static string RequireOperation(string? value) => IsOperation(value) ? value! : throw new GatewayException(ErrorCodes.ValueInvalid);

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,30}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ModulePattern();

    [GeneratedRegex(@"^[a-z][a-z0-9_.]{0,62}\z", RegexOptions.CultureInvariant)]
    private static partial Regex OperationPattern();
}
