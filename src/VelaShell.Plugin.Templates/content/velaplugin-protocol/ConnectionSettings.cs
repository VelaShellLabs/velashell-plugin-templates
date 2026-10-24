using VelaShell.PluginSdk.Protocols;

namespace VelaPlugin1;

/// <summary>回车键在线上发成什么。</summary>
internal enum EnterMode
{
    /// <summary>CR LF —— 按行工作的文本协议(SMTP / Redis 内联命令 / 大多数设备)要的就是它。</summary>
    CrLf,

    /// <summary>LF。</summary>
    Lf,

    /// <summary>原样发终端给的 CR,不改写。</summary>
    Cr
}

/// <summary>
/// 一条连接的强类型设置,外加连接表单的声明。
/// <para>
/// <b>声明(<see cref="Declare" />)与解析(<see cref="From" />)刻意放在同一个文件里</b>:
/// 两边只靠字符串键对齐,分开写迟早对不上 —— 第一方插件(Redis / S3 / 串口)全是这么放的。
/// </para>
/// <para>
/// 表单是**声明式**的:宿主按字段形态画控件、存值、把用户填的值原样递进
/// <see cref="ProtocolConnectRequest.Settings" />。插件没有一行连接对话框的界面代码。
/// </para>
/// </summary>
/// <param name="EnterMode">回车改写方式。</param>
/// <param name="LocalEcho">是否本地回显。</param>
/// <param name="ConnectTimeout">建连超时。</param>
internal sealed record ConnectionSettings(EnterMode EnterMode, bool LocalEcho, TimeSpan ConnectTimeout)
{
    // ✏️ 字段键:落进用户的会话配置,**发布后不可更名** —— 改了键,老配置里存的值就再也读不到。
    public const string KeyEnterMode = "enterMode";
    public const string KeyLocalEcho = "localEcho";
    public const string KeyConnectTimeout = "connectTimeout";

    /// <summary>
    /// 连接表单。字段顺序即表单顺序。
    /// <list type="bullet">
    ///   <item>决定"连不连得上"的留在外面;调优类(有合理默认值、绝大多数人不会改的)标
    ///     <see cref="ProtocolSettingField.IsAdvanced" />,宿主把它们放进「高级」页 ——
    ///     十来个字段一列铺开会把连接对话框顶出屏幕。</item>
    ///   <item>只在某种取值下才有意义的字段用 <see cref="ProtocolSettingField.VisibleWhen" />
    ///     声明,不要在 Hint 里写一行"仅 X 模式有效"。例:
    ///     <c>VisibleWhen = new(KeyMode, ["a", "b"])</c>。</item>
    ///   <item>令牌、附加密钥这类机密标 <see cref="ProtocolSettingField.IsSecret" />:
    ///     宿主随口令一起加密落盘,且不进日志。</item>
    /// </list>
    /// </summary>
    /// <param name="loc">文案表。</param>
    /// <returns>字段声明。</returns>
    public static IReadOnlyList<ProtocolSettingField> Declare(Loc loc) =>
    [
        new()
        {
            Key = KeyEnterMode,
            Label = loc["Form_EnterMode"],
            Kind = ProtocolSettingKind.Choice,
            DefaultValue = "crlf",
            Hint = loc["Form_EnterModeHint"],
            Choices =
            [
                // Value 是落盘的稳定标识,不随语言变;Label 才是给人看的。
                new("crlf", loc["Form_EnterCrLf"]),
                new("lf", loc["Form_EnterLf"]),
                new("cr", loc["Form_EnterCr"]),
            ],
        },
        new()
        {
            Key = KeyLocalEcho,
            Label = loc["Form_LocalEcho"],
            Kind = ProtocolSettingKind.Boolean,
            DefaultValue = "false",
            Hint = loc["Form_LocalEchoHint"],
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
    public static ConnectionSettings From(ProtocolConnectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnterMode enter = request.GetString(KeyEnterMode, "crlf") switch
        {
            "lf" => EnterMode.Lf,
            "cr" => EnterMode.Cr,
            _ => EnterMode.CrLf,
        };
        int seconds = Math.Clamp(request.GetInt32(KeyConnectTimeout, 10), 1, 120);
        return new(enter, request.GetBoolean(KeyLocalEcho), TimeSpan.FromSeconds(seconds));
    }
}
