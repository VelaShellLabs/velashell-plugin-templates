using System.Globalization;

namespace VelaPlugin1;

/// <summary>
/// 插件自带的文案表。宿主不替插件翻译 —— 页签上的字段标签、面板上的字、错误信息都是插件自己的,
/// 宿主只认语言代码(<c>context.Host.Locale</c>,如 <c>zh-Hans</c>、<c>en</c>)。
/// <para>
/// 做法与第一方插件(Telnet / 串口 / Redis / S3)一致:一张英文表 + 一张简体中文表,
/// 未收录的语言回落英文,未收录的键原样返回 —— 界面上冒出一个键名,一眼就知道漏了哪条。
/// </para>
/// <para>
/// ✏️ 要支持更多语言(宿主有 zh-Hans / zh-Hant / ja / ko / en 五种):照样加一张表,
/// 在构造里按语言前缀选。语言切换时 <see cref="VelaPlugin1Plugin" /> 会换一份新表重注册;
/// 已经开着的面板沿用打开时的语言(要热切换就让视图也订阅 <c>LocaleChanged</c>)。
/// </para>
/// </summary>
/// <param name="locale">宿主当前语言。</param>
internal sealed class Loc(string locale)
{
    private readonly Dictionary<string, string> _table =
        locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? Chinese : English;

    /// <summary>当前语言(构造时给的那个)。</summary>
    public string Locale { get; } = locale;

    /// <summary>取一条文案:当前语言没有就回落英文,英文也没有就原样返回键名。</summary>
    /// <param name="key">文案键。</param>
    /// <returns>文案。</returns>
    public string this[string key] =>
        _table.TryGetValue(key, out string? value) || English.TryGetValue(key, out value) ? value : key;

    /// <summary>取一条带占位符的文案并填参(不变文化,数字不随系统区域设置变样)。</summary>
    /// <param name="key">文案键。</param>
    /// <param name="args">参数。</param>
    /// <returns>文案。</returns>
    public string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, this[key], args);

    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["Form_Host"] = "Server address",
        ["Form_LineEnding"] = "Line ending",
        ["Form_LineEndingHint"] = "Appended to every line you send.",
        ["Form_LineLf"] = "LF",
        ["Form_LineCrLf"] = "CR LF",
        ["Form_JumpSession"] = "Through SSH tunnel",
        ["Form_JumpSessionHint"] = "Pick a saved SSH connection; VelaShell opens it and forwards a local port to this server before connecting.",
        ["Form_HistorySize"] = "Lines to remember",
        ["Form_HistorySizeHint"] = "How many sent lines are kept per server, across sessions.",
        ["Form_ConnectTimeout"] = "Connect timeout (seconds)",
        ["Error_NoHost"] = "Enter a server address.",
        ["Error_Connect"] = "Could not connect to {0}: {1}",
        ["Error_Timeout"] = "Could not connect to {0}: no answer within {1} s.",
        ["Error_NotConnected"] = "Not connected. Use Reconnect on the tab first.",
        ["Error_Send"] = "Sending failed: {0}",
        ["Status_Connected"] = "Connected · {0}",
        ["Status_Disconnected"] = "Disconnected by the server",
        ["View_Send"] = "Send",
        ["View_InputPlaceholder"] = "Type a line and press Enter",
        ["View_Connection"] = "CONNECTION",
        ["View_History"] = "RECENTLY SENT",
        ["Summary_Name"] = "Name",
        ["Summary_Server"] = "Server",
        ["Summary_Via"] = "Via",
        ["Summary_User"] = "User",
        ["Summary_Anonymous"] = "(anonymous)",
    };

    private static readonly Dictionary<string, string> Chinese = new(StringComparer.Ordinal)
    {
        ["Form_Host"] = "服务地址",
        ["Form_LineEnding"] = "行尾",
        ["Form_LineEndingHint"] = "每发送一行时追加在末尾。",
        ["Form_LineLf"] = "LF",
        ["Form_LineCrLf"] = "CR LF",
        ["Form_JumpSession"] = "经 SSH 隧道",
        ["Form_JumpSessionHint"] = "选一条已保存的 SSH 连接:VelaShell 先连上它,把一个本地端口转发到这台服务器,再连接。",
        ["Form_HistorySize"] = "记住的行数",
        ["Form_HistorySizeHint"] = "每台服务器保留多少条发送过的行,跨会话留存。",
        ["Form_ConnectTimeout"] = "连接超时(秒)",
        ["Error_NoHost"] = "请填写服务地址。",
        ["Error_Connect"] = "连不上 {0}:{1}",
        ["Error_Timeout"] = "连不上 {0}:{1} 秒内没有应答。",
        ["Error_NotConnected"] = "连接已断开,请先点标签页上的「重连」。",
        ["Error_Send"] = "发送失败:{0}",
        ["Status_Connected"] = "已连接 · {0}",
        ["Status_Disconnected"] = "服务器断开了连接",
        ["View_Send"] = "发送",
        ["View_InputPlaceholder"] = "输入一行,回车发送",
        ["View_Connection"] = "连接",
        ["View_History"] = "最近发送",
        ["Summary_Name"] = "名称",
        ["Summary_Server"] = "服务器",
        ["Summary_Via"] = "经由",
        ["Summary_User"] = "用户",
        ["Summary_Anonymous"] = "(匿名)",
    };
}
