using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Identity;
using Iskender.Management.Security;
using Microsoft.Extensions.Logging;

namespace Iskender.Identity.Tests.Telemetry;

/// <summary>
/// The operator-security contract's events, activities, and instruments, pinned exactly (ISK-0010 T013, Diagnostics,
/// FR-037): every event's ID, name, level, and template, with only the allowed fields; event 9006 at most once a minute
/// per instance; event 9007 naming only the exception's type; every instrument's name, kind, and label set.
/// </summary>
public class OperatorLogTests
{
    /// <summary>The contract's allowed fields: purpose, reason class, route template, a count, error code, and a failure's type name.</summary>
    private static readonly HashSet<string> AllowedFields = ["Purpose", "Reason", "Route", "Superseded", "Code", "Failure"];

    private const string Route = "/admin/v1/installation";

    public static TheoryData<int, string, LogLevel, string> Events() => new()
    {
        { 9001, "OperatorRedemptionSucceeded", LogLevel.Information, "Iskender operator redemption succeeded: purpose {Purpose}." },
        { 9002, "OperatorRedemptionFailed", LogLevel.Warning, "Iskender operator redemption failed: purpose {Purpose}, reason {Reason}." },
        { 9003, "OperatorRequestDenied", LogLevel.Information, "Iskender admin request denied: route {Route}, reason {Reason}." },
        { 9004, "OperatorTokenIssued", LogLevel.Information, "Iskender operator token issued: purpose {Purpose}, superseded {Superseded}." },
        { 9005, "OperatorRouteUndeclared", LogLevel.Error, "Iskender admin route has no single declaration: route {Route}." },
        { 9006, "OperatorRedemptionLimited", LogLevel.Warning, "Iskender operator redemption attempts limited." },
        { 9007, "OperatorResolutionFailed", LogLevel.Warning, "Iskender operator principal resolution failed: failure {Failure}." },
        { 9008, "OperatorStoreUnavailable", LogLevel.Warning, "Iskender operator store unavailable: route {Route}, code {Code}." },
    };

    [Theory]
    [MemberData(nameof(Events))]
    public void EachEventHasItsIdNameLevelTemplateAndOnlyAllowedFields(int id, string name, LogLevel level, string template)
    {
        var logger = new TemplateLogger();

        WriteEvery(logger);

        var logged = Assert.Single(logger.Events, logged => logged.Id.Id == id);
        Assert.Equal((name, level, template), (logged.Id.Name, logged.Level, logged.Template));
        Assert.Equal(OperatorLog.Category, logged.Category);
        Assert.All(logged.Fields, field => Assert.Contains(field.Key, AllowedFields));
        Assert.Null(logged.Exception);
    }

    [Fact]
    public void EightEventsAreDefinedAndNoneRepeats()
    {
        var logger = new TemplateLogger();

        WriteEvery(logger);

        Assert.Equal(Enumerable.Range(9001, 8), logger.Events.Select(logged => logged.Id.Id));
    }

    [Fact]
    public void TheEventsWriteTheirCodesCountsAndOnlyAFailuresTypeName()
    {
        var logger = new TemplateLogger();

        WriteEvery(logger);

        var fields = logger.Events.ToDictionary(logged => logged.Id.Id, logged => logged.Fields.ToDictionary(field => field.Key, field => field.Value?.ToString()));
        Assert.Equal("bootstrap", fields[9001]["Purpose"]);
        Assert.Equal(("recovery", "wrong_secret"), (fields[9002]["Purpose"], fields[9002]["Reason"]));
        Assert.Equal((Route, "forbidden"), (fields[9003]["Route"], fields[9003]["Reason"]));
        Assert.Equal(("bootstrap", "2"), (fields[9004]["Purpose"], fields[9004]["Superseded"]));
        Assert.Equal("InvalidOperationException", fields[9007]["Failure"]);
        Assert.Equal((Route, "operator.state_unavailable"), (fields[9008]["Route"], fields[9008]["Code"]));
        Assert.DoesNotContain(logger.Events, logged => logged.Message.Contains("secret detail", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("limited")]
    [InlineData("rejected_body")]
    [InlineData("unavailable")]
    [InlineData("other")]
    public void EventNineThousandThreeRefusesAReasonItDoesNotName(string reason)
    {
        var logger = new TemplateLogger();

        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => logger.Operator.RequestDenied(Route, reason)).Code);
        Assert.Empty(logger.Events);
    }

    [Fact]
    public void EventNineThousandSixIsWrittenAtMostOnceAMinutePerInstance()
    {
        var clock = new ManualClock();
        var logger = new TemplateLogger(clock);
        var other = new TemplateLogger(clock);

        logger.Operator.RedemptionLimited();
        logger.Operator.RedemptionLimited();
        clock.Advance(TimeSpan.FromSeconds(59));
        logger.Operator.RedemptionLimited();
        other.Operator.RedemptionLimited();
        Assert.Single(logger.Events);
        Assert.Single(other.Events);

        clock.Advance(TimeSpan.FromSeconds(1));
        logger.Operator.RedemptionLimited();
        Assert.Equal(2, logger.Events.Count);
    }

    public static TheoryData<string, string, string[]> Instruments() => new()
    {
        { "iskender.identity.redemptions", "Counter", ["outcome", "purpose", "reason"] },
        { "iskender.identity.admin.denials", "Counter", ["reason", "route"] },
        { "iskender.identity.tokens.issued", "Counter", ["purpose"] },
        { "iskender.identity.tokens.superseded", "Counter", ["purpose"] },
    };

    [Theory]
    [MemberData(nameof(Instruments))]
    public void EachInstrumentHasItsKindAndLabels(string name, string kind, string[] labels)
    {
        using var telemetry = new OperatorTelemetry();
        var published = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var recorded = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meters) =>
            {
                if (instrument.Meter.Name == OperatorTelemetry.SourceName)
                {
                    published[instrument.Name] = instrument.GetType().Name.Split('`')[0];
                    meters.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            recorded[instrument.Name] = string.Join(",", tags.ToArray().Select(tag => tag.Key).Order(StringComparer.Ordinal)));
        listener.Start();

        telemetry.RecordRedemption(OperatorTokenPurpose.Bootstrap, reason: null);
        telemetry.RecordDenial(Route, OperatorDenials.Limited);
        telemetry.RecordIssued(OperatorTokenPurpose.Recovery, superseded: 1);

        Assert.Equal(kind, published[name]);
        Assert.Equal(string.Join(",", labels), recorded[name]);
        Assert.Equal(4, published.Count);
        Assert.Equal(OperatorTelemetry.Instruments.Select(instrument => instrument.Name).Order(StringComparer.Ordinal), published.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ARedemptionCountsItsOutcomeAndReasonClassAndADenialRefusesAnUnknownReason()
    {
        using var telemetry = new OperatorTelemetry();
        var recorded = new ConcurrentQueue<string>();
        using var listener = Listen(telemetry, recorded);

        telemetry.RecordRedemption(OperatorTokenPurpose.Bootstrap, reason: null);
        telemetry.RecordRedemption(OperatorTokenPurpose.Enrollment, RedemptionFailureReason.Expired);
        telemetry.RecordIssued(OperatorTokenPurpose.Bootstrap, superseded: 0);

        Assert.Equal(
            [
                "iskender.identity.redemptions 1 outcome=succeeded,purpose=bootstrap,reason=none",
                "iskender.identity.redemptions 1 outcome=failed,purpose=enrollment,reason=expired",
                "iskender.identity.tokens.issued 1 purpose=bootstrap",
            ],
            recorded);
        Assert.Same(ErrorCodes.ValueInvalid, Assert.Throws<GatewayException>(() => telemetry.RecordDenial(Route, "other")).Code);
    }

    [Fact]
    public void TheActivitiesCarryOnlyPurposeRouteOutcomeAndReason()
    {
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OperatorTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        OperatorTelemetry.End(OperatorTelemetry.StartRedeem(OperatorTokenPurpose.Bootstrap), OperatorTelemetry.Failed, "wrong_secret");
        OperatorTelemetry.End(OperatorTelemetry.StartAuthorize(Route), OperatorTelemetry.Succeeded, OperatorTelemetry.None);

        var redeem = Assert.Single(stopped, activity => activity.OperationName == OperatorTelemetry.RedeemActivity);
        Assert.Equal(["outcome", "purpose", "reason"], redeem.TagObjects.Select(tag => tag.Key).Order(StringComparer.Ordinal));
        var authorize = Assert.Single(stopped, activity => activity.OperationName == OperatorTelemetry.AuthorizeActivity);
        Assert.Equal(["outcome", "reason", "route"], authorize.TagObjects.Select(tag => tag.Key).Order(StringComparer.Ordinal));
        Assert.Equal(Route, authorize.GetTagItem("route"));
    }

    private static MeterListener Listen(OperatorTelemetry telemetry, ConcurrentQueue<string> recorded)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meters) =>
            {
                if (instrument.Meter.Name == OperatorTelemetry.SourceName)
                {
                    meters.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            recorded.Enqueue($"{instrument.Name} {value} {string.Join(",", tags.ToArray().OrderBy(tag => tag.Key, StringComparer.Ordinal).Select(tag => $"{tag.Key}={tag.Value}"))}"));
        listener.Start();
        GC.KeepAlive(telemetry);
        return listener;
    }

    private static void WriteEvery(TemplateLogger logger)
    {
        logger.Operator.RedemptionSucceeded(OperatorTokenPurpose.Bootstrap);
        logger.Operator.RedemptionFailed(OperatorTokenPurpose.Recovery, RedemptionFailureReason.WrongSecret);
        logger.Operator.RequestDenied(Route, OperatorDenials.Forbidden);
        logger.Operator.TokenIssued(OperatorTokenPurpose.Bootstrap, 2);
        logger.Operator.RouteUndeclared(Route);
        logger.Operator.RedemptionLimited();
        logger.Operator.ResolutionFailed(new InvalidOperationException("secret detail"));
        logger.Operator.StoreUnavailable(Route, ErrorCodes.OperatorStateUnavailable);
    }

    /// <summary>A clock whose timestamp moves only when told to.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks = 1;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    /// <summary>Records each event's category, ID, level, template, rendered message, fields, and exception.</summary>
    private sealed class TemplateLogger : ILoggerFactory, ILogger
    {
        private readonly List<Logged> _events = [];
        private string _category = "";

        public TemplateLogger(TimeProvider? clock = null)
        {
            Operator = new OperatorLog(this, clock);
        }

        public OperatorLog Operator { get; }

        public List<Logged> Events => _events;

        public ILogger CreateLogger(string categoryName)
        {
            _category = categoryName;
            return this;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            var template = values.FirstOrDefault(value => value.Key == "{OriginalFormat}").Value?.ToString() ?? "";
            _events.Add(new(_category, eventId, logLevel, template, formatter(state, exception), [.. values.Where(value => value.Key != "{OriginalFormat}")], exception));
        }
    }

    private sealed record Logged(string Category, EventId Id, LogLevel Level, string Template, string Message, IReadOnlyList<KeyValuePair<string, object?>> Fields, Exception? Exception);
}
