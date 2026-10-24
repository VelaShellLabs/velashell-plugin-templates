using System.Text;
using VelaShell.PluginSdk.Protocols;
using VelaShell.PluginSdk.Testing;

namespace VelaPlugin1.Tests;

/// <summary>
/// 会话的四条契约:字节来回、连不上要抛对的异常、掉线是 EOF 而不是异常、关闭能唤醒挂着的读。
/// 走的是注册进替身的那个 <see cref="IProtocolTerminal" />,与宿主拿到的是同一条路径。
/// </summary>
[TestClass]
public sealed class ProtocolSessionTests
{
    private const string PluginId = "PLUGIN_ID";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly ProtocolTerminalOptions Options = new("xterm-256color", 80, 24);

    private static async Task<IProtocolTerminal> ActivateAsync(TestPluginContext context)
    {
        await new VelaPlugin1Plugin().ActivateAsync(context, CancellationToken.None);
        return context.RecordingProtocols.GetTerminal(context.PluginId)!;
    }

    private static ProtocolConnectRequest Request(string host, int port, string enterMode = "crlf") =>
        new()
        {
            Host = host,
            Port = port,
            Settings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ConnectionSettings.KeyEnterMode] = enterMode,
                [ConnectionSettings.KeyConnectTimeout] = "5",
            },
        };

    /// <summary>一直读,直到攒够 <paramref name="expected" /> 那么多字节或会话结束。</summary>
    private static async Task<string> ReadAtLeastAsync(IProtocolTerminalSession session, int expected)
    {
        var received = new List<byte>();
        byte[] buffer = new byte[256];
        using var timeout = new CancellationTokenSource(Patience);
        while (received.Count < expected)
        {
            int read = await session.ReadAsync(buffer, timeout.Token);
            if (read == 0)
            {
                break;
            }
            received.AddRange(buffer.AsSpan(0, read));
        }
        return Encoding.ASCII.GetString([.. received]);
    }

    [TestMethod]
    public async Task Write_IsEchoedBack_WithEnterRewrittenToCrLf()
    {
        await using var server = new LoopbackEchoServer();
        using var context = new TestPluginContext { PluginId = PluginId };
        IProtocolTerminal terminal = await ActivateAsync(context);

        await using IProtocolTerminalSession session =
            await terminal.ConnectAsync(Request("127.0.0.1", server.Port), Options, CancellationToken.None);
        // 终端按下回车给的是 CR;CR LF 模式下线上发的是 CR LF,回显回来的也就是它。
        await session.WriteAsync("hello\r"u8.ToArray(), CancellationToken.None);

        string echoed = await ReadAtLeastAsync(session, "hello\r\n".Length);
        Assert.AreEqual("hello\r\n", echoed);
    }

    [TestMethod]
    public async Task Connect_WhenNothingListens_ThrowsAConnectionExceptionNamingTheEndpoint()
    {
        int deadPort = LoopbackEchoServer.FreePort();
        using var context = new TestPluginContext { PluginId = PluginId };
        IProtocolTerminal terminal = await ActivateAsync(context);

        ProtocolConnectionException failure = await Assert.ThrowsExactlyAsync<ProtocolConnectionException>(
            () => terminal.ConnectAsync(Request("127.0.0.1", deadPort), Options, CancellationToken.None));

        // 用户同时开着好几条会话时,"连不上"必须说清是哪一条。
        Assert.Contains($"127.0.0.1:{deadPort}", failure.Message);
    }

    [TestMethod]
    public async Task Connect_WithoutAHost_IsRejectedBeforeTouchingTheNetwork()
    {
        using var context = new TestPluginContext { PluginId = PluginId };
        IProtocolTerminal terminal = await ActivateAsync(context);

        await Assert.ThrowsExactlyAsync<ProtocolConnectionException>(
            () => terminal.ConnectAsync(Request("  ", 7007), Options, CancellationToken.None));
    }

    [TestMethod]
    public async Task ServerHangingUp_EndsTheSessionWithEof_NotAnException()
    {
        await using var server = new LoopbackEchoServer();
        using var context = new TestPluginContext { PluginId = PluginId };
        IProtocolTerminal terminal = await ActivateAsync(context);
        await using IProtocolTerminalSession session =
            await terminal.ConnectAsync(Request("127.0.0.1", server.Port), Options, CancellationToken.None);
        // 先确认连接真的接上了,再让对端断开。
        await session.WriteAsync("x"u8.ToArray(), CancellationToken.None);
        string first = await ReadAtLeastAsync(session, 1);
        Assert.AreEqual("x", first);

        server.DropClients();

        // 返回 0 才会走到宿主"标签置为已断开、按 Enter 即重连"那条路上。
        using var timeout = new CancellationTokenSource(Patience);
        int read = await session.ReadAsync(new byte[16], timeout.Token);
        Assert.AreEqual(0, read);
    }

    [TestMethod]
    public async Task Dispose_WakesAPendingRead()
    {
        await using var server = new LoopbackEchoServer();
        using var context = new TestPluginContext { PluginId = PluginId };
        IProtocolTerminal terminal = await ActivateAsync(context);
        IProtocolTerminalSession session =
            await terminal.ConnectAsync(Request("127.0.0.1", server.Port), Options, CancellationToken.None);
        ValueTask<int> pending = session.ReadAsync(new byte[16], CancellationToken.None);

        await session.DisposeAsync();

        // 宿主关标签时就是这个顺序:读还挂着,会话被释放。读必须醒来并报 EOF,而不是永远挂住。
        int read = await pending.AsTask().WaitAsync(Patience);
        Assert.AreEqual(0, read);
    }

    [TestMethod]
    public void Encode_TreatsCrLfAsASingleEnter()
    {
        // 粘贴进来的 Windows 文本自带 CR LF:那是一个回车,不该变成 CR LF LF。
        byte[] encoded = ProtocolSession.Encode("a\r\nb\r"u8, EnterMode.CrLf);

        Assert.AreEqual("a\r\nb\r\n", Encoding.ASCII.GetString(encoded));
    }
}
