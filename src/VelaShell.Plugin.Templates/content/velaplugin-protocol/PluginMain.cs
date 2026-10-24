using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Protocols;

namespace VelaPlugin1;

/// <summary>
/// 插件入口:把一种**终端协议**注册进宿主,让它在连接配置页里与 SSH / SFTP / FTP 平起平坐。
/// <para>
/// 整个程序集里恰好有一个公开、非抽象、带 <see cref="VelaPluginAttribute" /> 且实现
/// <see cref="IVelaPlugin" /> 的类型,并且要有公开无参构造。
/// </para>
/// <para>
/// 惰性激活(<c>plugin.json</c> 的 <c>onProtocol:</c>):用户点到本协议的页签、或打开一条本协议的
/// 会话时才装载本程序集。激活后终端标签、VT 引擎、回滚、搜索、会话日志、录制与 ZMODEM
/// 全部由宿主复用,插件只实现字节双工(<see cref="ProtocolSession" />)。
/// </para>
/// <para>
/// ✏️ 通常要改的只有三处:<see cref="BuildDescriptor" />(页签名、图标、默认端口、表单)、
/// <see cref="ProtocolTerminal" />(建连与错误翻译)、<see cref="ProtocolSession" />(传输)。
/// 注册、语言切换与停用这几段一般原样留着。
/// </para>
/// </summary>
[VelaPlugin]
public sealed class VelaPlugin1Plugin : IVelaPlugin
{
    private IPluginContext? _context;
    private ProtocolTerminal? _terminal;
    private IDisposable? _registration;

    /// <summary>
    /// 激活。**必须快速返回**(宿主限时 10 秒):这里只注册,不建任何连接。
    /// </summary>
    public Task ActivateAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        var loc = new Loc(context.Host.Locale);
        _terminal = new ProtocolTerminal(context, loc);
        _registration = context.Protocols.Register(BuildDescriptor(context.PluginId, loc), _terminal);
        // 表单标签与提示是插件自己的文案,宿主不会替我们翻:语言一变就换一份描述重注册。
        context.Events.LocaleChanged += OnLocaleChanged;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停用(限时约 2 秒)。注册与事件订阅宿主本来也会自动撤,这里显式撤一遍:
    /// 一是好习惯,二是单测能据此验证"停用之后页签确实没了"。
    /// 已经开着的终端标签由宿主关闭(它会对每条会话调 <c>DisposeAsync</c>)。
    /// </summary>
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        if (_context is { } context)
        {
            context.Events.LocaleChanged -= OnLocaleChanged;
        }
        _registration?.Dispose();
        _registration = null;
        _terminal = null;
        _context = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 语言变了:换一份文案重注册。
    /// <para>
    /// **实现实例必须沿用同一个**:宿主把"同 id 换成另一个实现"视为旧实现失效,会把这个协议名下
    /// 开着的标签页全部关掉 —— 用户只是切了个界面语言。同 id、同实例的重注册按"替换"处理,
    /// 不发注销事件;旧句柄直接丢掉即可(替换之后它已不代表注册表里那一份)。
    /// </para>
    /// </summary>
    private void OnLocaleChanged(string locale)
    {
        if (_context is not { } context || _terminal is not { } terminal)
        {
            return;
        }
        try
        {
            var loc = new Loc(locale);
            terminal.Loc = loc;
            _registration = context.Protocols.Register(BuildDescriptor(context.PluginId, loc), terminal);
        }
        catch (Exception ex)
        {
            // 事件处理器必须快速返回且不抛:异常会被宿主吞掉记日志,这里先自己记清楚。
            context.Log.Error("Re-registering the protocol after a locale change failed.", ex);
        }
    }

    /// <summary>
    /// 协议描述:页签、图标、默认端口、连接表单、能力位。它是**纯数据**,宿主照着画界面。
    /// </summary>
    /// <param name="pluginId">插件 id。</param>
    /// <param name="loc">文案表。</param>
    /// <returns>协议描述。</returns>
    internal static ProtocolDescriptor BuildDescriptor(string pluginId, Loc loc) =>
        new()
        {
            // 协议 id = 插件 id(一个插件多个协议时用 "<插件id>.<子协议>")。
            // 与 plugin.json 的 contributes.protocols 一致,落进用户配置,发布后不可改。
            Id = pluginId,
            DisplayName = "VelaPlugin1",
            // ✏️ 会话标签页上的图标。不填就是所有插件共用的通用插头 —— 几种插件会话并排时分不出谁是谁。
            //    lucide 那套描边字形(视框 24)直接抄 path 用 PluginIcon.Stroked;
            //    品牌 logo 多半是实心、视框不是 24,用 PluginIcon.Filled(path, viewBoxSize: …),两件都要报。
            //    这里是 lucide 的 arrow-left-right(一条双工的字节通道)。
            Icon = PluginIcon.Stroked("M8 3L4 7l4 4 M4 7h16 M16 21l4-4l-4-4 M20 17H4"),
            // ✏️ 与 plugin.json 的 defaultPort 保持一致。
            DefaultPort = 7007,
            HostLabel = loc["Form_Host"],
            HostPlaceholder = "127.0.0.1",
            // NoCredentials:裸 TCP 没有协议级凭据,宿主据此收起用户名 / 口令两栏
            // (摆着填了也不起作用的框,只会让用户以为填上就能自动登录)。
            // ✏️ 协议有协议级认证时去掉 NoCredentials,在 ProtocolTerminal 里认出认证失败并抛
            //    ProtocolAuthenticationException;串口这类没有网络端点的再加 NoEndpoint。
            Features = ProtocolFeatures.AnonymousAccess | ProtocolFeatures.NoCredentials,
            Fields = ConnectionSettings.Declare(loc),
        };
}
