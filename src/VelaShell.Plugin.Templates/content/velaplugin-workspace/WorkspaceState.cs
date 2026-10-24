using VelaShell.PluginSdk.Storage;

namespace VelaPlugin1;

/// <summary>
/// 一条连接要跨会话留住的界面状态:输入框里没发出去的草稿,以及最近发送过的行。
/// <para>
/// 它经 <see cref="IPluginStorage" /> 做 JSON 往返,所以只放可序列化的纯数据。
/// ✏️ 换成你的面板要记住的东西(展开的树节点、上次的查询、列宽……)。
/// </para>
/// </summary>
internal sealed record WorkspaceState
{
    /// <summary>输入框里还没发出去的内容。</summary>
    public string Draft { get; init; } = "";

    /// <summary>最近发送过的行,新的在前。</summary>
    public string[] History { get; init; } = [];
}

/// <summary>
/// 状态的存取。
/// <para>
/// <b>SDK 没有"保存 / 还原工作台文档"的契约</b>:<c>IWorkspaceDocument</c> 只有
/// <c>CreateView</c> / <c>Status</c> / <c>ReconnectAsync</c> / <c>DisposeAsync</c>,宿主也不会在重启后
/// 替插件重开标签页。第一方插件(Redis 的收藏与命令历史)的做法是:**按连接端点**把界面状态写进
/// 插件私有存储(<c>context.Storage</c>,落宿主 SonnetDB,按插件 id 隔离,卸载时一并清掉),
/// 下次打开同一个端点时读回来。本类就是这个做法的最小形态。
/// </para>
/// <para>
/// 为什么按端点而不按 <c>WorkspaceConnectRequest.SessionId</c>:那是宿主**每次打开**时新分配的
/// 不透明 id,下次打开同一条配置就换了。端点(走隧道时取真实目标)才是"同一台服务器"。
/// </para>
/// <para>
/// 存储不可用(headless 宿主、配额)时**静默降级**:状态只在本次会话内有效,而不是让面板打不开。
/// </para>
/// </summary>
/// <param name="storage">插件私有存储。</param>
internal sealed class WorkspaceStateStore(IPluginStorage storage)
{
    /// <summary>读回某个端点的状态;没有存过或读失败时给一份空状态。</summary>
    /// <param name="connectionKey">连接标识(端点)。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>状态。</returns>
    public async Task<WorkspaceState> LoadAsync(string connectionKey, CancellationToken cancellationToken = default)
    {
        try
        {
            return await storage.GetAsync<WorkspaceState>(Key(connectionKey), cancellationToken).ConfigureAwait(false)
                   ?? new WorkspaceState();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new WorkspaceState();
        }
    }

    /// <summary>保存某个端点的状态;失败返回 <see langword="false" />,由调用方决定要不要记日志。</summary>
    /// <param name="connectionKey">连接标识(端点)。</param>
    /// <param name="state">状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否存成功。</returns>
    public async Task<bool> SaveAsync(string connectionKey, WorkspaceState state, CancellationToken cancellationToken = default)
    {
        try
        {
            await storage.SetAsync(Key(connectionKey), state, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static string Key(string connectionKey) => "state:" + connectionKey;
}
