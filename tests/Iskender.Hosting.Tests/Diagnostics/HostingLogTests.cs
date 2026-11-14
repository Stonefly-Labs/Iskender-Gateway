using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Ports;
using Iskender.Hosting.Diagnostics;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Composition;
using Microsoft.Extensions.Logging;

namespace Iskender.Hosting.Tests.Diagnostics;

/// <summary>Events 6001 to 6003 exactly as the hosting contract's Diagnostics table states them, with the rule 1 field texts.</summary>
public class HostingLogTests
{
    [Fact]
    public void Event6001NamesTheModulesSectionsEnvironmentAndRevisionInOrdinalOrder()
    {
        var logger = new RecordingLogger();

        HostingLog.LogComposed(
            logger,
            ["hosting", "coordination"],
            [SectionSchema.Create("hosting", "Iskender.Hosting", 1), SectionSchema.Create("coordination", "Iskender.Infrastructure", 1)],
            "production",
            new ConfigurationRevision("cfg-1a2b"));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal((6001, "IskenderComposed", LogLevel.Information), (entry.Id.Id, entry.Id.Name, entry.Level));
        Assert.Equal("Iskender composed: modules coordination,hosting, sections coordination@1,hosting@1, environment production, revision cfg-1a2b.", entry.Message);
    }

    [Fact]
    public void Event6001WritesNoneForAnEnvironmentWithNoName()
    {
        var logger = new RecordingLogger();

        HostingLog.LogComposed(logger, ["hosting"], [SectionSchema.Create("hosting", "Iskender.Hosting", 1)], null, new ConfigurationRevision("r1"));

        Assert.Equal("Iskender composed: modules hosting, sections hosting@1, environment (none), revision r1.", Assert.Single(logger.Entries).Message);
    }

    [Fact]
    public void Event6002NamesTheErrorCount()
    {
        var logger = new RecordingLogger();

        HostingLog.LogStartRefused(logger, 3);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal((6002, "IskenderStartRefused", LogLevel.Error), (entry.Id.Id, entry.Id.Name, entry.Level));
        Assert.Equal("Iskender start refused: configuration rejected with 3 error(s).", entry.Message);
    }

    [Fact]
    public void Event6003NamesWhatIsMissingAndWhatRequiresIt()
    {
        var logger = new RecordingLogger();

        HostingLog.LogRegistrationMissing(logger, "hosting", "coordination");
        HostingLog.LogRegistrationMissing(logger, HostingLog.CoordinatorMissing(CoordinatorKind.Redis, CoordinationPrimitive.Leases), "configuration");

        Assert.All(logger.Entries, entry => Assert.Equal((6003, "IskenderRegistrationMissing", LogLevel.Error), (entry.Id.Id, entry.Id.Name, entry.Level)));
        Assert.Equal(
            ["Iskender registration missing: hosting required by coordination.", "Iskender registration missing: redis coordinator for leases required by configuration."],
            logger.Entries.Select(entry => entry.Message));
    }

    [Fact]
    public void TheCoordinatorTextUsesEachKindAndPrimitiveCode()
    {
        Assert.Equal("postgresql coordinator for atomic_operations", HostingLog.CoordinatorMissing(CoordinatorKind.PostgreSql, CoordinationPrimitive.AtomicOperations));
        Assert.Equal("redis coordinator for cache_invalidation", HostingLog.CoordinatorMissing(CoordinatorKind.Redis, CoordinationPrimitive.CacheInvalidation));
        Assert.Equal("postgresql coordinator for fencing", HostingLog.CoordinatorMissing(CoordinatorKind.PostgreSql, CoordinationPrimitive.Fencing));
    }

    [Fact]
    public void TheCategoryIsIskenderHosting()
    {
        Assert.Equal("Iskender.Hosting", HostingLog.Category);
    }

    [Fact]
    public async Task InfrastructuresEvent6003RendersExactlyAsHostingsDefinition()
    {
        var ct = TestContext.Current.CancellationToken;
        var hosting = new RecordingLogger();
        HostingLog.LogRegistrationMissing(hosting, "hosting", "coordination");
        await using var host = await EmbeddedHost.CreateAsync(
            builder => CompositionRegistration.Add(builder.Services, new CompositionModule("coordination", [], ["hosting"])),
            start: false,
            ct: ct);

        var failure = await Assert.ThrowsAsync<GatewayException>(() => host.StartAsync(ct));

        Assert.Same(ErrorCodes.HostingRegistrationMissing, failure.Code);
        var expected = Assert.Single(hosting.Entries);
        var written = host.Logs.Only(6003);
        Assert.Equal(HostingLog.Category, written.Category);
        Assert.Equal((expected.Id.Id, expected.Id.Name, expected.Level, expected.Message), (written.Event.Id, written.Event.Name, written.Level, written.Message));
    }

    private sealed record Entry(EventId Id, LogLevel Level, string Message);

    private sealed class RecordingLogger : ILogger
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new(eventId, logLevel, formatter(state, exception)));
    }
}
