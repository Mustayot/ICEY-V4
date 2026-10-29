# ICEY V4 安装器(GUI)· 快速编译(UI 调样式用)
#
# 只重编 GUI,**不重新组装载荷** —— 载荷没变时改界面用这个,快。
# 载荷或插件 dll 变了,请用 pack-gui.ps1(它会重跑 pack-installer.ps1 的全部校验)。
#
#   powershell -ExecutionPolicy Bypass -File _tools\build-gui.ps1
#   powershell -ExecutionPolicy Bypass -File _tools\build-gui.ps1 -Run
#
# ★ 本文件必须存成 UTF-8 带 BOM(PS 5.1 会把无 BOM 的 UTF-8 当 ANSI 读)。

param([switch]$Run)

$ErrorActionPreference = 'Stop'
$PACK = Join-Path $PSScriptRoot 'pack-gui.ps1'
$PAYLOAD = Join-Path (Split-Path $PSScriptRoot -Parent) '_dist\ICEYCheatPanel'

# 载荷不在就老老实实全量跑一次
if (Test-Path -LiteralPath $PAYLOAD) { & $PACK -SkipPayload -NoDesktop } else { & $PACK -NoDesktop }
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Run) {
    $exe = Join-Path (Split-Path $PSScriptRoot -Parent) '_dist\ICEY V4.exe'
    Start-Process -FilePath $exe
    Write-Host "     已启动。" -ForegroundColor Green
}
