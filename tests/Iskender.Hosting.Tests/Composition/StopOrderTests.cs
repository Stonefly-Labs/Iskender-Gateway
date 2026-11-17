using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Iskender.Domain.Ports;
using Iskender.Hosting.Tests.Fixtures;
using Iskender.Infrastructure.Coordination;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iskender.Hosting.Tests.Composition;

/// <summary>
/// The composition start-ups stop after the server has drained, so a request in flight finishes before the instance
/// lease is released (hosting contract, Start-up rule 8; research R5). The host runs on Kestrel, whose stop waits for
/// requests in flight, rather than on the in-memory test server.
/// </summary>
public class StopOrderTests
{
    private const string Document = """{"iskender":{"coordination":{"topology":"single_instance","leases":{"coordinator":"postgresql"}}}}""";

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARequestHeldAcrossStopKeepsTheLeaseUntilItFinishes()
    {
        var journal = new ConcurrentQueue<string>();
        var leases = new RecordingLeases(journal);
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().UseUrls("http://127.0.0.1:0");
        builder.Services.AddRoutingCore();
        builder.Services.AddLogging(logging => logging.ClearProviders());
        builder.Services.AddIskender(options => options.BaseDocument = IskenderConfigurationDocument.FromText("base.json", IskenderDocumentFormat.Json, Document));
        builder.Services.AddIskenderCoordination();
        leases.Add(builder.Services);
        await using var app = builder.Build();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.UseRouting();
        app.MapGet("/hold", async () =>
        {
            entered.SetResult();
            await release.Task;
            journal.Enqueue("request finished");
            return "done";
        });
        await app.StartAsync(Token);
        Assert.Equal(SingleInstanceState.Held, app.Services.GetRequiredService<ISingleInstanceStatus>().State);
        var address = new Uri(Assert.Single(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses));
        using var client = new HttpClient { BaseAddress = address };

        var request = client.GetStringAsync("/hold", Token);
        await entered.Task.WaitAsync(Bound, Token);
        var stopping = app.StopAsync(CancellationToken.None);
        await ListenerClosedAsync(address);

        // The server has stopped listening and is draining; every hosted service's StopAsync has run or is running.
        Assert.Equal(0, leases.Releases);
        Assert.False(stopping.IsCompleted);
        release.SetResult();
        Assert.Equal("done", await request.WaitAsync(Bound, Token));
        await stopping.WaitAsync(Bound, Token);

        Assert.Equal(["acquire instance.data_plane", "request finished", "release instance.data_plane"], journal);
    }

    // Polls until a new connection to the listener is refused, which Kestrel does once its stop has begun.
    private static async Task ListenerClosedAsync(Uri address)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < Bound)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(address.Host, address.Port, Token);
            }
            catch (SocketException)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), Token);
        }

        Assert.Fail("The listener did not close.");
    }
}
