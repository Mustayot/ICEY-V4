<#
  build.ps1 —— 编译 ICEY 作弊面板插件（把第 3 节那条长命令固化下来）

  用法:
    powershell -ExecutionPolicy Bypass -File build.ps1                 # 编译并装到副本
    powershell -ExecutionPolicy Bypass -File build.ps1 -SideBuild       # 游戏在跑时:编到旁边,不覆盖
    powershell -ExecutionPolicy Bypass -File build.ps1 -InstallOriginal # 同时更新原版安装

  必须知道的坑（脚本已内置检查）:
    * /noconfig /nostdlib + 游戏自带 mscorlib  → 产物引用 mscorlib 2.0.5.0,匹配 Unity 5.4
    * 必须引用 Assembly-CSharp.dll 和 Assembly-CSharp-firstpass.dll（漏了会找不到 R / CameraFilterPack_*）
    * 元数据里不能出现游戏类型（否则 BepInEx 报 "0 plugins to load" 且无任何报错）
    * [BepInPlugin] 特性必须存在（丢了同样静默失败）
#>
param(
    [string]$Src,
    [switch]$SideBuild,
    [switch]$InstallOriginal
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ROOT     = Split-Path $PSScriptRoot -Parent          # ...\_moddev
$ORIGINAL = Split-Path $ROOT -Parent                  # ...\ICEY (原版)
if (-not $Src) { $Src = Join-Path $ROOT '_src\CheatPanel' }

$CSC   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$ILDSM = 'C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8.1 Tools\ildasm.exe'

# Managed 目录（副本优先，回退原版）
$managed = Join-Path $ROOT 'ICEY_Data\Managed'
if (-not (Test-Path (Join-Path $managed 'Assembly-CSharp.dll'))) {
    $managed = Join-Path $ORIGINAL 'ICEY_Data\Managed'
}
# BepInEx core：已安装就用安装好的，否则用 _tools 里的原包
$core = Join-Path $ROOT 'BepInEx\core'
if (-not (Test-Path (Join-Path $core 'BepInEx.dll'))) {
    $core = Join-Path $ROOT '_tools\BepInEx_5.4.23.5_x64\BepInEx\core'
}
if (-not (Test-Path (Join-Path $core 'BepInEx.dll'))) { throw "找不到 BepInEx core（试过 $ROOT\BepInEx\core 和 _tools）" }
if (-not (Test-Path (Join-Path $managed 'Assembly-CSharp.dll'))) { throw "找不到 Assembly-CSharp.dll（$managed）" }

# 源文件由目录决定(不再硬编码文件名):加新 partial 文件时不用改这个脚本
$files = @(Get-ChildItem -Path $Src -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
if ($files.Count -eq 0) { throw "源目录里没有 .cs 文件: $Src" }
foreach ($f in $files) { if (-not (Test-Path $f)) { throw "缺源文件: $f" } }

$dst = Join-Path $ROOT 'BepInEx\plugins\IceyCheatPanel.dll'
$new = Join-Path $ROOT '_work\IceyCheatPanel.new.dll'
if ($SideBuild) { $out = $new } else { $out = $dst }
New-Item -ItemType Directory -Path (Split-Path $out -Parent) -Force | Out-Null

Write-Host "== 编译 =="
Write-Host "   源码 : $Src"
Write-Host "   引用 : $managed"
Write-Host "   core : $core"
Write-Host "   输出 : $out"

$refs = @(
    "$managed\mscorlib.dll", "$managed\System.dll", "$managed\System.Core.dll",
    "$managed\UnityEngine.dll", "$managed\Assembly-CSharp.dll", "$managed\Assembly-CSharp-firstpass.dll",
    "$core\BepInEx.dll", "$core\0Harmony.dll"
)
$cscArgs = @('/nologo','/noconfig','/nostdlib','/utf8output','/target:library','/codepage:65001',"/out:$out")
foreach ($r in $refs) { $cscArgs += "/reference:$r" }
$cscArgs += $files
& $CSC @cscArgs
if ($LASTEXITCODE -ne 0) { Write-Host "!! 编译失败 EXITCODE=$LASTEXITCODE"; exit 1 }
$size = (Get-Item $out).Length
Write-Host "[OK] 编译成功 $size 字节"

# ---- 静态自检：这两个检查能挡住最隐蔽的两类静默失败 ----
Write-Host "== 自检 =="
$tmp = Join-Path $env:TEMP ("chk_" + [guid]::NewGuid().ToString('N'))
& $ILDSM /text /nobar "/out:$tmp.il" $out | Out-Null
$counts = @{}
foreach ($k in 'BepInPlugin','LoadCardLayout','CalcSize') {
    $counts[$k] = (Select-String -Path "$tmp.il" -Pattern $k | Measure-Object).Count
}
$bad = Select-String -Path "$tmp.il" -Pattern '^\s*\.(method|field|class)|^\s*\.custom' |
       Where-Object { $_.Line -match 'Assembly-CSharp' }
Remove-Item "$tmp.il","$tmp.res" -Force -ErrorAction SilentlyContinue

if ($counts['BepInPlugin'] -lt 1) {
    Write-Host "!! 致命：[BepInPlugin] 特性不存在 —— BepInEx 会报 0 plugins to load 且无任何报错。检查源文件里那一行还在不在！"
    exit 1
}
Write-Host "   BepInPlugin 特性: $($counts['BepInPlugin']) 处 ✓"
if ($bad) {
    Write-Host "!! 致命：方法签名/字段/特性里出现了游戏类型（共 $($bad.Count) 处）—— 这会让 BepInEx 丢弃整个 dll:"
    $bad | ForEach-Object { Write-Host ("      " + $_.Line.Trim()) }
    exit 1
}
Write-Host "   元数据里的游戏类型: 0 处 ✓"

# ---- 安装 ----
function Install-To($dir) {
    $plug = Join-Path $dir 'BepInEx\plugins'
    if (-not (Test-Path $plug)) { Write-Host "   跳过（没有 BepInEx 安装）: $dir"; return $false }
    try {
        $target = Join-Path $plug 'IceyCheatPanel.dll'
        # 非 -SideBuild 时 csc 已经直接写在目标位置了,再拷一次会"自己覆盖自己"而报占用,跳过
        if (-not [string]::Equals($target, $out, [StringComparison]::OrdinalIgnoreCase)) {
            Copy-Item $out $target -Force -ErrorAction Stop
        }
        Get-ChildItem (Join-Path $dir 'BepInEx\cache') -Force -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
        Write-Host "   已安装 + 清缓存: $dir"
        return $true
    } catch {
        Write-Host "   !! 安装失败（dll 被占用？游戏在跑？）: $dir"
        return $false
    }
}

if ($SideBuild) {
    Write-Host "== -SideBuild：只编到旁边，不覆盖 =="
    Write-Host "   要生效需在游戏退出后把 $new 复制到 $dst，并清空 BepInEx\cache"
} else {
    Write-Host "== 安装 =="
    Install-To $ROOT | Out-Null
    if ($InstallOriginal) { Install-To $ORIGINAL | Out-Null }
}
Write-Host "完成。"
