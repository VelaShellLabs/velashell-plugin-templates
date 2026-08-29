#Requires -Version 7.0
<#
.SYNOPSIS
    维护本仓库的两个版本号:模板包自己的版本,以及生成的工程要引用的 Build 包版本。

.DESCRIPTION
    拆库(2026-08-27)之后本仓库有**两个互不相干**的版本号,单仓库时代它们是同一个数字:

      ① VelaTemplatesVersion    模板包自己的版本。改了模板文案就该动它。
                                落点只有 Directory.Build.props 一处。

      ② VelaBuildPackageVersion `dotnet new velaplugin` 生成的工程要引用哪一版
                                VelaShell.PluginSdk.Build。落点五处:
                                  Directory.Build.props
                                  content/velaplugin/.template.config/template.json
                                  content/velaplugin-ui/.template.config/template.json
                                  zh/templates/dev-guide.md ┐ 这两处在 velashell-docs 仓库,
                                  en/templates/dev-guide.md ┘ 是**可选**落点,见 -DocsRoot

    把它俩分开是拆库的**要点**:「改了模板文案」与「换了插件作者的构建工具链」是性质
    完全不同的两种变更,合成一个数字之后就没法只做前一件。

    漏改 ② 的模板落点:新建出来的工程去还原一个旧版包,构建期由 VELA1004 拦下。
    漏改 ② 的文档落点:不报错,但文档是给人照抄的,过期版本号会被原样粘进别人的工程。
    2026-08-30 全部文档搬到 VelaShellLabs/velashell-docs 之后,那两处不在本仓库的
    checkout 里,所以找不到就跳过。

.PARAMETER Version
    模板包的目标版本(SemVer)。不给就读 Directory.Build.props 里的当前值。

.PARAMETER BuildPackageVersion
    生成的工程要引用的 VelaShell.PluginSdk.Build 版本(SemVer)。
    不给就读 Directory.Build.props 里的当前值 —— 于是"只发一版模板"时不必重复输入它。

.PARAMETER DocsRoot
    velashell-docs 仓库的位置,dev-guide 的 PackageReference 片段写在那里。默认先看
    $env:VELASHELL_DOCS,再看与本仓库同级的 ../velashell-docs。找不到就跳过那两处并
    提醒一句 —— 那是另一个仓库,CI 的 checkout 里本来就没有它,不该因此让流水线变红。

.PARAMETER Check
    只报告不落盘;有任何一处不同步就以退出码 1 结束。
    不带任何版本参数直接 -Check,就是拿 Directory.Build.props 里的当前值体检全部落点,
    CI 用的就是这一形态。

.EXAMPLE
    pwsh scripts/Set-Version.ps1 1.5.1
    只发一版模板(比如改了模板里的注释),不动生成工程引用的 Build 包版本。

.EXAMPLE
    pwsh scripts/Set-Version.ps1 -BuildPackageVersion 1.5.2
    velashell-plugin-cli 发了新版 .Build,把新建工程指过去。模板包版本随后也要发一版
    才能让人拿到,所以通常紧跟一句 Set-Version.ps1 <新模板版本>。

.EXAMPLE
    pwsh scripts/Set-Version.ps1 -Check
    体检:全部落点是否与 Directory.Build.props 一致。
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)] [string] $Version,
    [string] $BuildPackageVersion,
    [string] $DocsRoot,
    [switch] $Check
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot

# ── velashell-docs 的位置 ────────────────────────────────────────────────────
# 2026-08-30 起全部文档搬到 VelaShellLabs/velashell-docs,dev-guide 的 PackageReference
# 片段跟着走了。那是另一个仓库,发版 runner 的 checkout 里没有它 —— 所以这两处是**可选**
# 落点:本地开发时两个仓库通常并排放着,找得到就一起改;找不到就在末尾提醒一句。
if (-not $DocsRoot) {
    $DocsRoot = if ($env:VELASHELL_DOCS) { $env:VELASHELL_DOCS }
                else { Join-Path (Split-Path -Parent $root) "velashell-docs" }
}
$docsAvailable = Test-Path (Join-Path $DocsRoot "zh")
$skippedDocs = [System.Collections.Generic.List[string]]::new()
$propsPath = Join-Path $root 'Directory.Build.props'
$propsText = [IO.File]::ReadAllText($propsPath)

function Read-Prop([string] $name) {
    $m = [regex]::Match($propsText, "<$name Condition=""[^""]*"">([^<]+)</$name>")
    if (-not $m.Success) { throw "在 Directory.Build.props 里找不到 <$name>。文件结构改过了?" }
    return $m.Groups[1].Value
}

# 没给就沿用仓库当前值 —— 于是「只改一个」是默认形态,不必每次两个都输。
if (-not $Version) { $Version = Read-Prop 'VelaTemplatesVersion' }
if (-not $BuildPackageVersion) { $BuildPackageVersion = Read-Prop 'VelaBuildPackageVersion' }

foreach ($pair in @(@{ N = 'Version'; V = $Version }, @{ N = 'BuildPackageVersion'; V = $BuildPackageVersion })) {
    if ($pair.V -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        throw "$($pair.N) '$($pair.V)' 不是合法 SemVer。用 1.5.1 或 1.6.0-preview.1 这种形式。"
    }
}

Write-Host "模板包版本      $Version"
Write-Host "引用的 Build 包 $BuildPackageVersion"

# ── 落点清单 ────────────────────────────────────────────────────────────────
# 每条都用**锚定到上下文**的模式,不做"全局替换旧版本号"。后者会误伤示例输出里那些
# 只是碰巧等于当前版本的数字。
$edits = [System.Collections.Generic.List[hashtable]]::new()

$edits.Add(@{
    Path    = 'Directory.Build.props'
    Pattern = '(?<pre><VelaTemplatesVersion Condition="[^"]*">)(?<val>[^<]+)(?<post></VelaTemplatesVersion>)'
    What    = 'VelaTemplatesVersion'
    Value   = $Version
})
$edits.Add(@{
    Path    = 'Directory.Build.props'
    Pattern = '(?<pre><VelaBuildPackageVersion Condition="[^"]*">)(?<val>[^<]+)(?<post></VelaBuildPackageVersion>)'
    What    = 'VelaBuildPackageVersion'
    Value   = $BuildPackageVersion
})
foreach ($template in 'velaplugin', 'velaplugin-ui') {
    $edits.Add(@{
        Path    = "src/VelaShell.Plugin.Templates/content/$template/.template.config/template.json"
        Pattern = '(?<pre>"sdkVersion":\s*\{[\s\S]*?"defaultValue":\s*")(?<val>[^"]+)(?<post>")'
        What    = 'sdkVersion.defaultValue'
        Value   = $BuildPackageVersion
    })
}
# PackageReference 片段:锚在包 id 上,两份文档各一处 —— 都在 velashell-docs 仓库。
foreach ($doc in "zh/templates/dev-guide.md", "en/templates/dev-guide.md") {
    $edits.Add(@{
        Repo    = "docs"
        Path    = $doc
        Pattern = '(?<pre><PackageReference Include="VelaShell\.PluginSdk\.Build" Version=")(?<val>[^"]+)(?<post>")'
        What    = 'PackageReference 片段'
        Value   = $BuildPackageVersion
    })
}

# ── 应用 ────────────────────────────────────────────────────────────────────
# 同一个文件可能有多条 edit(Directory.Build.props 就有两条),所以按文件累积、
# 最后统一写盘 —— 逐条写盘的话后一条读到的是前一条写完的内容,虽然也对,
# 但一旦中途抛异常就会留下改了一半的文件。
$pending = @{}
$changed = [System.Collections.Generic.List[object]]::new()

foreach ($edit in $edits) {
    $inDocs = $edit.ContainsKey("Repo") -and $edit.Repo -eq "docs"
    if ($inDocs -and -not $docsAvailable) { $skippedDocs.Add($edit.Path); continue }

    $path = if ($inDocs) { Join-Path $DocsRoot $edit.Path } else { Join-Path $root $edit.Path }
    if (-not (Test-Path $path)) { throw "落点文件不存在:$($edit.Path)" }

    if (-not $pending.ContainsKey($path)) { $pending[$path] = [IO.File]::ReadAllText($path) }
    $text = $pending[$path]

    $found = [regex]::Matches($text, $edit.Pattern)
    if ($found.Count -eq 0) {
        # 模式失配 = 文件结构变了而本脚本没跟上。静默跳过等于把"漏改一处"重新放回来,
        # 所以这里直接断掉,让人当场看见。
        throw "在 $($edit.Path) 里没匹配到「$($edit.What)」。文件结构改过了?请同步更新 scripts/Set-Version.ps1。"
    }

    $stale = @($found | Where-Object { $_.Groups['val'].Value -cne $edit.Value })
    if ($stale.Count -eq 0) { continue }

    $changed.Add([pscustomobject]@{
        File = if ($inDocs) { "velashell-docs/" + $edit.Path } else { $edit.Path }
        What = $edit.What
        From = (($stale | ForEach-Object { $_.Groups['val'].Value } | Select-Object -Unique) -join ', ')
        To   = $edit.Value
    })
    if ($Check) { continue }

    $pending[$path] = [regex]::Replace($text, $edit.Pattern, {
        param($m) $m.Groups['pre'].Value + $edit.Value + $m.Groups['post'].Value
    })
}

if (-not $Check) {
    foreach ($path in $pending.Keys) {
        # 保留文件原有的 BOM 状态,免得 diff 里多出一堆与版本号无关的整文件改动。
        $bytes = [IO.File]::ReadAllBytes($path)
        $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        [IO.File]::WriteAllText($path, $pending[$path], [Text.UTF8Encoding]::new($hasBom))
    }
}

if ($skippedDocs.Count -gt 0) {
    Write-Warning @"
没找到 velashell-docs(试过 $DocsRoot),跳过了这几处文档里的 PackageReference 片段:
$($skippedDocs -join [Environment]::NewLine)
文档在 https://github.com/VelaShellLabs/velashell-docs —— 把它 clone 到本仓库同级目录,
或用 -DocsRoot / `$env:VELASHELL_DOCS 指过去,再跑一次即可一并更新。
"@
}

if ($changed.Count -eq 0) {
    Write-Host "全部落点已同步,无需改动。"
    exit 0
}

$changed | Format-Table -AutoSize | Out-String | Write-Host

if ($Check) {
    Write-Host "::error::仓库里的版本号不同步(见上表)。跑 ``pwsh scripts/Set-Version.ps1 $Version -BuildPackageVersion $BuildPackageVersion`` 修正。"
    exit 1
}

Write-Host "已更新 $($changed.Count) 处落点。"

# 显式 exit 0,别靠"脚本正常结束"隐含成功。
# 调用方是 `& ./scripts/Set-Version.ps1 ...` 后面跟一句 if ($LASTEXITCODE) —— 而 .ps1
# **不调用 exit 就根本不会设置 $LASTEXITCODE**,它会原样保留调用方进程里的旧值。
# GitHub 的每个 pwsh 步骤都是全新进程,那里的旧值是 $null,于是 `$LASTEXITCODE -ne 0`
# 求值为真 —— 脚本明明改好了文件,步骤却报 exit code 1。
exit 0
