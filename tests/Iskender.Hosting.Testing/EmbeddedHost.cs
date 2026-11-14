using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Iskender.Hosting.Testing;

/// <summary>
/// An ASP.NET Core host built in process on a <see cref="TestServer"/>, with container validation on and every log
/// entry captured (research R17, hosting contract Test fixture rule 1). It starts no process and opens no port. Owned
/// by ISK-0006.
/// </summary>
public sealed class EmbeddedHost : IAsyncDisposable
{
    private readonly WebApplication _application;

    private EmbeddedHost(WebApplication application, CapturedLog logs)
    {
        _application = application;
        Logs = logs;
    }

    /// <summary>The host's container.</summary>
    public IServiceProvider Services => _application.Services;

    /// <summary>The host's server, always a <see cref="TestServer"/>.</summary>
    public TestServer Server => (TestServer)_application.Services.GetRequiredService<IServer>();

    /// <summary>A client of the test server. Its requests stay in process.</summary>
    public HttpClient Client => Server.CreateClient();

    /// <summary>Every entry the host's logging has written.</summary>
    public CapturedLog Logs { get; }

    /// <summary>
    /// Builds a host from the caller's registrations, with <c>UseTestServer()</c>, <c>ValidateScopes</c> and
    /// <c>ValidateOnBuild</c> on, and <see cref="CapturedLog"/> added to its logging. With <paramref name="start"/>, it is
    /// started, and a failed start throws the host's exception, such as a <c>GatewayException</c>, after disposing it.
    /// </summary>
    public static async Task<EmbeddedHost> CreateAsync(Action<WebApplicationBuilder> configure, bool start = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = WebApplication.CreateBuilder();
        var logs = Prepare(builder);
        configure(builder);
        return await Start(new EmbeddedHost(builder.Build(), logs), start, ct).ConfigureAwait(false);
    }

    public Task StartAsync(CancellationToken ct = default) => _application.StartAsync(ct);

    public Task StopAsync(CancellationToken ct = default) => _application.StopAsync(ct);

    public ValueTask DisposeAsync() => _application.DisposeAsync();

    /// <summary>Applies the test server, container validation, and the captured log to a builder.</summary>
    private static CapturedLog Prepare(WebApplicationBuilder builder)
    {
        var logs = new CapturedLog();
        builder.WebHost.UseTestServer();
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        builder.Logging.AddProvider(logs);
        return logs;
    }

    private static async Task<EmbeddedHost> Start(EmbeddedHost host, bool start, CancellationToken ct)
    {
        if (!start)
        {
            return host;
        }

        try
        {
            await host.StartAsync(ct).ConfigureAwait(false);
            return host;
        }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
