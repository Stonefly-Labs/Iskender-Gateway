using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>
/// What a feature does when its coordinator fails (TR-5): fail closed, or degrade and raise a named, safe signal. A
/// feature in a mandatory security or hard-budget path cannot degrade. Owned by ISK-0005.
/// </summary>
public sealed class OutageBehavior
{
    private OutageBehavior(string? signal)
    {
        Signal = signal;
    }

    /// <summary>Every coordinator failure fails the feature's operation.</summary>
    public static OutageBehavior FailClosed { get; } = new(signal: null);

    /// <summary>The degrade signal's name, or <c>null</c> for <see cref="FailClosed"/>.</summary>
    public string? Signal { get; }

    public bool Degrades => Signal is not null;

    /// <summary>
    /// Degrades and raises <paramref name="signal"/>, which has the operation pattern (1 to 63 characters of <c>a-z</c>,
    /// <c>0-9</c>, <c>_</c>, <c>.</c>, starting with a letter). An invalid signal fails with <c>core.value.invalid</c>.
    /// </summary>
    public static OutageBehavior Degrade(string signal) => new(PortNames.RequireOperation(signal));

    public override string ToString() => Signal is null ? "fail_closed" : "degrade";
}
