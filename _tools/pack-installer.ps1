<#
  pack-installer.ps1 —— 重新打包「便携包 + 单文件安装器 exe」

  产出:
    _dist\ICEYCheatPanel\            载荷(winhttp.dll / doorstop_config.ini / .doorstop_version / BepInEx\)
    _dist\ICEYCheatPanel-portable.zip
    _dist\ICEY作弊面板-安装器.exe     载荷内嵌的单文件安装/卸载器

  关键点:
    * 发出的 BepInEx.cfg 必须已把 [Preloader.Entrypoint] 的 Type 改成 MonoBehaviour,
      否则 Unity 5.4 会黑屏（脚本会校验，不通过就中止）
    * 安装器 exe 用 csc /target:exe 编译,载荷用 /resource:文件,资源名 f0..fN 由本脚本
      **生成**成 _work\InstallerPayload.g.cs(不手写,手写迟早错位)
    * 安装器 **不要** 加 /noconfig /nostdlib（它要用完整 .NET 4 框架）
#>
param(
    [string]$Payload = "$PSScriptRoot\..\_dist\ICEYCheatPanel",
    # 打包哪几套运行时配色预设:一套 11 张 PNG 约 14MB,打进 exe 会让它明显变大,
    # 所以默认只带 sakura(浅粉)。别的预设用 make-skin-presets.ps1 现场生成即可。
    [string]$Presets = 'sakura'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# 载荷路径必须绝对化:下面用 FullName.Substring($Payload.Length) 算内嵌的相对路径
if (-not [System.IO.Path]::IsPathRooted($Payload)) { $Payload = Join-Path $PSScriptRoot $Payload }
$Payload = [System.IO.Path]::GetFullPath($Payload)

$ROOT     = Split-Path $PSScriptRoot -Parent
$DIST     = Join-Path $ROOT '_dist'
$CSC      = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$GEN      = Join-Path $ROOT '_src\Installer\Installer.cs'
$ZIP      = Join-Path $DIST 'ICEYCheatPanel-portable.zip'
$EXE      = Join-Path $DIST 'ICEY作弊面板-安装器.exe'

# ---- 1. 找 BepInEx 源（优先副本里装好的，回退 _tools 原包）----
$srcLoader = $ROOT
if (-not (Test-Path (Join-Path $ROOT 'BepInEx\core\BepInEx.dll'))) {
    $srcLoader = Join-Path $ROOT '_tools\BepInEx_5.4.23.5_x64'
}
$srcCore = Join-Path $srcLoader 'BepInEx\core'
if (-not (Test-Path (Join-Path $srcCore 'BepInEx.dll'))) { throw "找不到 BepInEx core：$srcCore" }
Write-Host "BepInEx 源: $srcLoader"

# ---- 2. 组装载荷 ----
# 插件本体来源(必须在删载荷之前确定,否则副本没装 BepInEx 时会找不到 dll):
#   副本已安装 > 旁边编译产物(build.ps1 -SideBuild)> 现有载荷
$plugCandidates = @(
    (Join-Path $ROOT 'BepInEx\plugins\IceyCheatPanel.dll'),
    (Join-Path $ROOT '_work\IceyCheatPanel.new.dll'),
    (Join-Path $Payload 'BepInEx\plugins\IceyCheatPanel.dll')
)
$plugSrc = $plugCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $plugSrc) { throw "找不到 IceyCheatPanel.dll(试过副本 plugins / _work / 现有载荷)。先跑 build.ps1 -SideBuild" }
Write-Host "插件本体: $plugSrc"

if (Test-Path $Payload) { Remove-Item $Payload -Recurse -Force }
foreach ($d in 'BepInEx\core','BepInEx\config','BepInEx\plugins') {
    New-Item -ItemType Directory -Path (Join-Path $Payload $d) -Force | Out-Null
}
foreach ($f in 'winhttp.dll','doorstop_config.ini','.doorstop_version') {
    $p = Join-Path $srcLoader $f
    if (Test-Path $p) { Copy-Item $p $Payload -Force } else { Write-Host "   !! 缺 $f（$srcLoader）" }
}
Copy-Item (Join-Path $srcCore '*') (Join-Path $Payload 'BepInEx\core') -Force
Copy-Item (Join-Path $srcLoader 'BepInEx\config\BepInEx.cfg') (Join-Path $Payload 'BepInEx\config') -Force -ErrorAction SilentlyContinue
Copy-Item $plugSrc (Join-Path $Payload 'BepInEx\plugins\IceyCheatPanel.dll') -Force
# 使用说明.txt 以前是手放进载荷的,这个脚本会把载荷整个删掉重建 → 忘了拷就等于"资源缺失",
# 装的时候只打一行 [!] 缺少资源 fN,装不全还看不出来。这里固定从 _moddev 拷。
if (Test-Path (Join-Path $ROOT '使用说明.txt')) {
    Copy-Item (Join-Path $ROOT '使用说明.txt') $Payload -Force
} else {
    Write-Host "   !! 缺 使用说明.txt($ROOT)—— 载荷里不会有它"
}

# 运行时配色预设:从副本的 BepInEx\config\player_skin\<预设>\ 拷进载荷(插件读的就是 config 下这一份)
$skinSrc = Join-Path $ROOT 'BepInEx\config\player_skin'
foreach ($ps in @($Presets -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })) {
    $s = Join-Path $skinSrc $ps
    if (-not (Test-Path $s)) { Write-Host "   !! 配色预设 $ps 不存在($s)—— 跳过,先跑 make-skin-presets.ps1"; continue }
    $d = Join-Path $Payload ("BepInEx\config\player_skin\" + $ps)
    New-Item -ItemType Directory -Path $d -Force | Out-Null
    Copy-Item (Join-Path $s '*.png') $d -Force
    $n = (Get-ChildItem $d -File | Measure-Object).Count
    Write-Host "   配色预设 $ps : $n 张 PNG"
}

# 方块人素材:BepInEx\config\player_skin\_blocky\(皮肤 + 5 把剑贴图)
# ★ 这段是补的坑:原先脚本只拷 -Presets 指定的预设目录,而 _blocky 是**手工**放进载荷的,
#   所以每次重打包都会静默丢掉它 —— 装到干净机器上方块人就没贴图,而且不报错,极难发现。
$blockySrc = Join-Path $skinSrc '_blocky'
if (Test-Path $blockySrc) {
    $d = Join-Path $Payload 'BepInEx\config\player_skin\_blocky'
    New-Item -ItemType Directory -Path $d -Force | Out-Null
    Copy-Item (Join-Path $blockySrc '*.png') $d -Force
    Write-Host ("   方块人素材 _blocky : {0} 张 PNG" -f (Get-ChildItem $d -File | Measure-Object).Count)
} else {
    Write-Host "   !! 缺方块人素材 $blockySrc —— 载荷里不会有它"
}

# 配置：优先用"已调好入口点"的那份
$cfgKeep = Join-Path $ROOT '_work\uninstall-keep\BepInEx.cfg'
if (Test-Path $cfgKeep) { Copy-Item $cfgKeep (Join-Path $Payload 'BepInEx\config\BepInEx.cfg') -Force }

# ---- 3. 校验入口点（黑屏的根因）----
$cfg = Join-Path $Payload 'BepInEx\config\BepInEx.cfg'
if (-not (Test-Path $cfg)) { throw "载荷里没有 BepInEx.cfg" }
$typeLine = (Select-String -Path $cfg -Pattern '^Type\s*=' | Select-Object -First 1).Line
if ($typeLine -notmatch 'MonoBehaviour') {
    Write-Host "!! 中止：发出的 BepInEx.cfg 入口点不是 MonoBehaviour（当前: $typeLine）—— 这样原版会黑屏"
    exit 1
}
Write-Host "入口点校验通过: $($typeLine.Trim())"

$stat = Get-ChildItem $Payload -Recurse -File | Measure-Object -Property Length -Sum
Write-Host ("载荷: {0} 个文件 / {1:N2} MB" -f $stat.Count, ($stat.Sum / 1MB))

# ---- 4. 打包 zip ----
if (Test-Path $ZIP) { Remove-Item $ZIP -Force }
Compress-Archive -Path (Join-Path $Payload '*') -DestinationPath $ZIP -Force
Write-Host ("zip: {0:N0} KB" -f ((Get-Item $ZIP).Length / 1KB))

# ---- 5. 编译单文件安装器 exe ----
if (-not (Test-Path $GEN)) { throw "缺安装器源码: $GEN" }
$files = Get-ChildItem $Payload -Recurse -File | Sort-Object FullName

# 载荷清单:资源名 f0..fN 与这个表必须一一对应,所以**生成**它,不手写(手写迟早错位)。
$genCs = Join-Path $ROOT '_work\InstallerPayload.g.cs'
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('// 自动生成:pack-installer.ps1。不要手改。')
[void]$sb.AppendLine('static class GeneratedPayload')
[void]$sb.AppendLine('{')
[void]$sb.AppendLine('    public static readonly string[,] Files = new string[,] {')
$i = 0
foreach ($f in $files) {
    $rel = $f.FullName.Substring($Payload.Length).TrimStart('\')
    [void]$sb.AppendLine('        { "f' + $i + '", @"' + $rel + '" },')
    $i++
}
[void]$sb.AppendLine('    };')
[void]$sb.AppendLine('}')
[System.IO.File]::WriteAllText($genCs, $sb.ToString(), (New-Object System.Text.UTF8Encoding($true)))
Write-Host "载荷清单: $genCs(共 $($files.Count) 项)"

$args = @('/nologo','/target:exe','/codepage:65001','/utf8output',"/out:$EXE")
$i = 0
foreach ($f in $files) { $args += "/resource:$($f.FullName),f$i"; $i++ }
$args += $GEN
$args += $genCs
& $CSC @args
if ($LASTEXITCODE -ne 0) { Write-Host "!! 安装器编译失败 EXITCODE=$LASTEXITCODE"; exit 1 }
Write-Host ("exe: {0:N0} KB（内嵌 {1} 个文件）" -f ((Get-Item $EXE).Length / 1KB), $files.Count)
Write-Host "完成。"
