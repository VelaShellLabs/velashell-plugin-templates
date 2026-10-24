# VelaPlugin1

VelaShell 协议插件:给连接配置页添一种**终端协议**。插件 id:`PLUGIN_ID`。

生成出来的是一个能直接跑的最小骨架 —— 一条 TCP 之上的字节双工会话。连上任何回显服务,
敲的字就会被弹回终端;把传输换成你自己的协议,就是一个真正的协议插件。

## 骨架里有什么

| 文件 | 作用 | 要不要改 |
| --- | --- | --- |
| `plugin.json` | 清单:`contributes.protocols` 声明页签、`onProtocol:` 惰性激活、`hostMode: inProcess`、`minSdkVersion` | 改描述;加协议时同步 |
| `PluginMain.cs` | 入口:注册 `ProtocolDescriptor`(页签名、图标、默认端口、能力位),语言切换时重注册,停用时撤注册 | ✏️ `BuildDescriptor` |
| `ConnectionSettings.cs` | 连接表单的**声明**与**解析**,放在一起靠同一组键对齐 | ✏️ 换成你的字段 |
| `ProtocolTerminal.cs` | `IProtocolTerminal`:连接请求 → 设置 → 建会话;错误翻成 SDK 的异常族 | ✏️ 握手与错误翻译 |
| `ProtocolSession.cs` | `IProtocolTerminalSession`:读泵 + 输出通道 + 串行化写 + 能唤醒读的关闭 | ✏️ 换传输,纪律原样保留 |
| `Loc.cs` | 插件自带的中英文案表,按 `context.Host.Locale` 取 | ✏️ 加文案 / 加语言 |
| `VelaPlugin1.Tests/` | MSTest + `VelaShell.PluginSdk.Testing`:激活、表单、回显、掉线即 EOF、关闭唤醒读 | 随代码一起改 |
| `tools/EchoServer.cs` | 本机试跑用的回显服务(单文件程序,不进插件) | 可删 |

带 ✏️ 的地方在代码里也有同样的标记。

## 三步跑起来

```bash
dotnet tool install -g VelaShell.Plugin.Cli   # 只需装一次
dotnet build
vela-plugin dev init                          # 配好 IDE 启动配置
```

然后在 IDE 里按 **F5**(启动配置名 `VelaShell`):宿主以调试实例启动,用 `--dev-root` 挂上本工程的
构建输出、用独立数据根(`--data-root ~/.velashell-dev`),于是日常那份 VelaShell 可以一直开着。
**前提是本机至少完整启动过一次 VelaShell**(`dev init` 从 `~/.velashell/host.json` 找安装位置)。

在调试实例里试一下:

```bash
dotnet run --file tools/EchoServer.cs         # 另开一个终端;默认监听 127.0.0.1:7007
```

连接配置页 → **VelaPlugin1** 页签 → 主机填 `127.0.0.1`、端口 `7007` → 连接。敲的每个字都会被弹回来;
停掉回显服务,标签页转为"已断开",按 Enter 即重连。不开回显服务直接连,看到的就是连接失败的提示 ——
那一句话是 `ProtocolTerminal` 抛出的 `ProtocolConnectionException` 的消息。

`vela-plugin doctor` 一次性体检:宿主版本、清单兼容性、构建产物是否干净、启动配置是否可用。

## 测试

```bash
dotnet test VelaPlugin1.Tests
```

不起宿主:`TestPluginContext` 扮演宿主,`RecordingProtocols` 记下注册的描述与实现(并复刻宿主
`Register` 的 id / 端口校验),会话测试连的是测试里起的回显服务。

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

- **协议能力只在 `inProcess` 下可用**:终端的每次读写都是宿主反向调用插件。清单写成 `isolated` 会在发现期被拒。
- **插件 id 与协议 id 发布后都不要再改**:协议 id 落在用户的会话配置里,改名等于让老配置认不出自己的协议;
  表单字段的**键**同理。
- **掉线是 EOF,不是异常**:`ReadAsync` 返回 0,宿主才会把标签置为"已断开、按 Enter 重连"。
- **连不上抛 `ProtocolConnectionException`**,凭据错才抛 `ProtocolAuthenticationException`(宿主据此重弹登录框)。
- **语言切换要重注册,但实现实例沿用同一个**:换了实例,宿主会关掉这个协议名下所有开着的标签页。
- 用到新版 SDK 的面就抬 `plugin.json` 的 `minSdkVersion`(本模板用了 2.0.4 的 `PluginIcon`)。
- `VelaShell.PluginSdk.dll` 与 `Avalonia*.dll` 永远不进插件目录:装载器强制共享宿主那一套。

完整开发指南(协议能力见 §5.13):<https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/templates/dev-guide.md>
命令行工具手册:<https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/cli/cli.md>
真实规模的例子:[Telnet](https://github.com/VelaShellLabs/velashell-plugins/tree/main/plugins/VelaShell.Plugin.Telnet)
(RFC 854 协商)与[串口](https://github.com/VelaShellLabs/velashell-plugins/tree/main/plugins/VelaShell.Plugin.Serial)
(动态下拉、带外命令)。
