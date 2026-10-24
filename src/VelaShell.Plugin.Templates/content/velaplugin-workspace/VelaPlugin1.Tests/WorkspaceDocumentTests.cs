using VelaShell.PluginSdk.Protocols;
using VelaShell.PluginSdk.Testing;
using VelaShell.PluginSdk.Workspaces;

namespace VelaPlugin1.Tests;

/// <summary>
/// 文档的生命周期:打开即连上、收发、状态跨会话留存、对端断开变红、重连、关闭。
/// 走的是注册进替身的那个 <see cref="IWorkspaceProvider" />,与宿主拿到的是同一条路径;
/// 视图(Avalonia)刻意不碰,见测试工程的 csproj。
/// </summary>
[TestClass]
public sealed class WorkspaceDocumentTests
{
    private const string PluginId = "PLUGIN_ID";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static async Task<IWorkspaceProvider> ActivateAsync(TestPluginContext context)
    {
        await new VelaPlugin1Plugin().ActivateAsync(context, CancellationToken.None);
        return context.RecordingWorkspaces.GetProvider(PluginId)!;
    }

    private static WorkspaceConnectRequest Request(int port) =>
        new()
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Host = "127.0.0.1",
            Port = port,
            DisplayName = "local echo",
            Settings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ConnectionSettings.KeyConnectTimeout] = "5",
            },
        };

    private static async Task<WorkspaceDocument> OpenAsync(IWorkspaceProvider provider, int port) =>
        (WorkspaceDocument)await provider.OpenAsync(Request(port), CancellationToken.None);

    /// <summary>等某个事件来一次(带超时),免得事件没来时测试永远挂着。</summary>
    private static async Task<T> NextAsync<T>(Action<Action<T>> subscribe)
    {
        var arrived = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        subscribe(value => arrived.TrySetResult(value));
        return await arrived.Task.WaitAsync(Patience);
    }

    [TestMethod]
    public async Task Open_ConnectsBeforeReturning_AndReportsConnected()
    {
        await using var server = new LoopbackEchoServer();
        using var context = new TestPluginContext { PluginId = PluginId };
        IWorkspaceProvider provider = await ActivateAsync(context);

        await using WorkspaceDocument document = await OpenAsync(provider, server.Port);

        Assert.AreEqual(ProtocolSessionState.Connected, document.Status.State);
        Assert.AreEqual("local echo", document.Title);
        Assert.AreEqual($"127.0.0.1:{server.Port}", document.Endpoint);
    }

    [TestMethod]
    public async Task Open_WhenNothingListens_ThrowsAConnectionExceptionNamingTheEndpoint()
    {
        int deadPort = LoopbackEchoServer.FreePort();
        using var context = new TestPluginContext { PluginId = PluginId };
        IWorkspaceProvider provider = await ActivateAsync(context);

        // 宿主正是靠这一抛把失败呈现在连接流程里;悄悄交回一个"连着但其实没连上"的文档是最难排查的坏法。
        ProtocolConnectionException failure = await Assert.ThrowsExactlyAsync<ProtocolConnectionException>(
            () => provider.OpenAsync(Request(deadPort), CancellationToken.None));

        Assert.Contains($"127.0.0.1:{deadPort}", failure.Message);
    }

    [TestMethod]
    public async Task Send_GetsTheLineEchoedBack()
    {
        await using var server = new LoopbackEchoServer();
        using var context = new TestPluginContext { PluginId = PluginId };
        IWorkspaceProvider provider = await ActivateAsync(context);
        await using WorkspaceDocument document = await OpenAsync(provider, server.Port);
        Task<string> reply = NextAsync<string>(handler => document.MessageReceived += handler);

        await document.SendAsync("hello");

        string echoed = await reply;
        Assert.AreEqual("hello", echoed);
    }

    [TestMethod]
    public async Task History_IsRestoredTheNextTimeTheSameServerIsOpened()
    {
        await using var server = new LoopbackEchoServer();
        using var context = new TestPluginContext { PluginId = PluginId };
        IWorkspaceProvider provider = await ActivateAsync(context);
        await using (WorkspaceDocument first = await OpenAsync(provider, server.Port))
        {
            await first.LoadStateAsync();
            await first.SendAsync("one");
            await first.SendAsync("two");
            first.Draft = "unfinished";
        }

        // 新打开一次:SessionId 是新的,端点相同 —— 状态按端点存,所以还在。
        await using WorkspaceDocument second = await OpenAsync(provider, server.Port);
        WorkspaceState restored = await second.LoadStateAsync();

        CollectionAssert.AreEqual(new[] { "two", "one" }, restored.History);
        Assert.AreEqual("unfinished", restored.Draft);
    }

    [TestMethod]
    public async Task OpenThenDispose_LikeTheTestButton_LeavesSavedStateAlone()
    {
        // 连接页的「测试」按钮:OpenAsync → 立刻 DisposeAsync,中间不建视图、不读状态。
        // 它不该把上次留下的历史清掉。
        await using var server = new LoopbackEchoServer();
        using var context = new TestPluginContext { PluginId = PluginId };
        IWorkspaceProvider provider = await ActivateAsync(context);
        await using (WorkspaceDocument used = await OpenAsync(provider, server.Port))
        {
            await used.SendAsync("keep me");
        }

        WorkspaceDocument probe = await OpenAsync(provider, server.Port);
        await probe.DisposeAsync();

        await using WorkspaceDocument later = await OpenAsync(provider, server.Port);
        WorkspaceState state = await later.LoadStateAsync();
        CollectionAssert.AreEqual(new[] { "keep me" }, state.History);
    }

    [TestMethod]
    public async Task ServerHangingUp_TurnsTheStatusFaulted_AndReconnectRecovers()
    {
        await using var server = new LoopbackEchoServer();
        using var context = new TestPluginContext { PluginId = PluginId };
        IWorkspaceProvider provider = await ActivateAsync(context);
        await using WorkspaceDocument document = await OpenAsync(provider, server.Port);
        Task<WorkspaceStatus> faulted = NextAsync<WorkspaceStatus>(handler => document.StatusChanged += (_, status) => handler(status));

        server.DropClients();

        WorkspaceStatus status = await faulted;
        Assert.AreEqual(ProtocolSessionState.Faulted, status.State);

        // 宿主标签页上的"重连"按钮调的就是它。
        await document.ReconnectAsync();
        Assert.AreEqual(ProtocolSessionState.Connected, document.Status.State);
        Task<string> reply = NextAsync<string>(handler => document.MessageReceived += handler);
        await document.SendAsync("back");
        string echoed = await reply;
        Assert.AreEqual("back", echoed);
    }

    [TestMethod]
    public async Task Dispose_ReportsClosed()
    {
        await using var server = new LoopbackEchoServer();
        using var context = new TestPluginContext { PluginId = PluginId };
        IWorkspaceProvider provider = await ActivateAsync(context);
        WorkspaceDocument document = await OpenAsync(provider, server.Port);

        await document.DisposeAsync();

        Assert.AreEqual(ProtocolSessionState.Closed, document.Status.State);
    }
}
