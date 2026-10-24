using VelaShell.PluginSdk;
using VelaShell.PluginSdk.Protocols;
using VelaShell.PluginSdk.Workspaces;

namespace VelaPlugin1;

/// <summary>
/// 一条已打开的工作台文档:**插件持有连接与界面**,宿主只负责标签页外壳、状态圆点与"重连"按钮。
/// <para>
/// 生命周期:<see cref="WorkspaceProvider.OpenAsync" /> 连上后造出来 → 宿主在 UI 线程调一次
/// <see cref="CreateView" /> 取控件挂进停靠区 → 用户点"重连"时 <see cref="ReconnectAsync" /> →
/// 用户关闭标签页或插件停用时 <see cref="DisposeAsync" />。连接配置页的「测试」按钮走的也是这条路:
/// <c>OpenAsync</c> 之后立刻 <c>DisposeAsync</c>,中间不会调 <c>CreateView</c>。
/// </para>
/// <para>
/// 刻意**不碰任何 Avalonia 类型**(视图只在 <see cref="CreateView" /> 里出现):界面线程的封送由视图
/// 自己做,于是这个类可以在不带 Avalonia 运行时的单测里完整驱动。
/// </para>
/// </summary>
internal sealed class WorkspaceDocument : IWorkspaceDocument
{
    private readonly WorkspaceConnectRequest _request;
    private readonly ConnectionSettings _settings;
    private readonly IPluginContext _context;
    private readonly WorkspaceStateStore _store;
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);
    private LineConnection _connection;
    private WorkspaceState? _state;
    private int _disposed;

    /// <summary>构造。</summary>
    /// <param name="connection">已连上的连接。</param>
    /// <param name="request">宿主的连接请求(取展示名、端点、隧道来路)。</param>
    /// <param name="settings">解析好的设置。</param>
    /// <param name="loc">文案表。</param>
    /// <param name="context">插件上下文。</param>
    public WorkspaceDocument(
        LineConnection connection,
        WorkspaceConnectRequest request,
        ConnectionSettings settings,
        Loc loc,
        IPluginContext context)
    {
        _request = request;
        _settings = settings;
        _context = context;
        Loc = loc;
        _store = new WorkspaceStateStore(context.Storage);
        Endpoint = DescribeEndpoint(request);
        ConnectionKey = KeyOf(request);
        Title = string.IsNullOrWhiteSpace(request.DisplayName) ? Endpoint : request.DisplayName;
        _connection = Attach(connection);
        Status = Connected(connection);
    }

    /// <summary>文案表(打开时的语言)。</summary>
    public Loc Loc { get; }

    /// <summary>面板标题:用户给这条配置起的名字,没起就用端点。</summary>
    public string Title { get; }

    /// <summary>
    /// 给人看的端点。走隧道时 <c>request.Host</c> 是宿主建好的本地转发端点(127.0.0.1:随机端口),
    /// 用户认的是真实目标与来路,所以显示 <c>10.0.3.12:7007 ↝ bastion-01</c>。
    /// </summary>
    public string Endpoint { get; }

    /// <summary>状态的存储键:同一台服务器(走隧道时取真实目标)就是同一个键。</summary>
    public string ConnectionKey { get; }

    /// <summary>当前状态里的草稿。由视图在输入框变化时写回,关闭时一并存下。</summary>
    public string Draft { get; set; } = "";

    /// <summary>最近发送过的行,新的在前(状态还没读回来时为空)。</summary>
    public IReadOnlyList<string> History => _state?.History ?? [];

    /// <inheritdoc />
    public WorkspaceStatus Status { get; private set; }

    /// <inheritdoc />
    public event EventHandler<WorkspaceStatus>? StatusChanged;

    /// <summary>收到一行。**在后台线程触发**,视图要自己封送回 UI 线程。</summary>
    public event Action<string>? MessageReceived;

    /// <summary>
    /// 这条连接的配置摘要(面板右栏显示)。这是"面板与连接配置的关联"最直接的一面:
    /// 名称、端点、隧道来路、用户名与表单里的专属设置都来自宿主递过来的 <see cref="WorkspaceConnectRequest" />。
    /// **口令永远不显示、不记日志**;<see cref="ProtocolSettingField.IsSecret" /> 的字段同理。
    /// </summary>
    /// <returns>"标签 · 值"形式的行。</returns>
    public IReadOnlyList<string> DescribeConnection()
    {
        var lines = new List<string>
        {
            $"{Loc["Summary_Name"]} · {Title}",
            $"{Loc["Summary_Server"]} · {(_request.Tunnel is { } t ? $"{t.TargetHost}:{t.TargetPort}" : $"{_request.Host}:{_request.Port}")}",
        };
        if (_request.Tunnel is { } tunnel)
        {
            lines.Add($"{Loc["Summary_Via"]} · {tunnel.JumpDisplayName}");
        }
        lines.Add($"{Loc["Summary_User"]} · {(_request.Username.Length > 0 ? _request.Username : Loc["Summary_Anonymous"])}");
        foreach (ProtocolSettingField field in ConnectionSettings.Declare(Loc))
        {
            if (field.IsSecret || field.IsHidden || field.Kind == ProtocolSettingKind.SshSession)
            {
                continue; // 机密不显示;跳板配置是不透明 id,上面的"经由"已经说清了。
            }
            string value = _request.GetString(field.Key, field.DefaultValue ?? "");
            string shown = field.Choices.FirstOrDefault(choice => choice.Value == value)?.Label ?? value;
            lines.Add($"{field.Label} · {shown}");
        }
        return lines;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 由宿主在 **UI 线程**调用一次;必须返回一个 <c>Avalonia.Controls.Control</c>。
    /// 抛异常或返回非控件只会让这一个标签页显示一行说明,不会带走宿主。
    /// </remarks>
    public object CreateView() => new WorkspaceView(this);

    /// <summary>
    /// 读回这条连接上次留下的状态。由视图在挂上可视树后调用(构造期做 I/O 会让标签页出现前先卡一拍)。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>状态。</returns>
    public async Task<WorkspaceState> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        if (_state is { } loaded)
        {
            return loaded;
        }
        WorkspaceState state = await _store.LoadAsync(ConnectionKey, cancellationToken).ConfigureAwait(false);
        _state = state;
        Draft = state.Draft;
        return state;
    }

    /// <summary>发一行,并把它记进历史。</summary>
    /// <param name="line">要发的行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    /// <exception cref="ProtocolConnectionException">连接已断开或写失败。</exception>
    public async Task SendAsync(string line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);
        LineConnection connection = _connection;
        if (!connection.IsOpen)
        {
            throw new ProtocolConnectionException(Loc["Error_NotConnected"]);
        }
        try
        {
            await connection.SendLineAsync(line, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new ProtocolConnectionException(Loc.Format("Error_Send", ex.Message), ex);
        }

        WorkspaceState current = _state ?? await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        _state = current with
        {
            Draft = "",
            History = [.. new[] { line }.Concat(current.History.Where(old => old != line)).Take(_settings.HistorySize)],
        };
        Draft = "";
        if (!await _store.SaveAsync(ConnectionKey, _state, cancellationToken).ConfigureAwait(false))
        {
            _context.Log.Info("Saving the workspace state failed; history is kept for this session only.");
        }
    }

    /// <inheritdoc />
    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await _reconnectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LineConnection fresh;
            try
            {
                fresh = await LineConnection.ConnectAsync(
                    _request.Host.Trim(), _request.Port, _settings, Loc, _context.Log, Endpoint, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ProtocolConnectionException ex)
            {
                // 失败要**同时**做两件事:状态如实变红,异常抛给宿主由它呈现原因。
                Publish(new(ProtocolSessionState.Faulted, ex.Message));
                throw;
            }
            LineConnection old = _connection;
            _connection = Attach(fresh);
            Detach(old);
            await old.DisposeAsync().ConfigureAwait(false);
            Publish(Connected(fresh));
        }
        finally
        {
            _ = _reconnectGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        // 只有读回过状态才写:连接页的「测试」按钮是 OpenAsync → DisposeAsync,
        // 那时没读过状态,这里一写就把上次的历史清成了空。
        if (_state is { } state && state.Draft != Draft)
        {
            _ = await _store.SaveAsync(ConnectionKey, state with { Draft = Draft }).ConfigureAwait(false);
        }
        Detach(_connection);
        await _connection.DisposeAsync().ConfigureAwait(false);
        Publish(new(ProtocolSessionState.Closed));
        MessageReceived = null;
        StatusChanged = null;
    }

    /// <summary>端点的显示形式(见 <see cref="Endpoint" />)。</summary>
    internal static string DescribeEndpoint(WorkspaceConnectRequest request) =>
        request.Tunnel is { } tunnel
            ? $"{tunnel.TargetHost}:{tunnel.TargetPort} ↝ {tunnel.JumpDisplayName}"
            : $"{request.Host}:{request.Port}";

    /// <summary>状态存储键(见 <see cref="ConnectionKey" />)。</summary>
    internal static string KeyOf(WorkspaceConnectRequest request) =>
        (request.Tunnel is { } tunnel
            ? $"{tunnel.TargetHost}:{tunnel.TargetPort}@{tunnel.JumpDisplayName}"
            : $"{request.Host}:{request.Port}").ToLowerInvariant();

    private WorkspaceStatus Connected(LineConnection connection) =>
        new(ProtocolSessionState.Connected, Loc.Format("Status_Connected", Endpoint),
            (int)Math.Round(connection.ConnectTime.TotalMilliseconds));

    private LineConnection Attach(LineConnection connection)
    {
        connection.LineReceived += OnLineReceived;
        connection.Closed += OnClosed;
        return connection;
    }

    private void Detach(LineConnection connection)
    {
        connection.LineReceived -= OnLineReceived;
        connection.Closed -= OnClosed;
    }

    private void OnLineReceived(string line) => MessageReceived?.Invoke(line);

    // 对端断开:标签页的状态圆点变红、"重连"按钮可用。文档本身不关 —— 用户还要看着刚才的记录。
    private void OnClosed() => Publish(new(ProtocolSessionState.Faulted, Loc["Status_Disconnected"]));

    private void Publish(WorkspaceStatus status)
    {
        Status = status;
        try
        {
            StatusChanged?.Invoke(this, status);
        }
        catch (Exception ex)
        {
            // 宿主或视图的处理器自爆,不该反过来把插件带崩。
            _context.Log.Error("A status handler threw.", ex);
        }
    }
}
