<#
  make-skin-presets.ps1 —— 生成「运行时配色」要用的 PNG 预设目录

  做两件事:
    1) 调 atlas_tool.py 的 export + palette,把 11 张主角贴图按预设调色 → _work\player_atlas_<预设>\
    2) 把 11 张 PNG 拷进 BepInEx\config\player_skin\<预设>\(插件运行时读这里)
       同时(如果 _dist\ICEYCheatPanel 存在)拷进载荷的同一位置,方便打包

  用法:
    powershell -ExecutionPolicy Bypass -File make-skin-presets.ps1                          # 默认 sakura(浅粉)
    powershell -ExecutionPolicy Bypass -File make-skin-presets.ps1 -Presets sakura,blue,gold
    powershell -ExecutionPolicy Bypass -File make-skin-presets.ps1 -ForceExport            # 强制重新导出原色 PNG
    powershell -ExecutionPolicy Bypass -File make-skin-presets.ps1 -ToCopyOnly             # 只灌进副本,不碰载荷

  注意:插件按 **贴图名** 匹配(NewPlayerNormal.png 这种),文件名必须就是这 11 个之一。
#>
param(
    # 逗号分隔的预设名。用 [string] 不用 [string[]]:用 `powershell -File 脚本.ps1 -Presets a,b`
    # 调用时数组会被当成**一个**字符串传进来,结果 atlas_tool.py --preset 收到 "a,b" 直接拒绝。踩过了。
    [string]$Presets = 'sakura',
    [switch]$ForceExport,
    [switch]$ToCopyOnly
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ROOT  = Split-Path $PSScriptRoot -Parent          # ...\_moddev
$PY    = 'C:\Users\Administrator\miniconda3\python.exe'
$TOOL  = Join-Path $PSScriptRoot 'atlas_tool.py'
$WORK  = Join-Path $ROOT '_work'
$RAW   = Join-Path $WORK 'player_atlas'            # 原色导出目录
$TARGETS = @(
    'NewPlayerNormal','NewPlayerAtk','NewPlayerAtkHV','NewPlayerSpAtk','NewPlayerUnderAtk',
    'NewPlayerAtkUpRising','NewPlayerAtk_Illum','NewPlayerNormal_Illum','NewPlayerSPAtk_Illum',
    'NewPlayerAtkUpRising_Illum','PlayerFlash'
)

if (-not (Test-Path $PY))   { throw "找不到 Python: $PY" }
if (-not (Test-Path $TOOL)) { throw "找不到 atlas_tool.py: $TOOL" }

# ---- 1. 原色 PNG ----
if ($ForceExport -or -not (Test-Path (Join-Path $RAW 'NewPlayerNormal.png'))) {
    Write-Host "== 导出原色 PNG =="
    & $PY $TOOL export
    if ($LASTEXITCODE -ne 0) { throw "atlas_tool.py export 失败" }
} else {
    Write-Host "== 原色 PNG 已存在,跳过导出(要重导加 -ForceExport)=="
}

function Copy-Into($srcDir, $dstDir) {
    if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
    $miss = @()
    foreach ($n in $TARGETS) {
        $s = Join-Path $srcDir ($n + '.png')
        if (Test-Path $s) { Copy-Item $s $dstDir -Force } else { $miss += $n }
    }
    $got = (Get-ChildItem $dstDir -Filter *.png -File | Measure-Object).Count
    $mb  = [math]::Round(((Get-ChildItem $dstDir -Filter *.png -File | Measure-Object Length -Sum).Sum / 1MB), 1)
    Write-Host ("   {0} -> {1} 张 / {2} MB" -f $dstDir, $got, $mb)
    if ($miss.Count -gt 0) { Write-Host ("   !! 缺: " + ($miss -join ', ')) }
}

$PresetList = @($Presets -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
foreach ($ps in $PresetList) {
    $out = Join-Path $WORK ("player_atlas_" + $ps)
    Write-Host "== 预设 $ps =="
    & $PY $TOOL palette --preset $ps
    if ($LASTEXITCODE -ne 0) { throw "atlas_tool.py palette ($ps) 失败" }
    if (-not (Test-Path $out)) { throw "没产出目录: $out" }

    Copy-Into $out (Join-Path $ROOT ("BepInEx\config\player_skin\" + $ps))
    if (-not $ToCopyOnly) {
        $payload = Join-Path $ROOT '_dist\ICEYCheatPanel'
        if (Test-Path $payload) { Copy-Into $out (Join-Path $payload ("BepInEx\config\player_skin\" + $ps)) }
    }
}

Write-Host ""
Write-Host "好了。插件里 F1 -> 「配色」卡片:开局「配色替换(开/关)」,再用「配色预设」选。"
Write-Host "换了 PNG 要让它立刻生效:点「重新读取 PNG」。"
