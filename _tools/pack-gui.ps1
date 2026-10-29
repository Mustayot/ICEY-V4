# ICEY V4 安装器(GUI)· 打包脚本
#
# 做的事:
#   1. 调 pack-installer.ps1 —— 组装载荷、校验 BepInEx.cfg 入口点、出便携 zip 和控制台版 exe
#      (这些校验不重复实现:尤其是"入口点必须是 MonoBehaviour,否则原版黑屏"那条)
#   2. 为 GUI 生成载荷表 _work\GuiPayload.g.cs
#   3. 用 csc /target:winexe 编出**载荷内嵌**的单文件 GUI 安装器
#   4. 拷到桌面(交付物)
#
# 用法:
#   powershell -ExecutionPolicy Bypass -File _tools\pack-gui.ps1
#   powershell -ExecutionPolicy Bypass -File _tools\pack-gui.ps1 -SkipPayload   # 载荷没变,只重编 GUI
#
# ★ 本文件必须存成 UTF-8 **带 BOM**:PS 5.1 会把无 BOM 的 UTF-8 当 ANSI 读,
#   中文字符串会炸成 "The string is missing the terminator"。

param(
    [switch]$SkipPayload,
    [switch]$NoDesktop,
    [string]$Presets = 'sakura'
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ROOT = Split-Path $PSScriptRoot -Parent
$SRC  = Join-Path $ROOT '_src\InstallerGui'
$DIST = Join-Path $ROOT '_dist'
$WORK = Join-Path $ROOT '_work'
$CSC  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$PAYLOAD = Join-Path $DIST 'ICEYCheatPanel'
$EXE  = Join-Path $DIST 'ICEY V4.exe'
$GEN  = Join-Path $WORK 'GuiPayload.g.cs'
$ICO  = Join-Path $WORK 'icey.ico'

if (-not (Test-Path -LiteralPath $CSC)) { throw "找不到 csc.exe: $CSC" }

# ---- 0. 图标:直接用艾希自己的 exe 图标 ----
# IconExtract 从 ICEY.exe 的资源段读 RT_GROUP_ICON + RT_ICON,拿到**原始多尺寸**图标
# (16/24/32/48/64/96/128/192/256),不是 ExtractAssociatedIcon 那种只有 32x32 的糊图。
# 用 LOAD_LIBRARY_AS_DATAFILE 加载,不执行目标模块。抽出来缓存到 _work\icey.ico。
$gameExe = Join-Path (Split-Path $ROOT -Parent) 'ICEY.exe'
if (-not (Test-Path -LiteralPath $ICO)) {
    if (-not (Test-Path -LiteralPath $gameExe)) { throw "找不到游戏 exe(取图标用): $gameExe" }
    $ieExe = Join-Path $WORK 'IconExtract.exe'
    & $CSC /nologo /target:exe /codepage:65001 /utf8output "/out:$ieExe" (Join-Path $PSScriptRoot 'IconExtract.cs')
    if ($LASTEXITCODE -ne 0) { throw "IconExtract 编译失败" }
    & $ieExe $gameExe $ICO
    if ($LASTEXITCODE -ne 0) { throw "图标抽取失败" }
}
Write-Host "图标: $ICO"

# ---- 1. 载荷(含所有校验) ----
if (-not $SkipPayload) {
    Write-Host "== 组装载荷 ==" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'pack-installer.ps1') -Presets $Presets
    if ($LASTEXITCODE -ne 0) { throw "pack-installer.ps1 失败 (exit $LASTEXITCODE)" }
}
if (-not (Test-Path -LiteralPath $PAYLOAD)) { throw "载荷目录不存在: $PAYLOAD —— 先跑不带 -SkipPayload 的一次" }

# ---- 2. 载荷表 ----
$files = Get-ChildItem -LiteralPath $PAYLOAD -Recurse -File | Sort-Object FullName
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('// 自动生成:pack-gui.ps1。不要手改。')
[void]$sb.AppendLine('static class GeneratedPayload')
[void]$sb.AppendLine('{')
[void]$sb.AppendLine('    public static readonly string[,] Files = new string[,] {')
$i = 0
foreach ($f in $files) {
    $rel = $f.FullName.Substring($PAYLOAD.Length).TrimStart('\')
    [void]$sb.AppendLine('        { "f' + $i + '", @"' + $rel + '" },')
    $i++
}
[void]$sb.AppendLine('    };')
[void]$sb.AppendLine('}')
[System.IO.File]::WriteAllText($GEN, $sb.ToString(), (New-Object System.Text.UTF8Encoding($true)))
Write-Host "载荷表: $GEN(共 $($files.Count) 项)"

# ---- 3. 编 GUI(载荷内嵌) ----
Get-Process -Name 'ICEY V4','ICEY V4 安装器','ICEY作弊面板-安装器' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

$cscArgs = @(
    '/nologo', '/target:winexe', '/codepage:65001', '/utf8output',
    "/win32manifest:$SRC\app.manifest",
    "/win32icon:$ICO",
    '/reference:System.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    "/out:$EXE"
)
$i = 0
foreach ($f in $files) { $cscArgs += "/resource:$($f.FullName),f$i"; $i++ }
$cscArgs += (Join-Path $SRC 'Theme.cs')
$cscArgs += (Join-Path $SRC 'SvgPath.cs')
$cscArgs += (Join-Path $SRC 'Gui.cs')
$cscArgs += (Join-Path $SRC 'Cli.cs')
$cscArgs += (Join-Path $SRC 'AssemblyInfo.cs')
$cscArgs += $GEN

& $CSC @cscArgs
if ($LASTEXITCODE -ne 0) { throw "GUI 编译失败 (exit $LASTEXITCODE)" }

$info = Get-Item -LiteralPath $EXE
Write-Host ""
Write-Host ("[OK] GUI 安装器: {0}" -f $info.FullName) -ForegroundColor Green
Write-Host ("     {0:N0} KB · 内嵌 {1} 个文件 · GUI 子系统 · 依赖 .NET Framework 4" -f ($info.Length/1KB), $files.Count)
$hash = (Get-FileHash -LiteralPath $EXE -Algorithm SHA256).Hash
Write-Host "     SHA-256: $hash"

# ---- 4. 交付到桌面 ----
if (-not $NoDesktop) {
    $desk = [Environment]::GetFolderPath('Desktop')
    if (Test-Path -LiteralPath $desk) {
        Copy-Item -LiteralPath $EXE -Destination $desk -Force
        # 清掉旧名字,别让桌面上留两个
        Get-ChildItem -LiteralPath $desk -Filter 'ICEY V4 安装器.exe' -ErrorAction SilentlyContinue | Remove-Item -Force
        $zip = Join-Path $DIST 'ICEYCheatPanel-portable.zip'
        if (Test-Path -LiteralPath $zip) { Copy-Item -LiteralPath $zip -Destination (Join-Path $desk 'ICEY V4 便携版.zip') -Force }
        Write-Host ""
        Write-Host "[OK] 已放到桌面:" -ForegroundColor Green
        Get-ChildItem -LiteralPath $desk -Filter 'ICEY V4*' | ForEach-Object { Write-Host ("     " + $_.Name + "  (" + [math]::Round($_.Length/1MB,2) + " MB)") }
    } else { Write-Host "!! 找不到桌面目录,跳过拷贝" }
}
Write-Host "完成。"
