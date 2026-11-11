using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>A row's optimistic concurrency marker: a positive counter (research R10). Owned by ISK-0004.</summary>
public readonly record struct VersionToken
{
    public VersionToken(long value)
    {
        Value = value > 0 ? value : throw new GatewayException(ErrorCodes.ValueInvalid);
    }

    /// <summary>The version of a newly inserted row.</summary>
    public static VersionToken Initial { get; } = new(1);

    public long Value { get; }

    /// <summary>The version after one more change.</summary>
    public VersionToken Next() => new(checked(Value + 1));
}
