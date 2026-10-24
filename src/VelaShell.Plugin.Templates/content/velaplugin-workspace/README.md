# VelaPlugin1

VelaShell 工作台插件:给连接配置页添一种**非文件型连接类型**,打开会话时由插件交出一个
Avalonia 面板挂成停靠文档。插件 id:`PLUGIN_ID`。

生成出来的是一个能直接跑的最小骨架 —— 一个按行收发的 TCP 客户端面板:左边是收发记录与输入行,
右边是这条连接的配置摘要与"最近发送"(按服务器跨会话留存)。把 `LineConnection` 换成你的
客户端库、把面板换成你的界面,就是一个真正的工作台插件(Redis、MySQL、Kafka……)。

## 骨架里有什么

| 文件 | 作用 | 要不要改 |
| --- | --- | --- |
| `plugin.json` | 清单:`contributes.workspaces` 声明页签、`onWorkspace:` 惰性激活、`hostMode: inProcess`、`minSdkVersion` | 改描述;加类型时同步 |
| `PluginMain.cs` | 入口:注册 `WorkspaceDescriptor`(页签名、图标、默认端口、能力位),语言切换时用**同一个** provider 重注册,停用时撤注册 | ✏️ `BuildDescriptor` |
| `ConnectionSettings.cs` | 连接表单的**声明**与**解析**,放在一起靠同一组键对齐;含一个"经 SSH 隧道"字段 | ✏️ 换成你的字段 |
| `WorkspaceProvider.cs` | `IWorkspaceProvider`:连接请求 → 返回前连上 → 交出文档;错误翻成 SDK 的异常族 | ✏️ 错误翻译 |
| `LineConnection.cs` | 到服务端的连接(TCP 按行收发) | ✏️ **最该换掉的一个类** |
| `WorkspaceDocument.cs` | `IWorkspaceDocument`:状态圆点、重连、关闭;收发;连接配置摘要;状态的读回与保存 | 随业务改 |
| `WorkspaceState.cs` | 跨会话留存的界面状态,按端点存进 `context.Storage` | ✏️ 换成你要记住的东西 |
| `WorkspaceView.axaml(.cs)` | 面板:主题令牌取色、后台事件封送回 UI 线程 | ✏️ 你的界面 |
| `Loc.cs` | 插件自带的中英文案表,按 `context.Host.Locale` 取 | ✏️ 加文案 / 加语言 |
| `VelaPlugin1.Tests/` | MSTest + `VelaShell.PluginSdk.Testing`:激活、表单、收发、状态留存、断开与重连 | 随代码一起改 |
| `tools/EchoServer.cs` | 本机试跑用的回显服务(单文件程序,不进插件) | 可删 |

带 ✏️ 的地方在代码里也有同样的标记。

### 关于"状态保存 / 还原"

SDK **没有**保存 / 还原工作台文档的契约:`IWorkspaceDocument` 只有 `CreateView` / `Status` /
`ReconnectAsync` / `DisposeAsync`,宿主也不会在重启后替插件重开标签页。第一方插件的做法(Redis 的
收藏与命令历史)是按**连接端点**把界面状态写进插件私有存储,下次打开同一个端点时读回来 ——
本模板的 `WorkspaceState` 就是这个做法的最小形态。不要拿 `WorkspaceConnectRequest.SessionId` 当键:
那是宿主每次打开时新分配的 id。

## 三步跑起来

```bash
dotnet tool install -g VelaShell.Plugin.Cli   # 只需装一次
dotnet build
vela-plugin dev init                          # 配好 IDE 启动配置
```

然后在 IDE 里按 **F5**(启动配置名 `VelaShell`):宿主以调试实例启动,用 `--dev-root` 挂上本工程的
构建输出、用独立数据根(`--data-root ~/.velashell-dev`),于是日常那份 VelaShell 可以一直开着。
**前提是本机至少完整启动过一次 VelaShell**(`dev init` 从 `~/.velashell/host.json` 找安装位置)。
inProcess 插件的断点直接命中,包括 `ActivateAsync` 第一行。

在调试实例里试一下:

```bash
dotnet run --file tools/EchoServer.cs         # 另开一个终端;默认监听 127.0.0.1:7007
```

连接配置页 → **VelaPlugin1** 页签 → 地址 `127.0.0.1`、端口 `7007` → 连接(先点「测试」也行)。
输入一行回车,回显出现在记录里;关掉标签页再打开,"最近发送"还在。停掉回显服务,标签页的状态圆点变红,
点"重连"恢复。要试 SSH 隧道:在"经 SSH 隧道"里选一条已保存的 SSH 连接,地址填**从那台机器看过去**的目标。

`vela-plugin doctor` 一次性体检:宿主版本、清单兼容性、构建产物是否干净、启动配置是否可用。

## 测试

```bash
dotnet test VelaPlugin1.Tests
```

不起宿主:`TestPluginContext` 扮演宿主,`RecordingWorkspaces` 记下注册的描述与 provider(并复刻宿主
`Register` 的 id / 端口 / 指纹字段校验),`InMemoryStorage` 与宿主的存储同样走 JSON 往返。
测试刻意不碰面板(Avalonia),要测面板的做法见测试工程 csproj 里的注释。

## 改了代码之后

```bash
dotnet build
```

回到 VelaShell 的插件管理页,在本插件那一行点 **重新加载** —— 不必重启宿主,清单也一起重读。
想更省事:`vela-plugin dev init --watch`,重编后自动重载。

## 出包

```bash
dotnet build -c Release -t:PackVpx
# → bin/vpx/PLUGIN_ID-0.1.0.vpx

# 带签名(密钥用 `vela-plugin keygen` 生成,别提交进仓库)
dotnet build -c Release -t:PackVpx -p:VelaSigningKey=/path/to/key.pem
```

测试工程与 `tools/` 不进包。`.vpx` 在 VelaShell 的插件管理页"安装 .vpx…"一键装上。

## 要点

- **工作台能力只在 `inProcess` 下可用**:原生控件无法跨进程嵌入。清单写成 `isolated` 会在发现期被拒。
- **插件 id 与连接类型 id 发布后都不要再改**;表单字段的**键**同理 —— 它们都落在用户的会话配置里。
- **`OpenAsync` 返回前要连上**,而且要能被反复调用、不留副作用(「测试」按钮就是开一次再立刻关)。
- **认证失败单独认出来**抛 `ProtocolAuthenticationException`,宿主才会重弹登录框。
- **语言切换要重注册,但 provider 沿用同一个实例**:换了实例,宿主会关掉这个类型名下所有开着的文档。
- **凭据是一次性的**:`request.Password` 只在 `OpenAsync` 那一刻交给你,不要落盘、不要进日志、不要显示。
- 面板里的颜色、字号一律 `{DynamicResource Vela*}`;后台线程来的事件经 `Dispatcher.UIThread` 封送。
- 用到新版 SDK 的面就抬 `plugin.json` 的 `minSdkVersion`(本模板用了 2.0.4 的 `PluginIcon`)。
- `VelaShell.PluginSdk.dll` 与 `Avalonia*.dll` 永远不进插件目录:装载器强制共享宿主那一套。

完整开发指南(工作台能力见 §5.14):<https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/templates/dev-guide.md>
命令行工具手册:<https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/cli/cli.md>
真实规模的例子:[Redis](https://github.com/VelaShellLabs/velashell-plugins/tree/main/plugins/VelaShell.Plugin.Redis)
(键空间浏览器、声明式隧道、连接提议)。
