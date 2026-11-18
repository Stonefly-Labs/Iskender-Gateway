using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Iskender.Work.Tests.Fixture;

/// <summary>
/// A loopback TCP forwarder to the fixture's server, for one test's connections (ISK-0009 T035, T038). While
/// <see cref="Down"/>, it resets every forwarded connection and closes each new one at once, as an unreachable database
/// looks to the driver, and the fixture's own connections are untouched. With <see cref="Pause"/> set, it holds each
/// message from the driver for the pause's result before forwarding it, which puts a pause before each statement.
/// </summary>
internal sealed class OutageProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string _host;
    private readonly int _port;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<Socket, byte> _open = new();
    private readonly Task _accepting;
    private volatile bool _down;

    public OutageProxy(string host, int port)
    {
        _host = host;
        _port = port;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>When set, how long to hold each message from the driver before forwarding it.</summary>
    public Func<TimeSpan>? Pause { get; set; }

    /// <summary>Whether the database is unreachable through the proxy.</summary>
    public bool Down
    {
        get => _down;
        set
        {
            _down = value;
            if (value)
            {
                foreach (var socket in _open.Keys)
                {
                    Reset(socket);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        foreach (var socket in _open.Keys)
        {
            Reset(socket);
        }

        await _accepting.ContinueWith(_ => { }, TaskScheduler.Default);
        _stop.Dispose();
    }

    private static void Reset(Socket socket)
    {
        try
        {
            socket.LingerState = new LingerOption(true, 0);
            socket.Close();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = await _listener.AcceptSocketAsync(_stop.Token);
            if (_down)
            {
                Reset(client);
                continue;
            }

            _ = ForwardAsync(client);
        }
    }

    private async Task ForwardAsync(Socket client)
    {
        var server = new Socket(SocketType.Stream, ProtocolType.Tcp);
        _open[client] = 0;
        _open[server] = 0;
        try
        {
            await server.ConnectAsync(_host, _port, _stop.Token);
            await Task.WhenAny(PumpAsync(client, server, paused: true), PumpAsync(server, client, paused: false));
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            _open.TryRemove(client, out _);
            _open.TryRemove(server, out _);
            Reset(client);
            Reset(server);
        }
    }

    private async Task PumpAsync(Socket from, Socket to, bool paused)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                var read = await from.ReceiveAsync(buffer, SocketFlags.None, _stop.Token);
                if (read == 0)
                {
                    return;
                }

                if (paused && Pause?.Invoke() is { } pause && pause > TimeSpan.Zero)
                {
                    await Task.Delay(pause, _stop.Token);
                }

                await to.SendAsync(buffer.AsMemory(0, read), SocketFlags.None, _stop.Token);
            }
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }
}
