using Iskender.Core.Contracts.Errors;
using Iskender.Hosting.Testing;
using Iskender.Infrastructure.Coordination;
using Iskender.Infrastructure.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Hosting.Tests.Acceptance;

/// <summary>
/// SC-005, research R16: canaries planted as a configuration value, a secret reference name, a database host, and a
/// request route value never appear in an exception, a diagnostic, an Iskender log entry, or a response, across a
/// successful start, a rejected start, a missing coordinator, an unreadable document, and requests under and outside
/// the prefix.
/// </summary>
public class CanaryTests
{
    private const string ConfigurationValue = "canarydatabasevalue";

    private const string SecretReference = "canaryvault/canarysecretname";

    private const string DatabaseHost = "canary-database-host.invalid";

    private const string RouteValue = "canaryroutevalue";

    private static readonly string[] Canaries = [ConfigurationValue, SecretReference, DatabaseHost, RouteValue];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NoCanaryAppearsInAnyExceptionDiagnosticLogEntryOrResponse()
    {
        var observed = new List<string>();

        // A successful start, and requests under and outside the prefix.
        var logs = new CapturedLog();
        await using (var host = await EmbeddedHost.CreateAsync(builder => Compose(builder, logs, Document()), application => application.MapIskender("/iskender"), ct: Token))
        {
            foreach (var path in new[] { $"/iskender/{RouteValue}", $"/iskender/{RouteValue}/{RouteValue}?{RouteValue}={RouteValue}", $"/{RouteValue}" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                using var response = await host.Client.SendAsync(request, Token);
                observed.Add(((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
                observed.AddRange(response.Headers.Concat(response.Content.Headers).Select(header => $"{header.Key}: {string.Join(',', header.Value)}"));
                observed.Add(await response.Content.ReadAsStringAsync(Token));
            }
        }

        Assert.Single(logs.ById(6001));
        observed.AddRange(Entries(logs));

        // The control: the requests did carry the route canary, which the host's own request logging records.
        Assert.Contains(logs.Entries, entry => !entry.Category.StartsWith("Iskender", StringComparison.Ordinal) && entry.Message.Contains(RouteValue, StringComparison.Ordinal));

        // A rejected start, a missing coordinator, and an unreadable document, each with the canaries in its configuration.
        observed.AddRange(await FailureAsync(Document(requestTimeout: "0s"), ErrorCodes.HostingConfigurationRejected));
        observed.AddRange(await FailureAsync(Document(leases: "redis"), ErrorCodes.HostingRegistrationMissing));
        observed.AddRange(await FailureAsync(
            Document(),
            ErrorCodes.HostingConfigurationRejected,
            overlay: IskenderConfigurationDocument.FromFile(Path.Combine(Path.GetTempPath(), "iskender-missing-" + Guid.NewGuid().ToString("N"), "overlay.json"), IskenderDocumentFormat.Json, "overlay")));

        Assert.NotEmpty(observed);
        foreach (var canary in Canaries)
        {
            var found = observed.Where(text => text.Contains(canary, StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.True(found.Count == 0, $"{canary} appeared {found.Count} time(s): {string.Join(" | ", found)}");
        }
    }

    // The canaries as the database name, the password's secret reference, and the database host, in a valid document
    // unless a test changes the request timeout or the lease coordinator.
    private static string Document(string requestTimeout = "20s", string leases = "local") =>
        "{\"iskender\":{\"hosting\":{\"requestTimeout\":\"" + requestTimeout + "\"},"
        + "\"persistence\":{\"host\":\"" + DatabaseHost + "\",\"database\":\"" + ConfigurationValue + "\",\"username\":\"gateway\",\"password\":{\"secretRef\":\"" + SecretReference + "\"}},"
        + "\"coordination\":{\"leases\":{\"coordinator\":\"" + leases + "\"}}}}";

    private static void Compose(WebApplicationBuilder builder, CapturedLog logs, string document, IskenderConfigurationDocument? overlay = null)
    {
        builder.Logging.AddProvider(logs);
        builder.Services.AddIskender(options =>
        {
            options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, document);
            options.OverlayDocument = overlay;
        });
        builder.Services.AddIskenderCoordination();
        builder.Services.AddIskenderPostgreSqlPersistence();
    }

    // Everything a failed start exposes: the exception and its inner exceptions, its diagnostics, and Iskender's log entries.
    private static async Task<List<string>> FailureAsync(string document, ErrorCode expected, IskenderConfigurationDocument? overlay = null)
    {
        var logs = new CapturedLog();

        var failure = await Assert.ThrowsAsync<GatewayException>(() => EmbeddedHost.CreateAsync(builder => Compose(builder, logs, document, overlay), ct: Token));

        Assert.Same(expected, failure.Code);
        var observed = new List<string> { failure.ToString() };
        for (Exception? inner = failure; inner is not null; inner = inner.InnerException)
        {
            observed.Add(inner.Message);
        }

        if (failure.Error?.Diagnostics is { } diagnostics)
        {
            observed.Add(diagnostics.Text ?? string.Empty);
            observed.AddRange(diagnostics.Values.Select(pair => $"{pair.Key}={pair.Value}"));
        }

        observed.AddRange(Entries(logs));
        return observed;
    }

    // Iskender's log entries, each as its message, and each structured property as name and value. The host's own
    // request logging, which names the path it was asked for, is the host's, not Iskender's.
    private static IEnumerable<string> Entries(CapturedLog logs) =>
        logs.Entries
            .Where(entry => entry.Category.StartsWith("Iskender", StringComparison.Ordinal))
            .SelectMany(entry => new[] { entry.Message }.Concat(entry.Values.Select(pair => $"{pair.Key}={pair.Value}")));
}
