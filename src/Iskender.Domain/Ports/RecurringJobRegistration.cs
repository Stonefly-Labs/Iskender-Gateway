using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Ports;

/// <summary>The registration of a recurring job, with the interval and batch it runs with by default. Owned by ISK-0009.</summary>
public sealed record RecurringJobRegistration
{
    private static readonly TimeSpan ShortestInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan LongestInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// A name in <see cref="WorkKind"/>'s shape, an interval of 1 min to 24 h, a batch of 1 to 10,000, and a concrete class
    /// implementing <see cref="IRecurringJob"/>. Otherwise <c>core.value.invalid</c>.
    /// </summary>
    public RecurringJobRegistration(string name, TimeSpan defaultInterval, int defaultBatch, Type jobType)
    {
        if (!WorkKind.TryParse(name, out _)
            || defaultInterval < ShortestInterval || defaultInterval > LongestInterval
            || defaultBatch is < 1 or > 10_000
            || jobType is null || !jobType.IsClass || jobType.IsAbstract || !typeof(IRecurringJob).IsAssignableFrom(jobType))
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        Name = name;
        DefaultInterval = defaultInterval;
        DefaultBatch = defaultBatch;
        JobType = jobType;
    }

    public string Name { get; }

    public TimeSpan DefaultInterval { get; }

    public int DefaultBatch { get; }

    public Type JobType { get; }
}
