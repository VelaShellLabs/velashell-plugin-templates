# AGENTS.md

> 给 AI 代理与新加入者的操作约定。**动手之前先读完本文件,以及它指向的文档。**

## 一、开工前必读:velashell-docs

VelaShell 生态的**全部文档**集中在一个仓库:
**[VelaShellLabs/velashell-docs](https://github.com/VelaShellLabs/velashell-docs)**。
本仓库**不放** `docs/`、`docs-en/` —— 设计手册、开发规范与开发文档都在那边。

**在动任何代码之前**,先把下表中与你要改的部分相关的几篇读掉。跳过这一步直接改,
结果通常是两种:与既有设计冲突,或者重复实现一个已经存在的能力。

| 位置 | 内容 |
| --- | --- |
| [`zh/host/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/host) | 宿主分层架构与依赖方向、工程化重构蓝图、交互与界面规格、快捷键参考、设置项审计,以及 SFTP / FTP / Telnet / 串口 / Redis / S3 / 系统密钥链等可行性调研 |
| [`zh/plugins/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/plugins) | 插件系统设计蓝图 01–15(进程模型、IPC 协议、权限系统、UI 扩展、威胁模型、路线图)与[进度总览 STATUS](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/plugins/STATUS.md) |
| [`zh/sdk/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/sdk) | 插件契约 SDK 参考、SDK 仓库的发版流程 |
| [`zh/cli/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/cli) | `vela-plugin` 命令行手册、CLI 仓库的发版流程 |
| [`zh/templates/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/zh/templates) | 插件开发指南、打包与发布、模板仓库的发版流程 |

英文镜像在 [`en/`](https://github.com/VelaShellLabs/velashell-docs/tree/main/en),与 `zh/` 同构。
[仓库首页](https://github.com/VelaShellLabs/velashell-docs)有按「我想做什么」组织的快速入口表。

## 二、涉及文档的改动一律同步到 velashell-docs

**这是本文件最重要的一条。**

- 本仓库里**不新建** `docs/`、`docs-en/` 或任何成体系的文档目录。要写文档,去 velashell-docs 开 PR。
- 改了代码,而**行为、接口、配置项、命令行、构建流程或版本纪律**与现有文档对不上时,
  必须**同时**在 velashell-docs 提一个 PR 把文档改过来。两个 PR 在正文里互相引用,一起合。
  只改代码不改文档,等于让文档开始骗人 —— 而文档是别人照抄的。
- velashell-docs 的 `zh/` 与 `en/` 是**互为镜像**的两棵树,文件一一对应。改了中文就要改英文,
  反之亦然。漏一边,两棵树就开始漂。
- velashell-docs 内部的互相引用**一律走相对路径**(如 `../templates/dev-guide.md`),
  不要写回 GitHub 绝对 URL —— 文档集中到一个仓库,消掉的正是那种一改路径就断的跨仓库链接。
- **例外**:留在代码仓库里的少数几份文件不适用上述规则,因为它们服务的是「在这个仓库里写代码」
  这件事,搬走只会离使用场景更远。各仓库的例外清单见下面第三节。

## 三、本仓库:velashell-plugin-templates(dotnet new 模板)

产出 `VelaShell.Plugin.Templates`:`velaplugin`(基础)与 `velaplugin-ui`(带 Avalonia 面板)。

### 构建与冒烟

```bash
dotnet build VelaShell.Plugin.Templates.slnx

# 冒烟:装上刚打出的模板包,像插件作者一样走一遍(两个模板都走)
dotnet pack src/VelaShell.Plugin.Templates/VelaShell.Plugin.Templates.csproj -c Release -o artifacts/nuget
pwsh scripts/Invoke-Smoke.ps1 -Feed ./artifacts/nuget -Version <版本> -BuildPackageVersion <版本>
```

冒烟不是形式:模板包是纯内容包,`dotnet pack` 成功**几乎什么都不能说明** —— 真正会出问题的
地方全在包装好之后(`.template.config` 以点开头,没开 `NoDefaultExcludes` 就不会进包,
而且不报错,只是 `dotnet new` 列表里看不见它)。

### 两个版本号,别混

| 属性(`Directory.Build.props`) | 含义 | 什么时候动 |
| --- | --- | --- |
| `VelaTemplatesVersion` | 模板包自己的版本 | 每次发版 |
| `VelaBuildPackageVersion` | 生成的工程引用哪一版 `VelaShell.PluginSdk.Build` | cli 仓库发了新版且你想让新建工程指过去 |

**不抬 `VelaBuildPackageVersion` 完全正常**,不是遗漏。

### 五处落点里有两处在 velashell-docs

`VelaBuildPackageVersion` 的落点:`Directory.Build.props`、两个 `template.json`,
以及**另一个仓库里的** `zh/templates/dev-guide.md` 与 `en/templates/dev-guide.md` 的
`PackageReference` 片段。`scripts/Set-Version.ps1` 按 `-DocsRoot` →
`$env:VELASHELL_DOCS` → 同级 `../velashell-docs` 找,找不到就跳过并警告。

漏改 `template.json` 那两处:新建工程会还原一个旧版包,构建期由 `VELA1004` 拦下。
漏改文档那两处:不报错,但文档是给人照抄的,过期版本号会被原样粘进别人的工程。

**改了模板的结构、参数或生成结果,必须同步改 velashell-docs 的
[`dev-guide.md`](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/templates/dev-guide.md)
(中英各一份)** —— 面向插件作者的主文档就是它。

完整流程见 [`zh/templates/release-process.md`](https://github.com/VelaShellLabs/velashell-docs/blob/main/zh/templates/release-process.md)。

### 留在本仓库的文档

`README.md`、`LICENSE`,以及模板内容里给使用者看的 `content/*/README.md`
(它们随 `dotnet new` 生成到用户工程里,必须留在包内)。
