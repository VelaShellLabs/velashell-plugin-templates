# Documentation index

**The main documentation for writing VelaShell plugins lives here.** Chinese version: [`../docs/`](../docs/).

| Document | Contents |
| --- | --- |
| [dev-guide.md](dev-guide.md) | **Development guide**: getting started, manifest, lifecycle, capability APIs, isolation modes, testing, deployment, performance discipline |
| [publishing.md](publishing.md) | **Packaging and publishing**: Release builds, `.vpx`, signing and trust, publishing to the [marketplace](http://market.easilynet.top), CI packaging |
| [../docs/release-process.md](../docs/release-process.md) | **How this repository releases** (Chinese only) |

Writing your first plugin? Read `dev-guide.md` → [CLI manual](https://github.com/VelaShellLabs/velashell-plugin-cli/blob/main/docs-en/cli.md) → `publishing.md`, in that order.

## Two documents that live elsewhere

After the 2026-08-27 split, these moved next to the packages they describe — each carries the
**version banner of its own package**, so keeping them here would mean every release of those
repositories needs a commit in this one:

| Document | Where it lives now |
| --- | --- |
| **`vela-plugin` manual** | [velashell-plugin-cli / docs-en/cli.md](https://github.com/VelaShellLabs/velashell-plugin-cli/blob/main/docs-en/cli.md) |
| **SDK reference** | [velashell-plugin-sdk / docs-en/sdk-reference.md](https://github.com/VelaShellLabs/velashell-plugin-sdk/blob/main/docs-en/sdk-reference.md) |

## What is not here

The plugin system's **architecture documents** (process model, IPC protocol, permissions, UI
extension, threat model, roadmap — the 01–15 series) stay in the host repository:
<https://github.com/joesdu/VelaShell/tree/main/docs/plugins>

Those describe the **host side**. Read them to understand why plugins look the way they do;
you do not need them to write one.
