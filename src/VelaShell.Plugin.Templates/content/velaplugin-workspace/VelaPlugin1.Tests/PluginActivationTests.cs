using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Protocols;
using VelaShell.PluginSdk.Testing;
using VelaShell.PluginSdk.Workspaces;

namespace VelaPlugin1.Tests;

/// <summary>
/// 激活:注册出去的连接类型描述本身就是产品的一部分 —— 它决定连接对话框长什么样、
/// 会话标签画什么图标。<see cref="RecordingWorkspaces" /> 复刻了宿主 <c>Register</c> 的前置校验
/// (id 前缀、端口区间、指纹字段),所以这几条在单测里就会红,不必等装进宿主。
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

    private static async Task<WorkspaceDescriptor> ActivateAsync(TestPluginContext context)
    {
        await new VelaPlugin1Plugin().ActivateAsync(context, CancellationToken.None);
        return context.RecordingWorkspaces.Registered.Single();
    }

    [TestMethod]
    public async Task Activate_RegistersOneWorkspaceUnderThePluginId()
    {
        using TestPluginContext context = NewContext();

        WorkspaceDescriptor descriptor = await ActivateAsync(context);

        Assert.AreEqual(PluginId, descriptor.Id);
        Assert.AreEqual(7007, descriptor.DefaultPort);
        Assert.IsNotNull(context.RecordingWorkspaces.GetProvider(PluginId));
    }

    [TestMethod]
    public async Task Activate_GivesTheSessionTabItsOwnIcon()
    {
        using TestPluginContext context = NewContext();

        PluginIcon? icon = (await ActivateAsync(context)).Icon;

        Assert.IsNotNull(icon, "不自报图标,标签上就是所有插件共用的那个通用插头。");
        Assert.IsFalse(icon.IsFilled);
        Assert.AreEqual(24d, icon.ViewBoxSize);
    }

    [TestMethod]
    public async Task SshTunnel_ComesWithAnSshSessionField()
    {
        // 声明了 SshTunnel 却没有 SshSession 字段,连接对话框里就没有地方选跳板机。
        using TestPluginContext context = NewContext();

        WorkspaceDescriptor descriptor = await ActivateAsync(context);

        Assert.IsTrue(descriptor.Features.HasFlag(WorkspaceFeatures.SshTunnel));
        ProtocolSettingField jump = descriptor.Fields.Single(field => field.Kind == ProtocolSettingKind.SshSession);
        Assert.AreEqual(ConnectionSettings.KeyJumpSession, jump.Key);
    }

    [TestMethod]
    public async Task DeclaredDefaults_ParseToTheDocumentedSettings()
    {
        // 宿主新建配置时按 DefaultValue 预填;那一组值解析出来的就是用户什么都不改时的行为。
        using TestPluginContext context = NewContext();
        WorkspaceDescriptor descriptor = await ActivateAsync(context);
        var defaults = descriptor.Fields.ToDictionary(field => field.Key, field => field.DefaultValue ?? "", StringComparer.Ordinal);

        ConnectionSettings settings = ConnectionSettings.From(new()
        {
            SessionId = "defaults",
            Host = "127.0.0.1",
            Port = 7007,
            Settings = defaults,
        });

        Assert.AreEqual("\n", settings.LineEnding);
        Assert.AreEqual(20, settings.HistorySize);
        Assert.AreEqual(TimeSpan.FromSeconds(10), settings.ConnectTimeout);
    }

    [TestMethod]
    public async Task Activate_LocalizesTheForm()
    {
        using TestPluginContext chinese = NewContext("zh-Hans");
        using TestPluginContext english = NewContext("en");

        WorkspaceDescriptor zh = await ActivateAsync(chinese);
        WorkspaceDescriptor en = await ActivateAsync(english);

        Assert.AreEqual("服务地址", zh.HostLabel);
        Assert.AreEqual("Server address", en.HostLabel);
    }

    [TestMethod]
    public async Task LocaleChanged_ReRegistersWithoutSwappingTheProvider()
    {
        // provider 换了实例就等于"换了实现",宿主会关掉该类型名下所有开着的文档 —— 用户只是切了个语言。
        using TestPluginContext context = NewContext("en");
        await ActivateAsync(context);
        IWorkspaceProvider? before = context.RecordingWorkspaces.GetProvider(PluginId);

        context.HostEvents.RaiseLocaleChanged("zh-Hans");

        Assert.AreSame(before, context.RecordingWorkspaces.GetProvider(PluginId));
        Assert.AreEqual("服务地址", context.RecordingWorkspaces.Registered.Single().HostLabel);
    }

    [TestMethod]
    public async Task Deactivate_UnregistersTheWorkspace()
    {
        using TestPluginContext context = NewContext();
        var plugin = new VelaPlugin1Plugin();
        await plugin.ActivateAsync(context, CancellationToken.None);

        await plugin.DeactivateAsync(CancellationToken.None);

        Assert.IsEmpty(context.RecordingWorkspaces.Registered);
    }
}
