using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Protocols;
using VelaShell.PluginSdk.Workspaces;

namespace VelaPlugin1;

/// <summary>
/// 本连接类型的 <see cref="IWorkspaceProvider" />:宿主打开一条会话时调它,它连上服务器、交出一个文档。
/// <para>
/// 宿主递来的 <see cref="WorkspaceConnectRequest" /> 里是**一次性**凭据(只在这一刻交出去,不要落盘;
/// 插件自己要持久化的东西用 <c>context.Storage</c> / <c>context.Secrets</c>),
/// <c>Settings</c> 已按表单声明补齐默认值;走 SSH 隧道时 <c>Host</c>/<c>Port</c> 已是本地转发端点。
/// </para>
/// <para>
/// <b>错误上报的约定</b>(宿主据异常类型决定怎么呈现,所以类型比消息更重要):
/// </para>
/// <list type="bullet">
///   <item><see cref="ProtocolConnectionException" />:连不上 / 握手失败。消息会原样出现在提示框
///     与「测试」按钮的结果里 —— 写成用户照着能改配置的话,带上是哪个端点。</item>
///   <item><see cref="ProtocolAuthenticationException" />:凭据不对,宿主重弹登录框。</item>
///   <item><see cref="ProtocolCertificateTrustException" />:TLS 证书未受信任,宿主弹信任提示、记指纹后重连
///     (要配合 <see cref="WorkspaceFeatures.CertificateTrust" /> 与一个 <c>IsHidden</c> 的指纹字段)。</item>
///   <item><see cref="ProtocolUnsupportedException" />:"这个操作在此不适用",不是失败。</item>
/// </list>
/// <para>
/// **<see cref="OpenAsync" /> 必须能被反复调用且不留副作用**:连接页的「测试」按钮就是
/// 真开一次再立刻 <c>DisposeAsync</c>。所以这里只建连,不写任何持久化状态。
/// </para>
/// </summary>
/// <param name="context">插件上下文。</param>
/// <param name="loc">文案表(语言切换时由入口就地替换)。</param>
internal sealed class WorkspaceProvider(IPluginContext context, Loc loc) : IWorkspaceProvider
{
    /// <summary>当前文案表。语言切换时由 <see cref="VelaPlugin1Plugin" /> 就地替换。</summary>
    public Loc Loc { get; set; } = loc;

    /// <inheritdoc />
    public async Task<IWorkspaceDocument> OpenAsync(
        WorkspaceConnectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Loc loc = Loc;
        if (string.IsNullOrWhiteSpace(request.Host))
        {
            throw new ProtocolConnectionException(loc["Error_NoHost"]);
        }
        ConnectionSettings settings = ConnectionSettings.From(request);
        string endpoint = WorkspaceDocument.DescribeEndpoint(request);

        // **须在返回前完成首次连接**:宿主据此把失败原因呈现在连接流程里,而不是开出一个空面板。
        LineConnection connection = await LineConnection.ConnectAsync(
            request.Host.Trim(), request.Port, settings, loc, context.Log, endpoint, cancellationToken)
            .ConfigureAwait(false);
        context.Log.Info($"Connected to {endpoint} in {connection.ConnectTime.TotalMilliseconds:0} ms.");
        return new WorkspaceDocument(connection, request, settings, loc, context);
    }
}
