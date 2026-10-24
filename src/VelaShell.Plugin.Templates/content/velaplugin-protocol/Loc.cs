using System.Globalization;

namespace VelaPlugin1;

/// <summary>
/// 插件自带的文案表。宿主不替插件翻译 —— 页签上的字段标签、提示、错误信息都是插件自己的字,
/// 宿主只认语言代码(<c>context.Host.Locale</c>,如 <c>zh-Hans</c>、<c>en</c>)。
/// <para>
/// 做法与第一方插件(Telnet / 串口 / Redis / S3)一致:一张英文表 + 一张简体中文表,
/// 未收录的语言回落英文,未收录的键原样返回 —— 界面上冒出一个键名,一眼就知道漏了哪条。
/// </para>
/// <para>
/// ✏️ 要支持更多语言(宿主有 zh-Hans / zh-Hant / ja / ko / en 五种):照样加一张表,
/// 在构造里按语言前缀选。语言切换时 <see cref="VelaPlugin1Plugin" /> 会换一份新表重注册。
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
        ["Form_Host"] = "Host / IP",
        ["Form_EnterMode"] = "Enter key sends",
        ["Form_EnterModeHint"] = "What goes on the wire when you press Enter. Most line-oriented servers want CR LF.",
        ["Form_EnterCrLf"] = "CR LF (recommended)",
        ["Form_EnterLf"] = "LF",
        ["Form_EnterCr"] = "CR (no rewrite)",
        ["Form_LocalEcho"] = "Local echo",
        ["Form_LocalEchoHint"] = "Show what you type even when the server does not echo it back.",
        ["Form_ConnectTimeout"] = "Connect timeout (seconds)",
        ["Error_NoHost"] = "Enter a host name or IP address.",
        ["Error_Connect"] = "Could not connect to {0}: {1}",
        ["Error_Timeout"] = "Could not connect to {0}: no answer within {1} s.",
    };

    private static readonly Dictionary<string, string> Chinese = new(StringComparer.Ordinal)
    {
        ["Form_Host"] = "主机名 / IP",
        ["Form_EnterMode"] = "回车键发送",
        ["Form_EnterModeHint"] = "按下回车时线上发什么。大多数按行工作的服务端要的是 CR LF。",
        ["Form_EnterCrLf"] = "CR LF(推荐)",
        ["Form_EnterLf"] = "LF",
        ["Form_EnterCr"] = "CR(不改写)",
        ["Form_LocalEcho"] = "本地回显",
        ["Form_LocalEchoHint"] = "对端不回显时,也能看见自己敲的字。",
        ["Form_ConnectTimeout"] = "连接超时(秒)",
        ["Error_NoHost"] = "请填写主机名或 IP 地址。",
        ["Error_Connect"] = "连不上 {0}:{1}",
        ["Error_Timeout"] = "连不上 {0}:{1} 秒内没有应答。",
    };
}
