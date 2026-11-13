using System.Text.Json;
using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;
using Iskender.Infrastructure.Coordination;

namespace Iskender.Infrastructure.Redis.Tests.Fixture;

/// <summary>
/// Builds the <c>iskender.coordination</c> section for the fixture, as a configuration document validated by ISK-0003,
/// with every primitive on <c>redis</c> and <c>tlsMode: disable</c> in the <c>development</c> environment.
/// </summary>
internal static class TestCoordinationSettings
{
    public static SecretReference PasswordSecret { get; } = new("test/redis");

    /// <summary>The section's JSON document, with any extra fields replacing the defaults written here.</summary>
    public static string Document(RedisFixture fixture, IReadOnlyDictionary<string, object>? redisOverrides = null, IReadOnlyDictionary<string, object>? sectionOverrides = null)
    {
        var redis = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["endpoints"] = new[] { fixture.Endpoint },
            ["username"] = fixture.Username,
            ["password"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["secretRef"] = PasswordSecret.Name },
            ["tlsMode"] = "disable",
        };
        foreach (var (key, value) in redisOverrides ?? new Dictionary<string, object>())
        {
            redis[key] = value;
        }

        var section = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["atomic"] = new Dictionary<string, object> { ["coordinator"] = "redis" },
            ["leases"] = new Dictionary<string, object> { ["coordinator"] = "redis" },
            ["cache"] = new Dictionary<string, object> { ["coordinator"] = "redis" },
            ["redis"] = redis,
        };
        foreach (var (key, value) in sectionOverrides ?? new Dictionary<string, object>())
        {
            section[key] = value;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object> { ["iskender"] = new Dictionary<string, object> { ["coordination"] = section } });
    }

    /// <summary>Validates <paramref name="document"/> against the coordination section alone, under ISK-0003.</summary>
    public static ConfigurationValidationReport Validate(string document, string? environment = "development") =>
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            ConfigurationSchemaRegistry.Create([CoordinationSectionSchema.Schema]),
            ConfigurationDocumentSource.FromString("coordination.json", ConfigurationDocumentFormat.Json, document),
            environmentName: environment));

    /// <summary>Validated settings read from <paramref name="document"/>, which must be valid.</summary>
    public static CoordinationSettings Read(string document, string? environment = "development")
    {
        var report = Validate(document, environment);
        Assert.True(report.IsValid, report.ToString());
        return CoordinationSettings.From(report.Snapshot!);
    }

    /// <summary>Settings for the fixture in the <c>development</c> environment.</summary>
    public static CoordinationSettings For(RedisFixture fixture, IReadOnlyDictionary<string, object>? redisOverrides = null, IReadOnlyDictionary<string, object>? sectionOverrides = null) =>
        Read(Document(fixture, redisOverrides, sectionOverrides));
}
