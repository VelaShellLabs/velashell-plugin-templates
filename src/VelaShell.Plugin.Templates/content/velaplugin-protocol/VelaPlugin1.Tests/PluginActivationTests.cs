using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Protocols;
using VelaShell.PluginSdk.Testing;

namespace VelaPlugin1.Tests;

/// <summary>
/// 激活:注册出去的协议描述本身就是产品的一部分 —— 它决定连接对话框长什么样、会话标签画什么图标。
/// 这里用 SDK 的测试替身把它取出来验。<see cref="RecordingProtocols" /> 复刻了宿主 <c>Register</c>
/// 的前置校验(id 前缀、端口区间、指纹字段),所以这几条在单测里就会红,不必等装进宿主。
/// </summary>
[TestClass]
public sealed class PluginActivationTests
{
    private const string PluginId = "PLUGIN_ID";

    private static TestPluginContext NewContext(string locale = "en")
    {
        var context = new TestPluginContext { PluginId = PluginId };
        context.HostInfo.Locale = locale;
        return context;
    }

    private static async Task<ProtocolDescriptor> ActivateAsync(TestPluginContext context)
    {
        await new VelaPlugin1Plugin().ActivateAsync(context, CancellationToken.None);
        return context.RecordingProtocols.Registered.Single();
    }

    [TestMethod]
    public async Task Activate_RegistersATerminalProtocolUnderThePluginId()
    {
        using TestPluginContext context = NewContext();

        ProtocolDescriptor descriptor = await ActivateAsync(context);

        Assert.AreEqual(PluginId, descriptor.Id);
        Assert.AreEqual(7007, descriptor.DefaultPort);
        // 注册的是终端协议(会话打开终端标签),不是文件协议。
        Assert.IsNotNull(context.RecordingProtocols.GetTerminal(PluginId));
        Assert.IsNull(context.RecordingProtocols.GetFileSystem(PluginId));
    }

    [TestMethod]
    public async Task Activate_GivesTheSessionTabItsOwnIcon()
    {
        using TestPluginContext context = NewContext();

        ProtocolDescriptor descriptor = await ActivateAsync(context);

        PluginIcon? icon = descriptor.Icon;
        Assert.IsNotNull(icon, "不自报图标,标签上就是所有插件共用的那个通用插头。");
        // lucide 那套的规格:描边、视框 24。报成实心会把描边字形填成一团色块。
        Assert.IsFalse(icon.IsFilled);
        Assert.AreEqual(24d, icon.ViewBoxSize);
    }

    [TestMethod]
    public async Task Activate_DeclaresEveryKeyThatConnectReads()
    {
        // 表单声明与解析只靠字符串键对齐:这里把"解析读的键"与"表单声明的键"对一遍。
        using TestPluginContext context = NewContext();

        ProtocolDescriptor descriptor = await ActivateAsync(context);

        string[] declared = [.. descriptor.Fields.Select(field => field.Key)];
        CollectionAssert.AreEquivalent(
            new[] { ConnectionSettings.KeyEnterMode, ConnectionSettings.KeyLocalEcho, ConnectionSettings.KeyConnectTimeout },
            declared);
    }

    [TestMethod]
    public async Task DeclaredDefaults_ParseToTheDocumentedSettings()
    {
        // 宿主新建配置时按 DefaultValue 预填;那一组值解析出来的就是用户什么都不改时的行为。
        using TestPluginContext context = NewContext();
        ProtocolDescriptor descriptor = await ActivateAsync(context);
        var defaults = descriptor.Fields.ToDictionary(field => field.Key, field => field.DefaultValue ?? "", StringComparer.Ordinal);

        ConnectionSettings settings = ConnectionSettings.From(new() { Host = "127.0.0.1", Port = 7007, Settings = defaults });

        Assert.AreEqual(EnterMode.CrLf, settings.EnterMode);
        Assert.IsFalse(settings.LocalEcho);
        Assert.AreEqual(TimeSpan.FromSeconds(10), settings.ConnectTimeout);
    }

    [TestMethod]
    public async Task Activate_LocalizesTheForm()
    {
        using TestPluginContext chinese = NewContext("zh-Hans");
        using TestPluginContext english = NewContext("en");

        ProtocolDescriptor zh = await ActivateAsync(chinese);
        ProtocolDescriptor en = await ActivateAsync(english);

        Assert.AreEqual("主机名 / IP", zh.HostLabel);
        Assert.AreEqual("Host / IP", en.HostLabel);
    }

    [TestMethod]
    public async Task LocaleChanged_ReRegistersWithTheSameTerminal()
    {
        // 换成另一个实现实例,宿主会把这个协议名下开着的标签页全部关掉 —— 用户只是切了个语言。
        using TestPluginContext context = NewContext("en");
        await ActivateAsync(context);
        IProtocolTerminal? before = context.RecordingProtocols.GetTerminal(PluginId);

        context.HostEvents.RaiseLocaleChanged("zh-Hans");

        Assert.AreSame(before, context.RecordingProtocols.GetTerminal(PluginId));
        Assert.AreEqual("主机名 / IP", context.RecordingProtocols.Registered.Single().HostLabel);
    }

    [TestMethod]
    public async Task Deactivate_UnregistersTheProtocol()
    {
        using TestPluginContext context = NewContext();
        var plugin = new VelaPlugin1Plugin();
        await plugin.ActivateAsync(context, CancellationToken.None);

        await plugin.DeactivateAsync(CancellationToken.None);

        Assert.IsEmpty(context.RecordingProtocols.Registered);
    }
}
