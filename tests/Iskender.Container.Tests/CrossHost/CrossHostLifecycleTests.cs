using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Iskender.Container.Tests.Fixture;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Context;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;
using Iskender.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Iskender.Container.Tests.CrossHost;

/// <summary>
/// User Story 5 and SC-008, per research R18 and container contract Test fixtures rule 3: the data-plane container, not
/// ready with no credential provider, and the hosting sample answer the same requests under Iskender's routes alike, and
/// the container's configuration revision is the one an embedded host computes from the same registrations and documents.
/// </summary>
[Trait("Category", "CrossHost")]
public partial class CrossHostLifecycleTests(ImageBuild build)
{
    private const string Scenario = "database-running";

    private const string Header = "X-Correlation-ID";

    private static readonly HttpMethod[] Methods = [HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete, HttpMethod.Head, HttpMethod.Options];

    private static readonly string[] Paths = ["", "x", "v1/anything", "v1/chat/completions", "health/live", "a/b/c?query=1"];

    // No header, a valid caller value, an empty value, one with a space, and one of 129 characters.
    private static readonly string?[] CallerValues = [null, "caller-1.a_b:c", "", "has space", new string('x', 129)];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BothHostsAnswerEveryRequestAlikeAndTheRevisionsMatch()
    {
        await RequireDocker.ThatAsync();
        await using var project = await ComposeProject.CreateAsync(await build.GetAsync());
        var service = Scenarios.Service(Scenario, "data-plane");
        await project.UpAsync(service);
        await project.WhenAsync(service, "/health/ready", (status, body) => Scenarios.HasReasons(Scenario, status, body), TimeSpan.FromMinutes(2));
        using var container = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{await project.PortAsync(service, ComposeProject.TrafficPort)}/") };
        await using var sample = await EmbeddedHost.FromSampleAsync(ct: Token);
        using var embedded = sample.Client;

        var compared = 0;
        foreach (var method in Methods)
        {
            foreach (var path in Paths)
            {
                foreach (var caller in CallerValues)
                {
                    var fromContainer = await SendAsync(container, method, path, caller);
                    var fromSample = await SendAsync(embedded, method, "iskender/" + path, caller);

                    Assert.True(fromSample == fromContainer, $"{method} /{path} with {caller ?? "no header"}: container {fromContainer}, sample {fromSample}");
                    Assert.True(fromContainer.Body.Length == 0 || fromContainer.CorrelationIsGenerated, $"{method} /{path}: {fromContainer.Body}");
                    compared++;
                }
            }
        }

        Assert.Equal(Methods.Length * Paths.Length * CallerValues.Length, compared);
        TestContext.Current.SendDiagnosticMessage($"Compared {compared} requests.");

        var logs = await project.LogsAsync(service);
        Assert.Equal(await EmbeddedRevisionAsync(), ComposedRevision(logs));
    }

    // The revision an embedded host composed with the data plane's registrations computes from the scenario's document.
    private static async Task<string> EmbeddedRevisionAsync()
    {
        var document = Path.Combine(await ImageBuild.RepositoryRootAsync(), "tests", "Iskender.Container.Tests", "compose", "scenarios", Scenario, "iskender.yaml");
        var sources = new CompositionSources(CompositionDocument.FromFile(document, ConfigurationDocumentFormat.Yaml, "iskender.yaml"), null, false, "development");
        await using var host = await EmbeddedHost.CreateAsync(builder => PlaneDefinitions.DataPlane().Compose(builder.Services, sources), start: false, ct: Token);
        return host.Services.GetRequiredService<ConfigurationRevision>().Value;
    }

    private static string ComposedRevision(string logs)
    {
        var line = logs.Split('\n').Single(entry => entry.Contains("\"EventId\":6001", StringComparison.Ordinal));
        return JsonNode.Parse(line)!["State"]!["Revision"]!.GetValue<string>();
    }

    private static async Task<Answer> SendAsync(HttpClient client, HttpMethod method, string path, string? caller)
    {
        using var request = new HttpRequestMessage(method, path);
        if (caller is not null)
        {
            request.Headers.TryAddWithoutValidation(Header, caller);
        }

        using var response = await client.SendAsync(request, Token);

        // A HEAD response carries no body on the wire: Kestrel drops it, and the sample's in-memory test server does not.
        var body = method == HttpMethod.Head ? "" : await response.Content.ReadAsStringAsync(Token);
        var generated = body.Length > 0 && JsonNode.Parse(body)?["correlation_id"]?.GetValue<string>() is { } id && id.StartsWith("req_", StringComparison.Ordinal) && id != caller;
        return new Answer(response.StatusCode, response.Content.Headers.ContentType?.ToString(), RequestId().Replace(body, "req_*"), generated);
    }

    [GeneratedRegex("req_[0-9a-z]+")]
    private static partial Regex RequestId();

    private sealed record Answer(HttpStatusCode Status, string? ContentType, string Body, bool CorrelationIsGenerated);
}
