# VelaShell 插件模板

[VelaShell](https://github.com/joesdu/VelaShell) 插件的 `dotnet new` 模板,以及**面向插件作者的
主文档**(开发指南与打包发布)。

| 包 | 内容 |
| --- | --- |
| [`VelaShell.Plugin.Templates`](https://www.nuget.org/packages/VelaShell.Plugin.Templates) | `velaplugin`(基础)与 `velaplugin-ui`(带 Avalonia 面板) |

## 快速上手

```bash
dotnet new install VelaShell.Plugin.Templates
dotnet new velaplugin-ui -n MyPlugin --publisher acme --authorName "Your Name"
cd MyPlugin
dotnet build -t:PackVpx          # 出 bin/vpx/*.vpx
```

生成的工程只引用一个包(`VelaShell.PluginSdk.Build`),契约程序集、与宿主版本一致的
Avalonia、清单校验与打包器都随它到位。

细节看 [`docs/dev-guide.md`](docs/dev-guide.md);发布与签名看
[`docs/publishing.md`](docs/publishing.md);英文版在 [`docs-en/`](docs-en/)。

## 插件生态的仓库分布

2026-08-27 起工具链按发布节奏拆成三个仓库,各有各的版本号,**不要求同步发版**:

| 仓库 | 产出 | 什么时候发 |
| --- | --- | --- |
| [`velashell-plugin-sdk`](https://github.com/VelaShellLabs/velashell-plugin-sdk) | `VelaShell.PluginSdk`、`.Testing` | 契约有增删改时 |
| [`velashell-plugin-cli`](https://github.com/VelaShellLabs/velashell-plugin-cli) | `VelaShell.Plugin.Cli`(`vela-plugin`)、`VelaShell.PluginSdk.Build` | 工具/打包/MSBuild 逻辑变化时 |
| **本仓库** `velashell-plugin-templates` | `VelaShell.Plugin.Templates` | 模板内容变化,或要把新建工程指到新版 Build 包时 |

依赖方向是单向的,没有环:

```
velashell-plugin-sdk        契约
        ↓ NuGet
velashell-plugin-cli        vela-plugin + VelaShell.PluginSdk.Build
        ↓ NuGet(仅一个版本字符串 + 冒烟时真的还原一次)
velashell-plugin-templates  ← 本仓库
```

另外两个相关仓库:[joesdu/VelaShell](https://github.com/joesdu/VelaShell)(宿主主程序)、
[VelaShellLabs/velashell-plugins](https://github.com/VelaShellLabs/velashell-plugins)(第一方插件)。

## 本仓库有两个版本号,别混

单仓库时代它们是同一个数字,拆库之后必须分开 —— 「改了模板文案」与「换了插件作者的
构建工具链」是性质完全不同的两种变更,合成一个数字之后就没法只做前一件。

| 属性(`Directory.Build.props`) | 含义 | 什么时候动 |
| --- | --- | --- |
| `VelaTemplatesVersion` | 模板包自己的版本 | 每次发版。就是 Release 标签里那个 |
| `VelaBuildPackageVersion` | 生成的工程要引用哪一版 `VelaShell.PluginSdk.Build` | cli 仓库发了新版 `.Build`,而你想让新建工程指过去 |

第二个有五处落点(props、两个 `template.json`、两份 `dev-guide.md`),由脚本统一维护:

```powershell
pwsh scripts/Set-Version.ps1 -BuildPackageVersion 1.5.2   # 只换 Build 包引用
pwsh scripts/Set-Version.ps1 1.5.1                        # 只发一版模板
pwsh scripts/Set-Version.ps1 -Check                       # 体检(CI 用的就是这个)
```

漏改 `template.json` 那两处的话,新建出来的工程会去还原一个旧版包 —— 构建期由 `VELA1004`
拦下。漏改文档那两处不报错,但文档是给人照抄的。

**不抬 `VelaBuildPackageVersion` 完全正常**:新建的工程继续引用上一版 `.Build`,那是可用的,
不是遗漏。

## 在本仓库里开发

```bash
dotnet build VelaShell.Plugin.Templates.slnx

# 端到端冒烟:装上刚打出的模板包,像插件作者一样走一遍(两个模板都走)
dotnet pack src/VelaShell.Plugin.Templates/VelaShell.Plugin.Templates.csproj -c Release -o artifacts/nuget
pwsh scripts/Invoke-Smoke.ps1 -Feed ./artifacts/nuget -Version 1.5.0 -BuildPackageVersion 1.5.0
```

冒烟不是形式:模板包是纯内容包,`dotnet pack` 成功**几乎什么都不能说明** —— 真正会出问题的
地方全在包装好之后(`.template.config` 以点开头,没开 `NoDefaultExcludes` 就不会进包,
而且不报错,只是 `dotnet new` 列表里看不见它)。

想验一版还没发布的 `VelaShell.PluginSdk.Build`:在 cli 仓库
`dotnet pack -o <这里>/artifacts/nuget`,冒烟脚本会把那个目录当作还原源之一。

本仓库**不产出程序集**,因此既不签名也不需要任何机密。

## 发版

```powershell
pwsh scripts/Set-Version.ps1 1.5.1     # 落版本号,连同改动合进 main
                                        # 再在 GitHub 上发 Release,标签 v1.5.1
```

完整流程见 [`docs/release-process.md`](docs/release-process.md)。

## 许可

AGPL-3.0-only,见 [LICENSE](LICENSE)。
