using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Protocols;

namespace VelaPlugin1;

/// <summary>
/// 本协议的 <see cref="IProtocolTerminal" /> 实现:把宿主递来的连接请求翻成设置,建一条会话交回去。
/// <para>
/// 会话的生命周期归终端标签:宿主拿到 <see cref="IProtocolTerminalSession" /> 之后自己读、自己写、
/// 关标签时自己 <c>DisposeAsync</c>。终端协议没有 SessionId,插件这边一般不需要会话表
/// (要在命令面板里对"当前会话"做带外动作时才需要,串口插件的 Break / DTR 就是这种)。
/// </para>
/// <para>
/// <b>错误上报的约定</b>(宿主据异常类型决定怎么呈现,所以类型比消息更重要):
/// </para>
/// <list type="bullet">
///   <item><see cref="ProtocolConnectionException" />:地址不对、端口不通、握手失败。宿主在标签页里
///     画失败覆盖层,消息原样给用户看 —— 写成用户照着能改配置的话,带上是哪个端点。</item>
///   <item><see cref="ProtocolAuthenticationException" />:凭据不对。宿主会重弹登录框 —— **只在确实是
///     凭据问题时**抛它,拿它兜底网络错误会让用户对着登录框反复无功。本模板声明了
///     <see cref="ProtocolFeatures.NoCredentials" />,没有这条路径;协议有协议级认证时去掉那一位,
///     并在握手里认出认证失败。</item>
///   <item>取消(<see cref="OperationCanceledException" />)原样抛,不要包装。</item>
///   <item>**连上之后**的掉线不走异常:<see cref="IProtocolTerminalSession.ReadAsync" /> 返回 0 即可
///     (见 <see cref="ProtocolSession" />)。</item>
/// </list>
/// </summary>
/// <param name="context">插件上下文(取日志)。</param>
/// <param name="loc">文案表(语言切换时由入口就地替换)。</param>
internal sealed class ProtocolTerminal(IPluginContext context, Loc loc) : IProtocolTerminal
{
    /// <summary>当前文案表。语言切换时由 <see cref="VelaPlugin1Plugin" /> 就地替换。</summary>
    public Loc Loc { get; set; } = loc;

    /// <inheritdoc />
    public async Task<IProtocolTerminalSession> ConnectAsync(
        ProtocolConnectRequest request,
        ProtocolTerminalOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Loc loc = Loc;
        string host = request.Host.Trim();
        if (host.Length == 0)
        {
            // "连都不用试"的判据在这里拦下,给一句能照着改的话 —— 否则用户看到的是
            // 套接字层的一句英文异常。
            throw new ProtocolConnectionException(loc["Error_NoHost"]);
        }
        ConnectionSettings settings = ConnectionSettings.From(request);

        // ✏️ 换成你的协议:握手、协商、登录提示……都在这一步做完再返回。
        //    "连上了但对端立刻拒绝"也应在这里暴露成异常,而不是交回一个永远不出字的会话。
        ProtocolSession session = await ProtocolSession.ConnectAsync(
            host, request.Port, settings, loc, context.Log, cancellationToken).ConfigureAwait(false);
        context.Log.Info($"Connected to {host}:{request.Port} ({options.Columns}x{options.Rows}, TERM={options.TerminalType}).");
        return session;
    }
}
