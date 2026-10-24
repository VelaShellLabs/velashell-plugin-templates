using Avalonia.Controls;
using Avalonia.Interactivity;
using VelaShell.PluginSdk;

namespace VelaPlugin1;

/// <summary>
/// 面板视图。插件 UI 用的是**完整的 Avalonia**:编译期 AXAML、自带样式、第三方控件包都能用,
/// 唯一约束是 Avalonia 版本必须与宿主一致(由 SDK 包锁定)。
/// </summary>
public sealed partial class DemoPanel : UserControl
{
    private readonly IPluginContext _context;

    /// <summary>由 <c>ShowPanelAsync</c> 的工厂在 UI 线程构造。</summary>
    public DemoPanel(IPluginContext context)
    {
        _context = context;
        // InitializeComponent 由 Avalonia 的源生成器按 AXAML 生成:装载 XAML,并给 x:Name 的那些字段
        // (CountSessionsButton / StatusText)赋值。别再自己写一个无参的 InitializeComponent():
        // 重载决议会挑中它而不是生成的那个(后者带可选参数),XAML 照样装载,字段却全是 null,
        // 下一行就是 NullReferenceException —— 面板根本开不出来。
        InitializeComponent();
        CountSessionsButton.Click += OnCountSessionsAsync;
    }

    private async void OnCountSessionsAsync(object? sender, RoutedEventArgs e)
    {
        try
        {
            var sessions = await _context.Sessions.ListAsync(_context.Shutdown).ConfigureAwait(true);
            // ConfigureAwait(true):回到 UI 线程再碰控件。
            StatusText.Text = $"{sessions.Count} session(s).";
        }
        catch (Exception ex)
        {
            // 事件处理器是 async void:异常必须就地接住,否则会以未处理异常的形式冒到宿主。
            _context.Log.Error("Counting sessions failed.", ex);
            StatusText.Text = "Failed - see the plugin log.";
        }
    }
}
