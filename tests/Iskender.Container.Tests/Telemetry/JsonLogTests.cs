using System.Text.Json;
using Iskender.Container.Tests.Fixture;
using Iskender.Container.Tests.Roots;
using Iskender.Infrastructure.Container;

namespace Iskender.Container.Tests.Telemetry;

/// <summary>
/// Container contract, Telemetry rule 3: the entry point's own logging writes one JSON object per line to standard output,
/// with no scope and no ASP.NET Core request log. It replaces <see cref="Console.Out"/>, so it runs in
/// <see cref="ProcessState"/>.
/// </summary>
[Collection(ProcessState.Name)]
public class JsonLogTests
{
    [Fact]
    public async Task EachLineIsOneJsonObjectWithNoScopeAndNoRequestLog()
    {
        using var host = new ContainerTestHost().WithDocument();
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            using var client = new HttpClient();
            var definition = TestPlanes.WithTraffic(host);
            var run = Task.Run(() => ContainerEntryPoint.RunAsync([], definition, host.Environment, host.Time, host.StopToken, loggerProvider: null, host.Files, host.StandardError));
            await ContainerTestHost.WhenListeningAsync(host.HealthPort, run);
            await host.WhenStartedAsync(client);
            using var response = await client.GetAsync($"http://127.0.0.1:{host.TrafficPort}/hello", TestContext.Current.CancellationToken);
            host.Stop();
            Assert.Equal(ExitCodes.Success, await run);
        }
        finally
        {
            Console.SetOut(original);
        }

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.NotEmpty(lines);
        var entries = lines.Select(line => JsonDocument.Parse(line)).ToList();
        try
        {
            Assert.All(entries, entry => Assert.Equal(JsonValueKind.Object, entry.RootElement.ValueKind));
            Assert.All(entries, entry => Assert.False(entry.RootElement.TryGetProperty("Scopes", out _)));
            var categories = entries.Select(entry => entry.RootElement.GetProperty("Category").GetString()!).ToList();
            Assert.DoesNotContain(categories, category => category.StartsWith("Microsoft.AspNetCore.Hosting", StringComparison.Ordinal));
            Assert.Contains("Iskender.Container", categories);
            Assert.Contains(entries, entry => entry.RootElement.GetProperty("EventId").GetInt32() == 7005);
        }
        finally
        {
            entries.ForEach(entry => entry.Dispose());
        }
    }
}
