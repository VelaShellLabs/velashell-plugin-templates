using System.Net;
using System.Net.Sockets;

namespace VelaPlugin1.Tests;

/// <summary>
/// 测试用的回显服务:绑 127.0.0.1 的随机端口,收到什么发回什么。
/// <see cref="DropClients" /> 模拟"对端把连接断了"。
/// </summary>
internal sealed class LoopbackEchoServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<TcpClient> _clients = [];
    private readonly Lock _gate = new();
    private readonly Task _acceptLoop;

    public LoopbackEchoServer()
    {
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>监听端口。</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>关掉目前所有已接入的连接(监听照旧,之后还能再连)。</summary>
    public void DropClients()
    {
        lock (_gate)
        {
            foreach (TcpClient client in _clients)
            {
                client.Dispose();
            }
            _clients.Clear();
        }
    }

    /// <summary>取一个"此刻肯定没人监听"的本地端口:先绑一个随机端口再立刻放掉。</summary>
    public static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        DropClients();
        try
        {
            await _acceptLoop;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
            lock (_gate)
            {
                _clients.Add(client);
            }
            _ = Task.Run(() => EchoAsync(client));
        }
    }

    private static async Task EchoAsync(TcpClient client)
    {
        byte[] buffer = new byte[4096];
        try
        {
            NetworkStream stream = client.GetStream();
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
        {
            // 被 DropClients 关掉,或测试那一端先走了。
        }
        finally
        {
            client.Dispose();
        }
    }
}
