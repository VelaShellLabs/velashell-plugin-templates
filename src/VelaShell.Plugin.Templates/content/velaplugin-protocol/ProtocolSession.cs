using System.Net.Sockets;
using System.Threading.Channels;
using VelaShell.PluginSdk.Logging;
using VelaShell.PluginSdk.Protocols;

namespace VelaPlugin1;

/// <summary>
/// 一条终端会话:TCP 之上的 <see cref="IProtocolTerminalSession" />,**只搬字节**。
/// <para>
/// VT 解析、回滚、搜索、会话日志、录制、ZMODEM 全在宿主侧 —— 插件多解释一层只会与宿主的
/// 终端引擎打架。插件要做的只是:把用户的按键变成线上的字节,把线上的字节交还给终端。
/// </para>
/// <para>
/// 结构是 Telnet / 串口插件用的那条**单出口管道**:后台读泵读传输 → 写进通道;
/// <see cref="ReadAsync" /> 只从通道取。本地回显也写同一个通道 —— 这样"对端不回显时用户
/// 仍看得见自己敲的字"不必等下一次网络数据到达(阻塞在套接字读上的读取方不会因为旁路缓冲而醒)。
/// </para>
/// <para>
/// ✏️ 换传输:把 <see cref="TcpClient" /> 换成你的(串口、WebSocket、某个库的流……),
/// 保留下面这几条纪律即可 —— 它们是宿主对终端会话的契约,不是本模板的口味:
/// </para>
/// <list type="number">
///   <item>掉线归一化成 EOF(<see cref="ReadAsync" /> 返回 0),**不要抛异常** —— 返回 0 才会走到
///     "标签置为已断开、按 Enter 即重连"那条路上。</item>
///   <item>写侧自己串行化:用户按键、尺寸上报、协商应答都是写,交织会把一帧撕成两半。
///     读侧由宿主独占,不会并发。</item>
///   <item><see cref="ResizeAsync" /> 是即发即忘:协议没有对应机制就实现成空操作,别抛。</item>
///   <item><see cref="DisposeAsync" /> 必须能唤醒挂着的读,且不能在关闭路径上无限阻塞。</item>
/// </list>
/// </summary>
internal sealed class ProtocolSession : IProtocolTerminalSession
{
    private const byte Cr = (byte)'\r';
    private const byte Lf = (byte)'\n';

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly ConnectionSettings _settings;
    private readonly IPluginLogger _log;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Channel<byte[]> _output = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private ReadOnlyMemory<byte> _carry;
    private Task? _pump;
    private volatile bool _disposed;

    private ProtocolSession(TcpClient client, ConnectionSettings settings, IPluginLogger log)
    {
        _client = client;
        _stream = client.GetStream();
        _settings = settings;
        _log = log;
    }

    /// <summary>建立连接并启动读泵。连不上、超时都翻成 <see cref="ProtocolConnectionException" />。</summary>
    /// <param name="host">主机名或 IP。</param>
    /// <param name="port">端口。</param>
    /// <param name="settings">连接设置。</param>
    /// <param name="loc">文案表(错误信息)。</param>
    /// <param name="log">插件日志。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已就绪的会话。</returns>
    public static async Task<ProtocolSession> ConnectAsync(
        string host,
        int port,
        ConnectionSettings settings,
        Loc loc,
        IPluginLogger log,
        CancellationToken cancellationToken)
    {
        string endpoint = $"{host}:{port}";
        // Nagle 会把单个按键攒到 40ms 后再发,交互式终端上是肉眼可见的迟滞。
        var client = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.ConnectTimeout);
        try
        {
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new ProtocolConnectionException(
                loc.Format("Error_Timeout", endpoint, (int)settings.ConnectTimeout.TotalSeconds));
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            client.Dispose();
            // 原始异常挂在 InnerException 上进日志;消息给人看,要说清是哪个端点。
            throw new ProtocolConnectionException(loc.Format("Error_Connect", endpoint, ex.Message), ex);
        }

        var session = new ProtocolSession(client, settings, log);
        session._pump = Task.Run(session.PumpAsync, CancellationToken.None);
        return session;
    }

    /// <inheritdoc />
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }
        if (_carry.IsEmpty)
        {
            try
            {
                _carry = await _output.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return 0; // 泵已收尾 = 会话结束(EOF)。
            }
        }
        int copied = Math.Min(buffer.Length, _carry.Length);
        _carry[..copied].CopyTo(buffer);
        _carry = _carry[copied..];
        return copied;
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed || data.IsEmpty)
        {
            return;
        }
        await SendAsync(Encode(data.Span, _settings.EnterMode), cancellationToken).ConfigureAwait(false);

        // 本地回显的是**用户原始输入**,不是改写后的线上字节;回车显示成换行,光标才会下移。
        if (_settings.LocalEcho)
        {
            _ = _output.Writer.TryWrite(Encode(data.Span, EnterMode.CrLf));
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 裸 TCP 没有"上报窗口尺寸"这回事,所以是空操作。✏️ 协议有对应机制(Telnet 的 NAWS、
    /// SSH 的 window-change)时在这里发,并且同样经 <see cref="SendAsync" /> 串行化。
    /// </remarks>
    public ValueTask ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            _client.Close(); // 唤醒阻塞在读上的泵。
        }
        catch (Exception ex)
        {
            _log.Debug($"Closing the socket threw: {ex.Message}");
        }
        if (_pump is { } pump)
        {
            // 泵只做套接字读与通道写,取消后很快返回;仍加超时兜底 —— 关标签不该卡在这里。
            // 用 WaitAsync 而不是 WhenAny(Task.Delay):后者即便泵先返回,定时器也会跑到点。
            try
            {
                await pump.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _log.Debug("The read pump did not finish within the shutdown timeout.");
            }
        }
        _ = _output.Writer.TryComplete(); // 挂着的 ReadAsync 因此返回 0。
        _stream.Dispose();
        _client.Dispose();
        _writeGate.Dispose();
        _lifetime.Dispose();
    }

    /// <summary>
    /// 用户输入 → 线上字节。终端按下回车给的是 CR;CR 后面紧跟 LF(粘贴进来的 Windows 文本)
    /// 算同一个回车,不重复改写。
    /// </summary>
    internal static byte[] Encode(ReadOnlySpan<byte> input, EnterMode mode)
    {
        if (mode == EnterMode.Cr || input.IndexOf(Cr) < 0)
        {
            return input.ToArray();
        }
        var output = new List<byte>(input.Length + 8);
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] != Cr)
            {
                output.Add(input[i]);
                continue;
            }
            if (i + 1 < input.Length && input[i + 1] == Lf)
            {
                i++;
            }
            if (mode == EnterMode.CrLf)
            {
                output.Add(Cr);
            }
            output.Add(Lf);
        }
        return [.. output];
    }

    private async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
    {
        if (payload.Length == 0)
        {
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            await _writeGate.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            return; // 会话正在关:这次写没有去处,也不该炸到宿主的输入队列里。
        }
        try
        {
            await _stream.WriteAsync(payload, linked.Token).ConfigureAwait(false);
            await _stream.FlushAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or OperationCanceledException)
        {
            // 连接已断:读侧的 EOF 才是权威结论,这里只丢弃这一次写。
            _log.Debug($"Write failed: {ex.Message}");
        }
        finally
        {
            try
            {
                _ = _writeGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Dispose 与写并发时信号量可能已释放。
            }
        }
    }

    /// <summary>
    /// 读泵:传输 → 通道。任何异常都归一化成"结束"(完成通道即 EOF)——
    /// 掉线不该以异常的形式冒到宿主的读循环里。
    /// </summary>
    private async Task PumpAsync()
    {
        byte[] buffer = new byte[16 * 1024];
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                int read = await _stream.ReadAsync(buffer, _lifetime.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break; // 对端关闭。
                }
                // ✏️ 协议有带内控制序列(Telnet 的 IAC 协商之类)就在这里剥掉,只把净数据交给终端。
                _ = _output.Writer.TryWrite(buffer.AsSpan(0, read).ToArray());
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException
                                       or ObjectDisposedException or SocketException)
        {
            // 正常收尾路径:关标签、对端 RST、网络断开。
        }
        catch (Exception ex)
        {
            _log.Warn($"The read pump stopped: {ex.Message}", ex);
        }
        finally
        {
            _ = _output.Writer.TryComplete();
        }
    }
}
