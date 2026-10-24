using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VelaShell.PluginSdk.Workspaces;

namespace VelaPlugin1;

/// <summary>
/// 工作台面板的视图。插件 UI 用的是**完整的 Avalonia**:编译期 AXAML、自带样式、第三方控件包都能用,
/// 唯一约束是 Avalonia 版本必须与宿主一致(由 SDK 包锁定)。
/// <para>
/// 由 <see cref="WorkspaceDocument.CreateView" /> 在 UI 线程构造。文档的事件在**后台线程**触发,
/// 这里一律经 <see cref="Dispatcher.UIThread" /> 封送回来再碰控件。
/// </para>
/// </summary>
public sealed partial class WorkspaceView : UserControl
{
    private readonly WorkspaceDocument _document;
    private readonly ObservableCollection<string> _transcript = [];
    private readonly ObservableCollection<string> _history = [];
    private bool _stateLoaded;

    internal WorkspaceView(WorkspaceDocument document)
    {
        _document = document;
        // InitializeComponent 由 Avalonia 的源生成器按 AXAML 生成:装载 XAML,并给 x:Name 的那些字段赋值。
        // ⚠️ 别再自己写一个无参的 InitializeComponent():重载决议会挑中它而不是生成的那个
        //    (后者带可选参数),XAML 照样装载,但 TitleText 之类的字段全是 null。
        InitializeComponent();

        Loc loc = document.Loc;
        TitleText.Text = document.Title;
        EndpointText.Text = document.Endpoint;
        ConnectionHeader.Text = loc["View_Connection"];
        HistoryHeader.Text = loc["View_History"];
        SendButton.Content = loc["View_Send"];
        InputBox.PlaceholderText = loc["View_InputPlaceholder"];
        ConnectionList.ItemsSource = document.DescribeConnection();
        TranscriptList.ItemsSource = _transcript;
        HistoryList.ItemsSource = _history;
        ShowStatus(document.Status);

        SendButton.Click += OnSendClicked;
        InputBox.KeyDown += OnInputKeyDown;
        InputBox.TextChanged += (_, _) => _document.Draft = InputBox.Text ?? "";
        HistoryList.SelectionChanged += OnHistorySelected;
        document.MessageReceived += line => Dispatcher.UIThread.Post(() => Append("← " + line));
        document.StatusChanged += (_, status) => Dispatcher.UIThread.Post(() => ShowStatus(status));
        // 状态在挂上可视树之后再读:构造期做 I/O 会让标签页出现前先卡一拍。
        // 停靠区拖动分栏时控件会摘下再挂上,Loaded 会再来一次 —— 只读一遍。
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_stateLoaded)
        {
            return;
        }
        _stateLoaded = true;
        try
        {
            WorkspaceState state = await _document.LoadStateAsync().ConfigureAwait(true);
            // ConfigureAwait(true):回到 UI 线程再碰控件。
            InputBox.Text = state.Draft;
            RefreshHistory();
        }
        catch (Exception ex)
        {
            // 事件处理器是 async void:异常必须就地接住,否则会以未处理异常的形式冒到宿主。
            Append(ex.Message);
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Send();
        }
    }

    private void OnSendClicked(object? sender, RoutedEventArgs e) => Send();

    private async void Send()
    {
        string line = InputBox.Text ?? "";
        if (line.Length == 0)
        {
            return;
        }
        try
        {
            await _document.SendAsync(line).ConfigureAwait(true);
            Append("→ " + line);
            InputBox.Text = "";
            RefreshHistory();
        }
        catch (Exception ex)
        {
            // 同上:async void 里的异常就地接住,把原因摆到用户眼前。
            Append(ex.Message);
        }
    }

    private void OnHistorySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is string line)
        {
            InputBox.Text = line;
            InputBox.CaretIndex = line.Length;
            _ = InputBox.Focus();
        }
    }

    private void RefreshHistory()
    {
        _history.Clear();
        foreach (string line in _document.History)
        {
            _history.Add(line);
        }
    }

    private void Append(string line)
    {
        _transcript.Add(line);
        TranscriptList.ScrollIntoView(_transcript.Count - 1);
    }

    private void ShowStatus(WorkspaceStatus status) =>
        StatusText.Text = status.Message ?? status.State.ToString();
}
