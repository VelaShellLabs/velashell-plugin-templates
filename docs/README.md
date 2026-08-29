# 文档索引

**写 VelaShell 插件的主文档在这里。** 英文版见 [`../docs-en/`](../docs-en/)。

| 文档 | 内容 |
| --- | --- |
| [dev-guide.md](dev-guide.md) | **开发指南**:快速上手、清单、生命周期、能力 API、隔离模式、测试、部署、性能纪律 |
| [publishing.md](publishing.md) | **打包与发布**:Release 构建、`.vpx`、签名与信任、发布到[插件商店](http://market.easilynet.top)、CI 出包 |
| [release-process.md](release-process.md) | **本仓库自己怎么发版**:模板包的 Release 流程、两个版本号的分工、NuGet 可信发布配置 |

第一次写插件的话,按 `dev-guide.md` → [CLI 手册](https://github.com/VelaShellLabs/velashell-plugin-cli/blob/main/docs/cli.md) → `publishing.md` 的顺序读。

## 在别的仓库的两篇

拆库之后(2026-08-27),这两篇跟着自己描述的那个包走了 —— 它们各自带着**那个包的版本号
横幅**,留在这里的话,那两个仓库发一版就要来改本仓库的文档:

| 文档 | 去了哪 |
| --- | --- |
| **`vela-plugin` 手册** | [velashell-plugin-cli / docs/cli.md](https://github.com/VelaShellLabs/velashell-plugin-cli/blob/main/docs/cli.md) |
| **SDK 参考**(契约表面、能力域一览、版本历史) | [velashell-plugin-sdk / docs/sdk-reference.md](https://github.com/VelaShellLabs/velashell-plugin-sdk/blob/main/docs/sdk-reference.md) |

## 不在这里的东西

插件系统的**架构蓝图**(进程模型、IPC 协议、权限系统、UI 扩展、威胁模型、路线图,
编号 01–15 的那批)留在主仓库:
<https://github.com/joesdu/VelaShell/tree/main/docs/plugins>

那些文档描述的是**宿主侧**的设计与实现 —— 读它是为了理解插件为什么长这样,
写插件本身用不到。
