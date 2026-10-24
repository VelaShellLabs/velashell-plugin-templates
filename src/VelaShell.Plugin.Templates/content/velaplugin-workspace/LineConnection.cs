using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using VelaShell.PluginSdk.Logging;
using VelaShell.PluginSdk.Protocols;

namespace VelaPlugin1;

/// <summary>
/// 到服务端的一条连接:TCP 之上按行收发的文本协议。
/// <para>
/// ✏️ 这是整个模板里**最该被换掉的一个类** —— 换成你的客户端库(数据库驱动、消息队列客户端……)
/// 即可。保留它对外的三样东西:<see cref="ConnectAsync" /> 在返回前连上(失败翻成 SDK 的异常族)、
/// 后台事件(<see cref="LineReceived" /> / <see cref="Closed" />,**任意线程**触发)、能收尾的
/// <see cref="DisposeAsync" />。第三方库的异常类型不要越过插件边界:出口一律翻成
/// <c>Protocol*Exception</c>(Redis 插件对 StackExchange.Redis、S3 插件对 AWSSDK 都是这么做的)。
/// </para>
/// </summary>
internal sealed class LineConnection : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly string _lineEnding;
    private readonly IPluginLogger _log;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private Task? _pump;
    private int _closed;

    private LineConnection(TcpClient client, string lineEnding, TimeSpan connectTime, IPluginLogger log)
    {
        _client = client;
        _stream = client.GetStream();
        _lineEnding = lineEnding;
        ConnectTime = connectTime;
        _log = log;
    }

    /// <summary>收到一行(不含行尾)。在读泵线程上触发。</summary>
    public event Action<string>? LineReceived;

    /// <summary>连接被对端关闭或网络断开(本地 <see cref="DisposeAsync" /> 不触发)。</summary>
    public event Action? Closed;

    /// <summary>TCP 握手耗时,报给宿主当延迟显示。</summary>
    public TimeSpan ConnectTime { get; }

    /// <summary>连接是否仍可用。</summary>
    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    /// <summary>建连。连不上、超时都翻成 <see cref="ProtocolConnectionException" />;取消原样抛出。</summary>
    /// <param name="host">主机(走隧道时是宿主给的本地转发端点)。</param>
    /// <param name="port">端口。</param>
    /// <param name="settings">连接设置。</param>
    /// <param name="loc">文案表(错误信息)。</param>
    /// <param name="log">插件日志。</param>
    /// <param name="displayEndpoint">写进错误信息的端点(走隧道时是真实目标,而不是 127.0.0.1)。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已连上的连接。</returns>
    public static async Task<LineConnection> ConnectAsync(
        string host,
        int port,
        ConnectionSettings settings,
        Loc loc,
        IPluginLogger log,
        string displayEndpoint,
        CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.ConnectTimeout);
        var watch = Stopwatch.StartNew();
        try
        {
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new ProtocolConnectionException(
                loc.Format("Error_Timeout", displayEndpoint, (int)settings.ConnectTimeout.TotalSeconds));
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            client.Dispose();
            throw new ProtocolConnectionException(loc.Format("Error_Connect", displayEndpoint, ex.Message), ex);
        }
        // ✏️ 协议有握手 / 登录就在这里做完。认证失败要单独认出来并抛 ProtocolAuthenticationException ——
        //    宿主看到它才会重弹登录框,"连不上"对"密码打错了"是最无用的反馈。

        var connection = new LineConnection(client, settings.LineEnding, watch.Elapsed, log);
        connection._pump = Task.Run(connection.PumpAsync, CancellationToken.None);
        return connection;
    }

    /// <summary>发一行。写侧串行化:两次发送交织会把两行拼成一行。</summary>
    /// <param name="line">要发的行(不含行尾)。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    /// <exception cref="IOException">连接已断开。</exception>
    public async Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        if (!IsOpen)
        {
            throw new IOException("The connection is closed.");
        }
        byte[] payload = Encoding.UTF8.GetBytes(line + _lineEnding);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            throw new IOException(ex.Message, ex);
        }
        finally
        {
            _ = _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0 && _pump is null)
        {
            return;
        }
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _client.Close(); // 唤醒阻塞在读上的泵。
        if (Interlocked.Exchange(ref _pump, null) is { } pump)
        {
            try
            {
                await pump.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _log.Debug("The read pump did not finish within the shutdown timeout.");
            }
        }
        _stream.Dispose();
        _client.Dispose();
        _lifetime.Dispose();
    }

    /// <summary>读泵:逐行读,交给 <see cref="LineReceived" />;读到头或出错即视为对端断开。</summary>
    private async Task PumpAsync()
    {
        try
        {
            using var reader = new StreamReader(_stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            while (!_lifetime.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(_lifetime.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break; // 对端关闭。
                }
                try
                {
                    LineReceived?.Invoke(line);
                }
                catch (Exception ex)
                {
                    // 订阅方自爆不该把读泵带走。
                    _log.Error("A LineReceived handler threw.", ex);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException
                                       or ObjectDisposedException or SocketException)
        {
            // 正常收尾路径:本地关闭、对端 RST、网络断开。
        }
        catch (Exception ex)
        {
            _log.Warn($"The read pump stopped: {ex.Message}", ex);
        }

        // 只有"不是我们自己关的"才算断开:本地 Dispose 已经先把 _closed 置位。
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            try
            {
                Closed?.Invoke();
            }
            catch (Exception ex)
            {
                _log.Error("A Closed handler threw.", ex);
            }
        }
    }
}
