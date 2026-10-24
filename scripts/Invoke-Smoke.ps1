#Requires -Version 7.0
<#
.SYNOPSIS
    端到端冒烟:装上刚打出的模板包,像插件作者一样新建工程、构建、出 .vpx。

.DESCRIPTION
    模板包是纯内容包,`dotnet pack` 成功**几乎什么都不能说明** —— 模板真正会出问题的地方
    全在包装好之后:

      · .template.config 以点开头,没开 NoDefaultExcludes 的话根本不会进包,
        而且不报错,只是 `dotnet new` 列表里看不见它;
      · sdkVersion 的默认值指向一个不存在(或已过期)的 VelaShell.PluginSdk.Build 版本;
      · 模板里的示例代码用到了新版契约的 API,而它引用的 Build 包还是老的。

    所以这一步必须真的装一次、真的生成一次、真的构建一次。

    四个模板都走一遍:velaplugin(基础)、velaplugin-ui(带 Avalonia 面板)、
    velaplugin-protocol(终端协议)与 velaplugin-workspace(工作台连接类型)。
    带面板的两个额外覆盖编译期 AXAML 那条链路。

    后两个模板自带测试工程(<名称>.Tests/,与插件工程同目录):这里连它一起跑
    `dotnet test` —— 它们连的是测试里起的环回回显服务,覆盖的是"注册出去的描述对不对、
    会话真的收发得了字节、掉线是 EOF、文档状态能留存"这些只编译验不出来的东西。
    测试工程**不进 .vpx**:下面的共享程序集体检照样只看插件自己的输出目录。

.PARAMETER Feed
    本地 NuGet 源目录,里面应有刚打出的 VelaShell.Plugin.Templates.<版本>.nupkg。
    这个目录同时会被生成的工程当作还原源之一 —— 于是想连带验一版还没发布的
    VelaShell.PluginSdk.Build 时,把它的 .nupkg 也丢进来即可。

.PARAMETER Version
    要装的模板包版本。

.PARAMETER BuildPackageVersion
    传给 `dotnet new --sdkVersion` 的值,即生成的工程要引用的
    VelaShell.PluginSdk.Build 版本。不给就用模板自己的默认值。

.PARAMETER WorkDirectory
    工作目录。默认取 RUNNER_TEMP(CI)或系统临时目录。

.EXAMPLE
    pwsh scripts/Invoke-Smoke.ps1 -Feed ./artifacts/nuget -Version 1.5.0 -BuildPackageVersion 1.5.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Feed,
    [Parameter(Mandatory)] [string] $Version,
    [string] $BuildPackageVersion,
    [string] $WorkDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$feedPath = (Resolve-Path $Feed).Path
$nupkg = Join-Path $feedPath "VelaShell.Plugin.Templates.$Version.nupkg"
if (-not (Test-Path $nupkg)) { throw "找不到模板包:$nupkg" }

if (-not $WorkDirectory) {
    $WorkDirectory = Join-Path ($env:RUNNER_TEMP ?? [IO.Path]::GetTempPath()) 'vela-templates-smoke'
}
if (Test-Path $WorkDirectory) { Remove-Item -Recurse -Force $WorkDirectory }
New-Item -ItemType Directory -Force $WorkDirectory | Out-Null

Write-Host "== 冒烟:VelaShell.Plugin.Templates $Version =="
Write-Host "   源     $feedPath"
Write-Host "   工作区 $WorkDirectory"
if ($BuildPackageVersion) { Write-Host "   Build 包 $BuildPackageVersion" }

# <clear /> 很关键:不清掉的话机器上已有的源可能把**上一版**同名包喂进来。
# nuget.org 仍要留着 —— VelaShell.PluginSdk.Build 与 Avalonia 都从那来。
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feedPath" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content (Join-Path $WorkDirectory 'nuget.config')

# 打包器随 VelaShell.PluginSdk.Build 分发到包的 tools/。
# 还原之后它就躺在全局包目录里,用它把 .vpx 读回来复核一遍容器。
# 文件名换过一次:2026-09-11 起 .Build 搬进 velashell-plugin-sdk,tools/ 里放的是
# VelaShell.PluginSdk.Packer.dll(同样有 info 子命令);此前的包里是 vela-plugin 本体
# VelaShell.Plugin.Cli.dll。两个都找 —— 只认旧名的话,读回这一步会对新包一直静默跳过。
function Get-BundledCli([string] $buildVersion) {
    $localsLine = (dotnet nuget locals global-packages --list) -join "`n"
    $m = [regex]::Match($localsLine, 'global-packages:\s*(.+)')
    if (-not $m.Success) { return $null }
    $tools = Join-Path $m.Groups[1].Value.Trim() "velashell.pluginsdk.build/$buildVersion/tools/net11.0"
    foreach ($name in 'VelaShell.PluginSdk.Packer.dll', 'VelaShell.Plugin.Cli.dll') {
        $candidate = Join-Path $tools $name
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

dotnet new install $nupkg
if ($LASTEXITCODE -ne 0) { throw "装模板失败。" }

try {
    foreach ($template in 'velaplugin', 'velaplugin-ui', 'velaplugin-protocol', 'velaplugin-workspace') {
        Write-Host ""
        Write-Host "-- $template --"
        $projectName = 'Smoke' + ($template -replace '[^A-Za-z0-9]', '')
        $newArgs = @($template, '-n', $projectName, '-o', (Join-Path $WorkDirectory $projectName),
                     '--publisher', 'ci', '--authorName', 'CI')
        if ($BuildPackageVersion) { $newArgs += @('--sdkVersion', $BuildPackageVersion) }

        dotnet new @newArgs
        if ($LASTEXITCODE -ne 0) { throw "`dotnet new $template` 失败。" }

        Push-Location (Join-Path $WorkDirectory $projectName)
        try {
            # nuget.config 在工作区根,生成的工程在它下一层,还原时会向上找到 —— 与
            # 插件作者在自己机器上的情形一致(他们的源来自机器级配置)。
            dotnet build -c Release -t:PackVpx --nologo
            if ($LASTEXITCODE -ne 0) { throw "$template 生成的工程构建失败。" }

            $vpx = Get-ChildItem 'bin/vpx/*.vpx' -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $vpx) { throw "${template}:PackVpx 没产出 .vpx。" }
            Write-Host "   产物 $($vpx.Name)"

            # 共享程序集绝不能出现在插件输出目录 —— 出现了就说明 exclude=Runtime 的链路断了。
            $leaked = Get-ChildItem 'bin/Release/net11.0' -Filter '*.dll' |
                Where-Object { $_.Name -like 'Avalonia*' -or $_.Name -eq 'VelaShell.PluginSdk.dll' }
            if ($leaked) { throw "${template}:共享程序集漏进了插件输出目录:$($leaked.Name -join ', ')" }

            # 自带测试工程的模板:生成出来的测试必须能直接跑绿。
            $testProject = "$projectName.Tests"
            if (Test-Path $testProject) {
                dotnet test $testProject -c Release --nologo
                if ($LASTEXITCODE -ne 0) { throw "$template 生成的测试工程没跑绿。" }

                # 测试工程会顺带重编一次插件(ProjectReference):再查一遍,确认没把测试替身或
                # 测试框架带进插件的输出目录。
                $strays = Get-ChildItem 'bin/Release/net11.0' -Filter '*.dll' |
                    Where-Object { $_.Name -ne "$projectName.dll" }
                if ($strays) { throw "${template}:插件输出目录里多出了别的程序集:$($strays.Name -join ', ')" }
            }

            $cli = Get-BundledCli ($BuildPackageVersion ? $BuildPackageVersion : '')
            if ($cli) {
                dotnet $cli info $vpx.FullName
                if ($LASTEXITCODE -ne 0) { throw "${template}:打包器的 info 读不回刚打出的 .vpx。" }
            }
            else {
                # 不是硬失败:上面的 PackVpx 已经证明打包器跑得起来,info 只是再读一遍容器。
                # 找不到通常是因为没传 -BuildPackageVersion(于是不知道该去哪个版本目录找)。
                Write-Host "::notice::未在全局包目录定位到随包分发的打包器,跳过容器读回校验。"
            }
        }
        finally { Pop-Location }
    }
    Write-Host ""
    Write-Host "== 冒烟通过 =="
}
finally {
    # 装到机器级模板列表里的东西一定要卸干净,否则本地重复跑会撞上"已装同名模板"。
    dotnet new uninstall VelaShell.Plugin.Templates 2>&1 | Out-Null
}

exit 0
