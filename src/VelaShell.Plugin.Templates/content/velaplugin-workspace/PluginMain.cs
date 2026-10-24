using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Workspaces;

namespace VelaPlugin1;

/// <summary>
/// 插件入口:把一种**工作台连接类型**注册进宿主。
/// <para>
/// 工作台给的是"不像文件系统"的连接(键值库、消息队列、数据库……):连接对话框、凭据加密落盘、
/// 登录弹窗、云同步、会话树与最近连接全部复用宿主,**插件只负责交出一个 Avalonia 控件**
/// (<see cref="WorkspaceDocument.CreateView" />)。像文件系统的那一类请用协议能力
/// (<c>dotnet new velaplugin-protocol</c> 或 <c>IProtocolFileSystem</c>)。
/// </para>
/// <para>
/// 整个程序集里恰好有一个公开、非抽象、带 <see cref="VelaPluginAttribute" /> 且实现
/// <see cref="IVelaPlugin" /> 的类型,并且要有公开无参构造。惰性激活见 <c>plugin.json</c>
/// 的 <c>onWorkspace:</c>。
/// </para>
/// <para>
/// ✏️ 通常要改的:<see cref="BuildDescriptor" />(页签名、图标、默认端口、能力位)、
/// <see cref="ConnectionSettings" />(表单)、<see cref="LineConnection" />(换成你的客户端)、
/// <see cref="WorkspaceView" />(面板)。注册、语言切换与停用这几段一般原样留着。
/// </para>
/// </summary>
[VelaPlugin]
public sealed class VelaPlugin1Plugin : IVelaPlugin
{
    private IPluginContext? _context;
    private WorkspaceProvider? _provider;
    private IDisposable? _registration;

    /// <summary>
    /// 激活。**必须快速返回**(宿主限时 10 秒):这里只注册,不建任何连接。
    /// </summary>
    public Task ActivateAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        var loc = new Loc(context.Host.Locale);
        _provider = new WorkspaceProvider(context, loc);
        _registration = context.Workspaces.Register(BuildDescriptor(context.PluginId, loc), _provider);
        // 表单标签是插件自己的文案,宿主不会替我们翻:语言一变就换一份描述重注册。
        context.Events.LocaleChanged += OnLocaleChanged;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停用(限时约 2 秒)。注册与事件订阅宿主本来也会自动撤,这里显式撤一遍:
    /// 一是好习惯,二是单测能据此验证"停用之后页签确实没了"。
    /// 已经开着的文档由宿主关闭(对每个文档调 <c>DisposeAsync</c>)。
    /// </summary>
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        if (_context is { } context)
        {
            context.Events.LocaleChanged -= OnLocaleChanged;
        }
        _registration?.Dispose();
        _registration = null;
        _provider = null;
        _context = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 语言变了:换一份文案重注册。
    /// <para>
    /// **provider 必须沿用同一个实例**:注册表把"同 id 换成另一个实现"视为旧实现失效,会通知宿主
    /// 关掉这个类型名下所有开着的文档 —— 用户只是切了个界面语言。同 id、同实例的重注册按"替换"处理,
    /// 不发注销事件;旧句柄直接丢掉即可(替换之后它已不代表注册表里那一份)。
    /// </para>
    /// </summary>
    private void OnLocaleChanged(string locale)
    {
        if (_context is not { } context || _provider is not { } provider)
        {
            return;
        }
        try
        {
            var loc = new Loc(locale);
            provider.Loc = loc;
            _registration = context.Workspaces.Register(BuildDescriptor(context.PluginId, loc), provider);
        }
        catch (Exception ex)
        {
            // 事件处理器必须快速返回且不抛:异常会被宿主吞掉记日志,这里先自己记清楚。
            context.Log.Error("Re-registering the workspace after a locale change failed.", ex);
        }
    }

    /// <summary>
    /// 连接类型描述:页签、图标、默认端口、连接表单、能力位。它是**纯数据**,宿主照着画界面。
    /// </summary>
    /// <param name="pluginId">插件 id。</param>
    /// <param name="loc">文案表。</param>
    /// <returns>连接类型描述。</returns>
    internal static WorkspaceDescriptor BuildDescriptor(string pluginId, Loc loc) =>
        new()
        {
            // 连接类型 id = 插件 id(一个插件多种类型时用 "<插件id>.<子类型>")。
            // 与 plugin.json 的 contributes.workspaces 一致,落进用户配置,发布后不可改。
            Id = pluginId,
            DisplayName = "VelaPlugin1",
            // ✏️ 会话标签页上的图标。不填就是所有插件共用的通用插头。lucide 描边字形(视框 24)用
            //    PluginIcon.Stroked;品牌 logo 多半实心、视框不是 24,用 PluginIcon.Filled(path, viewBoxSize: …)。
            //    这里是 lucide 的 layout-dashboard。
            Icon = PluginIcon.Stroked(
                "M4 3h5a1 1 0 0 1 1 1v7a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1Z "
                + "M15 3h5a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1Z "
                + "M15 12h5a1 1 0 0 1 1 1v7a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1v-7a1 1 0 0 1 1-1Z "
                + "M4 16h5a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-3a1 1 0 0 1 1-1Z"),
            // ✏️ 与 plugin.json 的 defaultPort 保持一致。
            DefaultPort = 7007,
            HostLabel = loc["Form_Host"],
            HostPlaceholder = "127.0.0.1",
            Fields = ConnectionSettings.Declare(loc),
            // AnonymousAccess:不填凭据也能连,宿主据此不弹登录框(回显服务没有认证)。
            // SshTunnel:宿主在连接对话框里追加"经 SSH 隧道"一节,并在 OpenAsync 之前代建本地转发;
            //   需要表单里有一个 ProtocolSettingKind.SshSession 字段(见 ConnectionSettings)。
            // ✏️ 还有 CertificateTrust(TLS 自签证书走"提示 → 记指纹 → 重连",配 TrustedThumbprintSettingKey
            //    指向一个 IsHidden 字段)、NoCredentials / NoEndpoint(本地文件型的连接收起凭据 / 端口栏)。
            Features = WorkspaceFeatures.AnonymousAccess | WorkspaceFeatures.SshTunnel,
        };
}
