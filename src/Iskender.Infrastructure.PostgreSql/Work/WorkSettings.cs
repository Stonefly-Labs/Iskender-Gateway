using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.PostgreSql.Work;

/// <summary>The validated <c>iskender.work</c> section, read once (<c>work</c> contract Configuration). Owned by ISK-0009.</summary>
internal sealed class WorkSettings
{
    /// <summary>The shipped job <c>jobs.purgeExpired</c> configures (<c>work</c> contract Recurring jobs rule 4).</summary>
    public const string PurgeExpiredJobName = "coordination.purge_expired";

    /// <summary>The shipped job <c>jobs.retention</c> configures (<c>work</c> contract Recurring jobs rule 5).</summary>
    public const string RetentionJobName = "work.retention";

    private WorkSettings(ConfigurationSection section)
    {
        var dispatch = section.GetSection("dispatch");
        var retry = section.GetSection("retry");
        var retention = section.GetSection("retention");
        var jobs = section.GetSection("jobs");
        PollInterval = section.GetDuration("pollInterval")!.Value;
        BatchSize = (int)section.GetInt64("batchSize")!.Value;
        LeaseDuration = section.GetDuration("leaseDuration")!.Value;
        Shards = (int)dispatch.GetInt64("shards")!.Value;
        Concurrency = (int)dispatch.GetInt64("concurrency")!.Value;
        ScopeConcurrency = (int)dispatch.GetInt64("scopeConcurrency")!.Value;
        HandlerTimeout = section.GetSection("handler").GetDuration("timeout")!.Value;
        InitialDelay = retry.GetDuration("initialDelay")!.Value;
        MaximumDelay = retry.GetDuration("maximumDelay")!.Value;
        MaximumAttempts = (int)retry.GetInt64("maximumAttempts")!.Value;
        CompletedRetention = retention.GetDuration("completed")!.Value;
        PoisonedRetention = retention.GetDuration("poisoned")!.Value;
        PurgeExpiredInterval = jobs.GetSection("purgeExpired").GetDuration("interval")!.Value;
        PurgeExpiredBatch = (int)jobs.GetSection("purgeExpired").GetInt64("batch")!.Value;
        RetentionInterval = jobs.GetSection("retention").GetDuration("interval")!.Value;
        RetentionBatch = (int)jobs.GetSection("retention").GetInt64("batch")!.Value;
    }

    public TimeSpan PollInterval { get; }

    /// <summary>
    /// The interval and batch <paramref name="registration"/>'s job runs with: the <c>jobs</c> settings for a job they
    /// name, otherwise its registration's defaults.
    /// </summary>
    public (TimeSpan Interval, int Batch) Schedule(RecurringJobRegistration registration) => registration.Name switch
    {
        PurgeExpiredJobName => (PurgeExpiredInterval, PurgeExpiredBatch),
        RetentionJobName => (RetentionInterval, RetentionBatch),
        _ => (registration.DefaultInterval, registration.DefaultBatch),
    };

    /// <summary>Directory rows per page, and items per shard claim.</summary>
    public int BatchSize { get; }

    public TimeSpan LeaseDuration { get; }

    /// <summary>Dispatch shards per scope, a divisor of 64.</summary>
    public int Shards { get; }

    /// <summary>Items in flight in this worker.</summary>
    public int Concurrency { get; }

    /// <summary>Items in flight per scope in this worker, at most <see cref="Concurrency"/>.</summary>
    public int ScopeConcurrency { get; }

    public TimeSpan HandlerTimeout { get; }

    public TimeSpan InitialDelay { get; }

    public TimeSpan MaximumDelay { get; }

    public int MaximumAttempts { get; }

    public TimeSpan CompletedRetention { get; }

    public TimeSpan PoisonedRetention { get; }

    public TimeSpan PurgeExpiredInterval { get; }

    public int PurgeExpiredBatch { get; }

    public TimeSpan RetentionInterval { get; }

    public int RetentionBatch { get; }

    /// <summary>
    /// Reads the section from a validated snapshot, then makes the checks of research R13 that the schema cannot express,
    /// each failing with <c>config.field.out_of_range</c> naming the field and no value: <c>dispatch.shards</c> divides 64,
    /// <c>dispatch.scopeConcurrency</c> is at most <c>dispatch.concurrency</c>, <c>retry.maximumDelay</c> is at least
    /// <c>retry.initialDelay</c>, and <c>leaseDuration</c> is within <paramref name="coordination"/>'s
    /// <c>leases.minimumDuration</c> and <c>leases.maximumDuration</c>.
    /// </summary>
    public static WorkSettings From(ConfigurationSnapshot snapshot, CoordinationSettings coordination)
    {
        if (snapshot is null || coordination is null)
        {
            throw new GatewayException(ErrorCodes.ValueInvalid);
        }

        var section = snapshot.GetSection(WorkSectionSchema.Schema);
        var settings = new WorkSettings(section);
        if (64 % settings.Shards != 0)
        {
            throw OutOfRange($"{section.Path}.dispatch.shards");
        }

        if (settings.ScopeConcurrency > settings.Concurrency)
        {
            throw OutOfRange($"{section.Path}.dispatch.scopeConcurrency");
        }

        if (settings.MaximumDelay < settings.InitialDelay)
        {
            throw OutOfRange($"{section.Path}.retry.maximumDelay");
        }

        if (settings.LeaseDuration < coordination.MinimumLeaseDuration || settings.LeaseDuration > coordination.MaximumLeaseDuration)
        {
            throw OutOfRange($"{section.Path}.leaseDuration");
        }

        return settings;
    }

    private static GatewayException OutOfRange(string field) => new(GatewayError.Create(
        ErrorCodes.ConfigFieldOutOfRange,
        RequestCorrelationId.New(),
        OutputVisibility.None,
        diagnostics: new ErrorDiagnostics(values: [new("field", field)])));
}
