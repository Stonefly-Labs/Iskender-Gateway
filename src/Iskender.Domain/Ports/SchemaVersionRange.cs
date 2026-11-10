using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// The schema versions a release can run a module against (FR-024). <see cref="Maximum"/> may exceed the latest shipped
/// version, promising the next release's migrations up to it are expand-only. Owned by ISK-0004.
/// </summary>
public sealed record SchemaVersionRange
{
    /// <summary>Both bounds positive, with <paramref name="minimum"/> no more than <paramref name="maximum"/>.</summary>
    public SchemaVersionRange(int minimum, int maximum)
    {
        if (minimum < 1 || maximum < minimum)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    public int Minimum { get; }

    public int Maximum { get; }
}
