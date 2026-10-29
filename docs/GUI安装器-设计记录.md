# GUI 安装器 · 设计记录(v0.1 观感版)

> 日期 2026-10-07 · 状态:**第一版观感预览,安装逻辑是模拟的**
> 源码 `_moddev\_src\InstallerGui\`(Theme.cs / SvgPath.cs / Gui.cs / app.manifest)
> 构建 `powershell -ExecutionPolicy Bypass -File _tools\build-gui.ps1 -Run`

---

## 1. 框架为什么定在 **C# WinForms + GDI+**(实测结论)

**决定的硬约束:分享出去对方不能装任何东西。**

| 方案 | 结论 | 理由 |
|---|---|---|
| **C# WinForms + GDI+ + csc** | ✅ **选它** | 实测 `csc /target:winexe` 编出 PE Subsystem=2 的 GUI exe;系统自带 .NET Framework 4 即可跑;单文件;**零新工具链** |
| Flutter(Windows 桌面) | ❌ **本机做不了** | `flutter doctor`:`[X] Visual Studio not installed`。Flutter 编 Windows 必须装 VS 的"使用 C++ 的桌面开发"工作负载。SDK 本身有(`D:\project\flutter`,3.47.4 stable) |
| Flutter Web | ❌ | 网页**装不了文件**,当不了安装器 |
| .NET 10 WPF 自包含单文件 | ❌ | 对方要装 ~55MB 运行时;自包含 **150MB+**;单文件发布对 net48 不支持 |
| net48 + **WebView2** + HTML/CSS | ⚠️ **备选(已验证可行)** | `dotnet restore`(net48 + `Microsoft.Web.WebView2`)实测成功,36 秒。能拿到 100% 玻璃拟态(`backdrop-filter`)。代价:引入 dotnet build + NuGet,且**依赖 WebView2 运行时**(本机有 154;Win11 自带,Win10 不保证) |
| Python + tkinter/PySide + PyInstaller | ❌ | 本机没 pyinstaller;PySide6 100MB+;PyInstaller 单文件比 C# exe 更易被杀软误报 |
| PowerShell + WinForms 脚本 | ❌ | 没法干净地出单文件 exe(PS2EXE 又是额外工具)+ 执行策略问题 |

**本机工具链实测**
```
csc.exe      C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe   ← 唯一可用编译器
.NET FW      4.8.09221 (Release 533509)
dotnet SDK   10.0.400 (C:\Program Files\dotnet)
Flutter      3.47.4 stable @ D:\project\flutter  ← 缺 VS,不能编 Windows
WebView2 RT  154.0.4258.53
显示缩放     125%(所以窗口 880x600 出来是 1100x750)
```

---

## 2. 玻璃拟态能到哪一层(主人问的)

| 层 | WinForms | 说明 |
|---|---|---|
| ① 窗口级毛玻璃(窗口背后真模糊) | ✅ **能** | Win11 `DwmSetWindowAttribute`(Mica/亚克力);Win10 退 `SetWindowCompositionAttribute`。本机两个 API 实测都在 |
| ② 卡片级玻璃(卡片半透明叠在模糊底上) | ⚠️ **能近似到 85~90%** | 半透明填充 + 1px 高光描边 + 圆角 + 柔和投影 |
| ③ 内层互相模糊(`backdrop-filter: blur()`) | ❌ **做不到(WPF 也做不到)** | 需要采样背景。WPF 的 `BlurEffect` 只模糊自己子树。**只有 WebView2/浏览器能给** |

**v0.1 现在用的是 ②**(卡片是半透明白 + 描边),没上 ① 的 DWM 模糊——因为本页设计里真正需要模糊的只有顶栏,而顶栏下面没有内容滚动,模糊看不出差别。要加的话是十几行 P/Invoke。

---

## 3. 视觉逐项来源(全部对应 mustayot.love 的源码)

来源:`C:\Users\Administrator\.dsh\browser-sessions\home-mustayot-love-源码-20261004.html`

| 元素 | 源码 | 我们的实现 |
|---|---|---|
| 背景 | `body::before{background:linear-gradient(178deg,#e9f5ff 0%,#f1ecff 46%,#fff0f8 100%)}` | `LinearGradientBrush` + `ColorBlend` 三停(0 / 0.46 / 1) |
| 海浪后排 | `fill:rgba(232,161,255,.34)`,整高,`sea 18s linear infinite` | `#E8A1FF` α0.34,18s 正向 |
| 海浪前排 | `fill:rgba(168,200,255,.5)`,`height:62%`,`26s reverse` | `#A8C8FF` α0.5,62% 高,26s 反向 |
| 循环方式 | `.sea__track{width:200%}` 放 2 个 50% 的 svg,平移到 `-50%` | 同一套:画 2 个副本,平移一个窗宽 |
| 文字三档 | `--ink:#2c2740 --sub:#6f6a86 --faint:#a49fb5` | Theme.Ik/Sub/Faint |
| 强调色 | `--accent:#8E44D9 --hot:#FF6B9D --ok:#1c8a5a` | Theme.Accent/Hot/Ok |
| 卡片描边 | `--line:rgba(44,39,64,.12)` | Theme.Line |
| 字体 | `--sans` 首选 Segoe UI,中文回落微软雅黑;`--mono` Cascadia Code | `Microsoft YaHei UI` + `Cascadia Code`(运行时探测) |
| 鲸鱼 | `#i-whale` | ❌ **主人说不需要,已全部移除** |
| 顶栏 | — | ❌ **不要图标、不要「ICEY 作弊面板」**,只留一行 `INSTALLER v0.1`(主人要求) |
| 大字标题 | — | `ICEY V4`(主人指定;原先是「把 MC 搬进艾希」) |
| 日志区 | — | **只放关键信息,不写自我介绍/宣传语**(主人要求)。启动时跑一次真实状态探测:读 `<游戏目录>\BepInEx\plugins\IceyCheatPanel.dll` 是否存在,报出大小与修改时间;没有则报「面板未安装」 |

### ★ 海浪 path 做过一处修正(不是抄错)

源码的 `d` **不是周期曲线**:后排 `M0 70 …` 起、终在 `(720,45)`;前排 `M0 100 …` 起、终在 `(720,75)`。
首尾 y 差 25 单位。而 CSS 靠"200% 宽轨道 + 平移 -50%"循环 → **接缝处会露出一道约 25 单位(125% 缩放下 ≈39px)的竖边**。
另外源码里第二组波峰只有 135 宽、末尾 45 宽,四组本身不等宽。

**修正**:把四组波峰齐成等宽(每 180 一个),首尾回到同一 y。
**波幅、相位、配色、18s/26s 滚动速度全部不变。**
原始 `d` 留在 `Gui.cs` 的注释里,想还原逐字版本直接换回来。

---

## 4. 踩过的坑(都会再踩,记下来)

| # | 坑 | 症状 → 原因 → 修法 |
|---|---|---|
| 1 | **PowerShell 5.1 文本往返毁中文源码** | 用 `Get-Content -Raw` + `Set-Content -Encoding UTF8` 改 `Gui.cs` → 中文全乱 → csc 报一堆 **CS1010「常量中有换行符」**。原因:PS 5.1 按 ANSI 读 UTF-8 **再写回 UTF-8**,双重编码。→ **改源码一律用编辑器工具,不走 PS 文本管道** |
| 2 | **截图工具没声明 DPI 感知** | `GetWindowRect` 返回虚拟化坐标、`CopyFromScreen` 抓物理像素 → 抓出来整张歪掉(第一次的图就是歪的)。→ 进程启动先 `SetProcessDPIAware()`。**同类坑在调研笔记里也出现过**(um win shot 物理像素 vs drive 虚拟坐标) |
| 3 | **形状没乘 `_dp`、文字乘了** | 卡片/按钮的底和上面的字整体错位半行。→ 加 `S(float)` / `S(RectangleF)` 两个换算函数,**凡是传给 GDI+ 的坐标一律先过 `S()`**;鼠标来的坐标反过来除 `_dp` |
| 4 | **编译时 exe 被占用** | `error CS0016: 未能写入输出文件 … 另一个程序正在使用此文件`。→ **先 Stop-Process 再编译**(build-gui.ps1 里已内置) |
| 5 | **没有 app.manifest 时系统版本会撒谎** | 不加 manifest,`Environment.OSVersion` 把 **Win11 25H2 谎报成 Win8 (6.2.9200)**,导致 DPI 走错分支。→ `app.manifest` 声明 supportedOS + `PerMonitorV2` + `asInvoker`。**这是必需品不是可选项** |
| 6 | **`CreateRoundRectRgn` 在 gdi32 不在 user32** | 第一次探针查 user32 报"不存在",虚惊一场。→ 圆角 GDI32;Win11 优先用 `DWMWA_WINDOW_CORNER_PREFERENCE`(抗锯齿更好) |
| 7 | **PS 脚本必须存成 UTF-8 **带 BOM**** | `build-gui.ps1` 里有中文,存成无 BOM 的 UTF-8 → `powershell -File` 按 ANSI 读 → 报 `The string is missing the terminator`(报错行号还会指向一个纯 ASCII 行,极难查)。→ **已实测:`_tools\build.ps1` 头部就是 `EF BB BF`**,这正是它一直好使的原因。**本项目所有 .ps1 一律带 BOM** |

> 坑 #1 和 #7 是同一件事的两面:**这台机器上,PS 5.1 处理 UTF-8 文本一定要显式声明编码**。
> 读源码用编辑器工具、写 .ps1 带 BOM、解析命令输出用 `PYTHONUTF8=1`(调研笔记里那条)。

---

## 5. v0.1 里哪些是真的、哪些还是假的

| 功能 | 状态 |
|---|---|
| 窗口外壳:无边框 / 可拖动 / 系统圆角 / 关闭(X、Esc) | ✅ 真 |
| 背景渐变 + 两层海浪动画(60fps,双缓冲) | ✅ 真 |
| 游戏目录自动检测(3 个常见路径)+ 浏览对话框 | ✅ 真 |
| **安装 / 修复重装的真实文件写入**(41 个内嵌载荷) | ✅ **真**(后台线程 + BeginInvoke 回 UI) |
| **真实卸载**(带警告框 + 配置先备份到 `_ICEY_panel_config_backup`) | ✅ 真 |
| 进度条真实进度(按文件数) | ✅ 真 |
| 启动时的安装状态探测(读 dll 大小与时间) | ✅ 真 |
| 程序集元数据(`ProductName`/`FileDescription`/`Company`/版本) | ✅ 已填(杀软友好化,见 §7) |
| 命令行模式 `install <目录>` / `uninstall <目录>`(自动化与自测) | ✅ 真 |
| 「关于 / 第三方许可」页(BepInEx LGPL-2.1) | ❌ 还没做 |
| 素材剥离(去掉包里的游戏贴图)→ 包体 14MB→1MB | ❌ 还没做 |

### 端到端自测结果(2026-10-07)

在**空目录**里跑(不碰任何游戏目录):
```
ICEY V4 安装器.exe install <scratch>     → 退出码 0,41/41 个文件
抽样 SHA-256 比对:winhttp.dll / IceyCheatPanel.dll / _blocky\skin.png  全部一致 ✔
ICEY V4 安装器.exe uninstall <scratch>   → 退出码 0,只剩 ICEY.exe 与日志
```

### 顺带修掉打包脚本的一个静默缺口

`pack-installer.ps1` 只拷贝 `-Presets` 指定的预设目录,而 `_blocky`(方块人的皮肤 + 5 把剑贴图)
是**手工**放进载荷的 → **每次重打包都会静默丢掉它**,装到干净机器上方块人就没贴图,而且不报错。
已修:脚本现在固定拷 `_blocky`,并打印张数。载荷因此从 35 → **41 个文件**。
**教训:载荷清单一定要在打包时打印出来核对**,别信"上次是好的"。

### 命令行模式为什么值得留

装解逻辑必须能**在没有鼠标点击的情况下被验证**,而且要能装到空目录里跑,
不必拿真实游戏目录做试验(本项目铁律:不许改原版游戏文件)。
`/target:winexe` 没有控制台,所以结果同时写一份 `install-log.txt`,并用退出码表态。

## 6. 下一步(按依赖顺序)

1. `Installer.cs` 改 `partial class Program`:保留 `install <路径>` / `uninstall <路径>` 命令行,无参数时弹这个 GUI
2. 把 `_work\InstallerPayload.g.cs`(41 个文件的载荷表)接进来 → 换成真装
3. 加「关于 / 第三方许可」页 + 填程序集信息
4. **素材剥离**:包里 11 张游戏贴图上色版 + 6 张 Mojang 素材全部去掉,改成插件运行时从用户自己的安装生成(`atlas_tool.py` 的 `palette`/`apply` 逻辑搬到运行时)
5. 重新打包,核对包体与文件清单

---

## 7. 杀毒软件:做了什么、以及**做不到什么**

> 主人要求「保证这个软件不会轻易被杀毒软件杀死」。**必须分开说**:
> **能做的都做了,但"保证"两个字给不了** —— 原因见下面第 2、3 条。

### 已经做的(可验证)

| 措施 | 为什么有用 |
|---|---|
| 填满程序集元数据(`ProductName`/`FileDescription`/`Company`/版本/版权) | 未签名 **且** 属性页一片空白,是启发式的重点扣分项 |
| `app.manifest` 用 **`asInvoker`**,绝不 `requireAdministrator` | 一上来就要管理员权限是重量级可疑信号 |
| 纯 .NET 程序集,**不加壳 / 不压缩 / 不混淆** | UPX、自定义 packer = 秒杀级启发式 |
| **不写注册表、不设自启、不驻留、不自修改** | 持久化行为是最强的恶意特征 |
| **零网络**(没有 socket、没有 HTTP) | 装机后回连的安装器被重点盯 |
| 不从 `%TEMP%` 释放并执行;只写用户明确选定的游戏目录 | 避开"释放到临时目录再跑"的经典木马模式 |
| 同时发一份**便携版 zip**(散文件,不含 exe) | **最有效的路线**:解压复制,全程没有任何程序被执行 |

**实测证据(2026-10-07,本机)**:
```
最终产物   C:\Users\Administrator\Desktop\ICEY V4 安装器.exe
SHA-256    C378AA06C60ACF66089BAA62BEF815AEB1CD6579E9B029F1B5526FF46308BB4C
签名       NotSigned
Defender   Start-MpScan CustomScan → 扫描完成,**无威胁记录(未报毒)**
```
> Defender 的排除项里**没有**桌面、也没有 ICEY 目录,所以这次是真扫,不是被排除放过的。

### 做不到的(必须让主人知道)

1. **没有数字签名** —— 这是最有效的一项,而它需要**代码签名证书**:
   - **EV 证书**(约 $300–400/年):最有效,立刻拿到 SmartScreen 信誉;
   - **OV 证书**(约 $200/年):要累积下载量才有 SmartScreen 信誉;
   - **自签名证书没用** —— 对 SmartScreen 和杀软信誉毫无帮助,甚至更可疑。
2. **`winhttp.dll` 是最大的风险点,而且躲不掉。** BepInEx 的 Doorstop 注入就是靠
   **DLL 搜索顺序劫持**(把一个 `winhttp.dll` 放在游戏 exe 旁边)。这**正好是恶意软件最常用的手法**,
   所以**任何** BepInEx 安装器都会被激进引擎盯上。这是技术路线的固有代价,不是我们写错了。
3. **第三方杀软比 Defender 激进得多。** 360、火绒、卡巴、ESET 对「未签名 + 释放 DLL 代理」这个组合
   特别敏感。**Defender 过了 ≠ 360 过了。**
4. 本次只做了**静态扫描**。真正运行时释放 41 个文件的那一刻,行为监控仍可能介入。
   真机实测只有一件事能证明:**在一台开着 360/火绒的干净机器上装一次**。

### 如果真要认真发布,建议按这个顺序

1. **先推便携版 zip**,exe 作为备选 —— 成本最低、效果最好;
2. 发布页附上 **SHA-256**,让下载者自己核对;
3. 把 exe 提交给**各家的误报申诉入口**(微软、360、火绒……)各提交一遍,免费;
4. 有预算就买 **EV 代码签名证书**;
5. **绝对不要**把"关杀软 / 加白名单"写成默认安装步骤 —— 那才是真的会让人觉得是木马;
6. **绝对不要**做"自动往 Defender 加排除项"的功能 —— 会一次性毁掉全部可信度。

---

## 8. 图标 与 体积构成

### 图标:直接用艾希自己的

`_tools\IconExtract.cs` —— 从 `ICEY.exe` 的资源段读 `RT_GROUP_ICON` + `RT_ICON`,重组成标准 .ico。
**为什么不用 `Icon.ExtractAssociatedIcon`**:那个只给 32x32,大图标位图就糊了;这个把原始
**9 个尺寸**(16/24/32/48/64/96/128/192/256,全 32bpp)**一个不落**地拿出来。
用 `LoadLibraryEx(..., LOAD_LIBRARY_AS_DATAFILE)` 加载,**不执行**目标模块。
产物缓存到 `_work\icey.ico`,打包时 `/win32icon:` 塞进 exe。

### 体积:14.65 MB 都是什么(实测)

| 类别 | 文件数 | MB | 占比 |
|---|---|---|---|
| **配色预设 sakura(11 张 PNG)** | 11 | **12.20** | **86.6%** |
| BepInEx 核心 | 18 | 1.69 | 12.0% |
| 插件本体 `IceyCheatPanel.dll` | 1 | 0.16 | 1.1% |
| winhttp.dll(Doorstop) | 1 | 0.02 | 0.2% |
| 其它(配置/说明/doorstop) | 4 | 0.01 | 0.1% |
| 方块人素材 `_blocky` | 6 | <0.01 | ~0% |
| 图标(9 尺寸) | — | 0.55 | 3.8% |
| **合计** | 41 | **14.08**(+图标 0.55) | |

**一句话:大是因为它是个"自包含"安装器,而 12.2 MB 是那套浅粉配色预设的 11 张图集 PNG。**

**注意:PNG 已经是压缩格式,再套一层 zip 基本压不动** —— 便携版 zip 12.79 MB 就是证据。
所以"压一压就小了"这条路不存在,只能**少带东西**。

### 想变小,三个选项

| 方案 | 能到多小 | 代价 |
|---|---|---|
| **A. 素材剥离(主人已选定的方向)** —— 插件改成**运行时从用户自己的安装**生成上色贴图 | **≈1.9 MB** | 要改插件 + 进游戏实测;`atlas_tool.py` 的 `palette`/`apply` 逻辑搬到运行时 |
| **B. 预设不进 exe,改成"按需下载/单独发"** | ≈1.9 MB | 玩家要多下一个小包 |
| **C. 图标只留 32/48/256** | 省 ≈0.35 MB | 高 DPI 下某些视图略糊 |
| (什么都不做) | 14.65 MB | 自包含,双击即装 |

---

## 9. ★ 一个会反复咬人的坑:edit 工具会**剥掉 .ps1 的 BOM**

`build.ps1` / `pack-installer.ps1` 之所以一直好用,是因为**文件头部有 `EF BB BF`**。
但用编辑器工具改过 .ps1 之后,**BOM 会被去掉** → 下次 `powershell -File` 按 ANSI 读中文 →
报 `The string is missing the terminator: "`,而且**报错行号指向一个纯 ASCII 行**,极难定位。

**规矩:每次用编辑工具改过 `_tools\*.ps1` 之后,补一次 BOM。** 一行命令:
```powershell
foreach ($p in (Get-ChildItem _tools -Filter *.ps1 -File)) {
  $b=[System.IO.File]::ReadAllBytes($p.FullName)
  if (-not ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)) {
    $t=[System.IO.File]::ReadAllText($p.FullName,[System.Text.Encoding]::UTF8)
    [System.IO.File]::WriteAllText($p.FullName,$t,(New-Object System.Text.UTF8Encoding($true)))
  }
}
```
(和 §4 的坑 #1 / #7 是同一族问题:**这台机器上 PS 5.1 处理 UTF-8 一定要显式声明编码**。)
