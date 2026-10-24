# VelaShell.Plugin.Templates

VelaShell 插件工程的 `dotnet new` 模板。

```bash
dotnet new install VelaShell.Plugin.Templates
```

| 模板 | 短名 | 内容 |
| --- | --- | --- |
| VelaShell plugin | `velaplugin` | 入口类 + 一条命令 + `plugin.json`,可直接出 `.vpx` |
| VelaShell plugin with a panel | `velaplugin-ui` | 上面的一切 + 一个 Avalonia 面板(AXAML) |
| VelaShell protocol plugin | `velaplugin-protocol` | 一种终端协议:声明式连接表单 + TCP 字节双工会话 + 错误翻译,带测试工程与本机回显服务 |
| VelaShell workspace plugin | `velaplugin-workspace` | 一种工作台连接类型:声明式表单(含 SSH 隧道)+ Avalonia 文档面板 + 按端点留存的状态,带测试工程与本机回显服务 |

```bash
# 基础插件(id = acme.snippets)
dotnet new velaplugin -n Snippets --publisher acme --authorName "Your Name"

# 带面板、跑在独立进程里
dotnet new velaplugin-ui -n Dashboard --publisher acme --hostMode isolated

# 连接配置页里多一个协议页签(会话开成终端标签)
dotnet new velaplugin-protocol -n Modbus --publisher acme

# 连接配置页里多一个连接类型(会话开成插件自己的面板)
dotnet new velaplugin-workspace -n Kafka --publisher acme
```

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `--publisher` | `acme` | 插件 id 的发布者段(小写) |
| `--authorName` | 空 | 插件管理页展示的作者(`author` 是模板引擎的保留名,故加了后缀) |
| `--hostMode` | `inProcess` | `inProcess` / `isolated`(仅 `velaplugin` / `velaplugin-ui`:协议与工作台能力只在 `inProcess` 下可用,那两个模板固定写死) |
| `--sdkVersion` | 当前版本 | 引用的 `VelaShell.PluginSdk.Build` 版本(自带测试工程的模板,`VelaShell.PluginSdk.Testing` 用同一版) |

插件 id 由 `--publisher` 与 `-n` 拼出并全部转小写(`acme` + `Snippets` → `acme.snippets`)。
**发布后不要再改 id**:它同时是命令 id 前缀与插件私有数据/机密的命名空间。

卸载模板:`dotnet new uninstall VelaShell.Plugin.Templates`。

完整开发指南:<https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/templates/dev-guide.md>

## 生成之后

```bash
cd Snippets
dotnet build
vela-plugin dev init      # 配好 IDE 启动配置,然后按 F5 断点调试
```

`dev init` 会读 `~/.velashell/host.json`(VelaShell 每次启动自己写的)找到本机安装,
生成的启动配置用独立数据根起一个调试实例,于是日常那份 VelaShell 可以一直开着。
改完代码 `dotnet build`,在插件管理页点"重新加载"即可跑上新代码。

- 命令行手册:<https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/cli/cli.md>
- 打包与发布:<https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/templates/publishing.md>
