# ICEY V4 · 源码包

> 这是《ICEY》作弊面板 + GUI 安装器 + 全部逆向/调研记录的**源码与文档**。
> **本包里不含任何游戏文件,也不含任何游戏素材。** 详见下面「被排除的东西」。

---

## 这个包怎么用

包里的目录布局和作者的开发目录 `_moddev` 一致。用法:

1. 找到你自己的《ICEY》安装目录(里面有 `ICEY.exe` 和 `ICEY_Data\`)。
2. **复制一份游戏目录副本**(别在原版上开发)。习惯做法是命名成 `_moddev`。
3. 把这个包里的内容放进那份副本里,和 `ICEY.exe` 同级:
   ```
   <你的游戏目录>\
     ICEY.exe
     ICEY_Data\
     _src\        ← 本包
     _tools\      ← 本包
     _work\       ← 本包
     docs\         ← 本包
     README-源码包.md
   ```
4. 按下面「编译」那一节操作。

> ⚠️ 包里**没有** `BepInEx\`。编译需要 BepInEx 的引用程序集,请自己下载
> **BepInEx 5.4.23.5 x64** 解压到 `_tools\BepInEx_5.4.23.5_x64\`,
> 或者先把 BepInEx 装进这份副本(`_tools\build.ps1` 两处都认)。
> (`pack-installer.ps1` 打安装器时也需要它。)

---

## 环境要求

| 项 | 值 |
|---|---|
| 系统 | **Windows**(全部脚本是 PowerShell,**5.1 就够**,不是 pwsh 7) |
| .NET Framework | 4.0 编译器即可;运行时 4.x(Win10/11 自带) |
| 编译器 | `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`(**系统自带,不需要装 VS**) |
| Python | 3.x(`atlas_tool.py` / 探针脚本用;**需要 `UnityPy` 和 `Pillow`**) |
| 游戏 | 《ICEY》原版,自备 |

---

## 编译

```powershell
# 插件(编译 + 两项静态自检 + 装进这份副本 + 清类型缓存)
powershell -ExecutionPolicy Bypass -File _tools\build.ps1
powershell -ExecutionPolicy Bypass -File _tools\build.ps1 -SideBuild    # 游戏正在跑时用:编到 _work,不覆盖

# GUI 安装器(组装载荷 + 校验 + 编 exe + 拷桌面)
powershell -ExecutionPolicy Bypass -File _tools\pack-gui.ps1
powershell -ExecutionPolicy Bypass -File _tools\build-gui.ps1           # 只重编 GUI,快

# 图集工具链(列出/导出/换色/区域/回写/校验/还原)
python _tools\atlas_tool.py list
```

`build.ps1` 里那两项自检**不是可选的**,它们挡的是两类"静默失败"
(插件不加载但日志无报错)。详见 `docs\逆向文档.md` 的坑清单与编译章节。

---

## 目录说明

```
_src\CheatPanel\        插件本体(12 个 partial 文件,C# / BepInEx 5 + HarmonyX)
_src\Installer\         老的控制台安装器
_src\InstallerGui\      ★ GUI 安装器「ICEY V4」(WinForms 全自绘 + GDI+)
_tools\                 构建 / 打包 / 图集工具 / 图标抽取 / 探针
_work\                  中间产物与设计文档(只留了文档和探针脚本)
docs\                   全部调研与技术文档(★ 先看「逆向文档.md」)
```

**★ 接手必读顺序**:
1. `docs\逆向文档.md` —— 游戏文件地图 + 代码地图 + 可 hook 锚点 + 坑清单;
2. 同文件 **§29**(PowerShell 5.1 + UTF-8 的三条铁律 —— **不读一定踩**);
3. `docs\GUI安装器-设计记录.md`(GUI 的选型/视觉/体积/杀软);
4. `docs\官方调试面板-解锁记录.md`(F2 解锁官方调试面板)。

---

## ★ 被排除的东西(以及为什么)

这个项目有一条铁律:**仓库里只放代码,绝不提交任何游戏文件。**
所以下面这些**故意没打进包** —— 如果你要复现,请用**你自己那份游戏**重新生成:

| 没打进包的 | 是什么 | 怎么自己生成 |
|---|---|---|
| `_work\*.il`(44 MB) | 《ICEY》的反汇编(游戏代码) | 自己用 `ildasm` 反汇编 `ICEY_Data\Managed\Assembly-CSharp.dll` |
| `_work\player_atlas*\` | 从游戏 `resources.assets` **导出**的主角图集 PNG | `python _tools\atlas_tool.py export` |
| `_work\mc_assets\`、`mcskin\` | Minecraft 官方皮肤 / 剑贴图(Mojang 素材) | 从你自己那份 MC 的 jar 里取 |
| `BepInEx\config\player_skin\*` | 上色后的游戏贴图(sakura 预设) | `python _tools\atlas_tool.py palette` + `apply` |
| `_work\icey.ico` | 从游戏 exe 抽出来的图标 | `_tools\pack-gui.ps1` 会自动抽(它就是干这个的) |
| `BepInEx\`、`_dist\` | 第三方二进制 / 已构建产物 | 自己下 BepInEx、跑一遍构建脚本 |
| `_work\pt_research\_raw\` | 调研时抓的网页快照(scratch) | 不需要 |

`_work\` 里**留下的**都是文档和探针脚本(纯代码/结论),不含游戏数据。

---

## 规矩(和代码里一致,请一起遵守)

1. **绝不启动或关闭游戏进程**;游戏在跑时只能 `build.ps1 -SideBuild`。
2. **不改原版游戏文件**;一切改动要能一键还原,**还原要有哈希校验**(项目里有 `atlas_backup` 账本)。
3. **等长补丁原则**:游戏资源按"偏移+长度"寻址,只做等长度覆盖。
4. **别在启动路径做重活**(踩过:8.4 MB 日志把主线程卡死)。
5. **一切要能自证**:关键事实打进日志,别靠猜。
6. 只用于**单机离线**;不碰 DRM / 反作弊;不提交游戏文件。

---

## 许可与第三方

- 本仓库原创源代码及项目自制文档采用根目录 `LICENSE` 中的 MIT License。
- MIT License 不授予《ICEY》游戏本体、游戏内美术/音频/文本等内容、商标或其他第三方材料的权利；这些内容仍归各自权利人所有，并受其许可条款约束。
- 本包不捆绑第三方二进制。文档中提及的第三方组件按各自许可证使用：**BepInEx**(LGPL-2.1)、**HarmonyX / MonoMod / Mono.Cecil**(MIT)，以及 Python 工具链中的 **UnityPy**、**Pillow** 等(遵循各自许可证)。
- 《ICEY》版权属 **Shanghai FantaBlade Network Technology Co., Ltd**；Minecraft 属 **Mojang / Microsoft**。本项目是非官方粉丝作品，与任何官方无关。
- 使用本代码前请先备份存档，并自行确认对游戏本体及相关资源的使用符合适用条款。

## 补充说明(2026-10-29 审计后补)

1. **`_work\model_files\` 不入库**:里面是游戏导出的 Spine 骨架(`skel\`)、布局表(`atlas\`)与原始图集 PNG(`texture\`),
   属于**游戏素材**,只保留 `README.txt`(说明文件与可改动范围)。要复现请自己从游戏 `resources.assets` 导出。
2. **`_src\Installer` 与 `_src\InstallerGui` 不能单独编译**:
   它们引用 `GeneratedPayload.Files`,这个类由 `_tools\pack-installer.ps1` / `_tools\pack-gui.ps1`
   **生成**到 `_work\InstallerPayload.g.cs` / `_work\GuiPayload.g.cs`。
   要编安装器,先跑对应打包脚本,别直接 `csc` 这两个目录。
3. 版本以源码 metadata 为准:当前 `[BepInPlugin(...)]` 是 **0.7.2**,构建产物 **167,424 字节**(与本仓库发布包一致)。
