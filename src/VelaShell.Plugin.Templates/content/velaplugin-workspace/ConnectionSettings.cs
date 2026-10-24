using VelaShell.PluginSdk.Protocols;
using VelaShell.PluginSdk.Workspaces;

namespace VelaPlugin1;

/// <summary>
/// 一条连接的强类型设置,外加连接表单的声明。
/// <para>
/// <b>声明(<see cref="Declare" />)与解析(<see cref="From" />)刻意放在同一个文件里</b>:
/// 两边只靠字符串键对齐,分开写迟早对不上 —— 第一方插件(Redis / S3 / 串口)全是这么放的。
/// </para>
/// <para>
/// 工作台与协议能力共用同一套**声明式**表单(<see cref="ProtocolSettingField" />):宿主按字段形态
/// 画控件、加密存凭据、把用户填的值原样递进 <see cref="WorkspaceConnectRequest.Settings" />。
/// 插件没有一行连接对话框的界面代码。
/// </para>
/// </summary>
/// <param name="LineEnding">每行末尾追加的字节。</param>
/// <param name="HistorySize">每台服务器记住多少条发送过的行。</param>
/// <param name="ConnectTimeout">建连超时。</param>
internal sealed record ConnectionSettings(string LineEnding, int HistorySize, TimeSpan ConnectTimeout)
{
    // ✏️ 字段键:落进用户的会话配置,**发布后不可更名** —— 改了键,老配置里存的值就再也读不到。
    public const string KeyLineEnding = "lineEnding";
    public const string KeyJumpSession = "jumpSession";
    public const string KeyHistorySize = "historySize";
    public const string KeyConnectTimeout = "connectTimeout";

    /// <summary>
    /// 连接表单。字段顺序即表单顺序。
    /// <list type="bullet">
    ///   <item>决定"连不连得上"的留在外面;调优类(有合理默认值、绝大多数人不会改的)标
    ///     <see cref="ProtocolSettingField.IsAdvanced" />,宿主把它们放进「高级」页。</item>
    ///   <item>只在某种取值下才有意义的字段用 <see cref="ProtocolSettingField.VisibleWhen" />
    ///     声明(例:<c>VisibleWhen = new(KeyMode, ["sentinel"])</c>),不要在 Hint 里写"仅 X 模式有效"。</item>
    ///   <item>令牌、附加密钥这类机密标 <see cref="ProtocolSettingField.IsSecret" />;宿主或插件写回、
    ///     不给用户看的状态(如证书指纹)标 <see cref="ProtocolSettingField.IsHidden" />。</item>
    /// </list>
    /// </summary>
    /// <param name="loc">文案表。</param>
    /// <returns>字段声明。</returns>
    public static IReadOnlyList<ProtocolSettingField> Declare(Loc loc) =>
    [
        new()
        {
            Key = KeyLineEnding,
            Label = loc["Form_LineEnding"],
            Kind = ProtocolSettingKind.Choice,
            DefaultValue = "lf",
            Hint = loc["Form_LineEndingHint"],
            Choices =
            [
                // Value 是落盘的稳定标识,不随语言变;Label 才是给人看的。
                new("lf", loc["Form_LineLf"]),
                new("crlf", loc["Form_LineCrLf"]),
            ],
        },
        new()
        {
            // 「经 SSH 隧道」:选一条已保存的 SSH 配置,宿主在 OpenAsync **之前**建好 SSH 会话与
            // 本地端口转发,递给插件的 Host/Port 已是本地端点(真实目标在 request.Tunnel 里)。
            // 插件因此一行 SSH 代码都不用写、一次 SSH 凭据都不用见。
            // 要配合描述符上的 WorkspaceFeatures.SshTunnel 才生效(见 PluginMain.BuildDescriptor)。
            // ✏️ 服务从不藏在内网里的话,删掉这个字段并去掉那一位能力。
            Key = KeyJumpSession,
            Label = loc["Form_JumpSession"],
            Kind = ProtocolSettingKind.SshSession,
            Hint = loc["Form_JumpSessionHint"],
        },
        new()
        {
            Key = KeyHistorySize,
            Label = loc["Form_HistorySize"],
            Kind = ProtocolSettingKind.Integer,
            DefaultValue = "20",
            Hint = loc["Form_HistorySizeHint"],
            IsAdvanced = true,
        },
        new()
        {
            Key = KeyConnectTimeout,
            Label = loc["Form_ConnectTimeout"],
            Kind = ProtocolSettingKind.Integer,
            DefaultValue = "10",
            IsAdvanced = true,
        },
    ];

    /// <summary>
    /// 从宿主的连接请求解析设置。宿主会按声明补齐默认值,但**照样要给兜底**:
    /// 老配置是在这个字段存在之前存下的、手输的整数越界、下拉值是别的版本写的 —— 都会走到这里。
    /// </summary>
    /// <param name="request">连接请求。</param>
    /// <returns>设置。</returns>
    public static ConnectionSettings From(WorkspaceConnectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string ending = request.GetString(KeyLineEnding, "lf") == "crlf" ? "\r\n" : "\n";
        int history = Math.Clamp(request.GetInt32(KeyHistorySize, 20), 0, 500);
        int seconds = Math.Clamp(request.GetInt32(KeyConnectTimeout, 10), 1, 120);
        return new(ending, history, TimeSpan.FromSeconds(seconds));
    }
}
