# ICEY 作弊面板 · ICEY Cheat Panel

给《ICEY》(2D 横版动作游戏)做的**游戏内中文作弊与外观面板**,基于 **BepInEx 5** 插件,游戏里按 **`F1`** 开关。
所有功能都在**运行时**生效,**不修改游戏任何资源文件**,关掉即还原。

| | |
|---|---|
| 插件 ID | `icey.cheatpanel` |
| 版本 | **v0.7.2** |
| 运行环境 | Unity 5.4.0f3 / Mono(.NET 2.0 档)/ x64 Windows |
| 框架 | BepInEx 5.4.23.5 + HarmonyX |
| 规模 | **11 张卡片 / 120 项功能**;插件源码 **12 个分部类 / 6,044 行 C#**;另含命令行安装器与 GUI 安装器源码 |
| 发行形态 | 单文件安装器 exe(命令行版 + GUI 版)+ 便携 zip |

> 本仓库**只含源码、文档与自制素材,不含任何游戏文件**。

---

## 功能总览

面板是 11 张可拖动卡片:滚轮滚动、卡片吸附、`Shift` 自由摆放、`F1` 开关、配置自动持久化、右上角状态水印。

| 卡片 | 内容 |
|---|---|
| 战斗 | 无敌、无限能量、一击必杀、无限闪现、无限连击、连击数显示、攻击力倍率、**判定帧(防反窗口:秒/帧双滑块)**、回满血/复活、清场 |
| 玩家 | 移动速度、跳跃高度、角色缩放、重力、穿墙、回到最后安全位置、瞬移到最近敌人 / 鼠标、相机自由视角 |
| 敌人 | 血量/攻击力倍率、敌人不攻击、永久冻结、AI 全关、选择敌人类型生成 |
| 资源与进度 | 金币(+100 / +1000 / +10000 / 设为指定值)、强化全拉满 / 重置 / 选择项 / 归零 / 刷新属性、循环切换难度、一键解锁全部成就、跳关 |
| 世界与画面 | HUD 叠加、DPS、碰撞盒显示、时间缩放 / 倍率 / 暂停 / 冻结 30 帧、视野大小、毛玻璃 + 模糊强度、滤镜切换(游戏自带 216 个)、画面去色、满屏特效 |
| 存档与剧情 | 手动存档、手动读档(读档前自动备份)、进入 / 退出血宫 |
| 配色 | 运行时配色替换(预设 / 自定义 PNG)+ 动态配色(颜色随时间流动) |
| 文本 | 运行时文本替换(自动导出 1262 条术语,改完即时生效) |
| 换角色 | 运行时"借"场景里其他骨架给主角,一键还原 |
| 方块人 | 主角换成 Minecraft 风格方块人(真 6 面盒子 + 程序化动画 + 手持物) |
| 水印界面 | 右上角状态水印(逐字渐变,中/英可切)、界面主题切换、开发者模式 |

另外解锁了**游戏自带的官方调试面板**(游戏内按 **`F2`**);实验性功能收进"开发者模式",正常游玩时看不到。

---

## 技术要点(全部实测)

项目有一条硬约束:游戏资源睡在 Unity 的 `resources.assets` / `.resS` 里,按"偏移 + 长度"寻址。所以做法只有两种 ——
**要么完全不碰文件(内存里改)**,**要么只做等长度覆盖**(同尺寸贴图、4 字节 float),写完哈希校验、可一键回滚。

由此实现的几套"运行时直通"：

- **贴图热替换**:读 `player_skin\<预设>\*.png` 塞进主角材质,按材质对象记账,关闭时逐个写回;
- **骨架数据热替换**:替换 `SkeletonRenderer.skeletonDataAsset`(Spine 2.1.27),带账本与原值还原;
- **调用拦截**:HarmonyX 挂在游戏自己的方法上 —— 文本(`I2.Loc.TermData.GetTranslation` 后缀)、攻击冷却(攻击入口前缀)、防反窗口(`FlashStart` 后缀);
- **顶点色染色**:该游戏的 shader 不读材质 `_Color`,动态配色改走**顶点色** —— 在 `SkeletonRenderer.LateUpdate()` 之后改 `mesh.colors32`,每帧由骨架重建自动复原;
- **自绘网格换建模**:关掉原骨架渲染,用自己的 6 面盒子跟随艾希的骨头与位置,配 MC 皮肤与物品贴图;
- **改判定帧**:防反(闪击反击)的判定窗口由 `PlayerAnimEventListener.FlashStart()` 里的
  `_flashEndCount = WorldTime.SecondToFrame(0.0833f)`(约 5 帧 @60fps)决定,`UpdateFlashEnd()` 每帧减 1,减到 0 关闭 `flashFlag`。
  这个常量被编进 IL 改不了,但 `_flashEndCount` 是**实例字段** —— 所以在 `FlashStart` 后挂 postfix 把它顶大,
  就等于把"被打前一瞬闪避"的窗口拉宽。面板给了联动两个滑块:**秒** 与 **帧**。

---

## 目录结构

```
_src/CheatPanel/          插件源码(12 个分部类,6,044 行)
  CheatPanelPlugin.cs             主体:卡片定义、功能实现、配置读写、防反窗口补丁
  CheatPanelPlugin.Ui.cs          面板绘制(卡片/行/开关/滑块/搜索/Toast/HUD)
  CheatPanelPlugin.Theme.cs       水印与渐变绘制
  CheatPanelPlugin.Theme2.cs      双主题皮肤层、布局滚动、开发者模式过滤
  CheatPanelPlugin.Skin.cs        配色替换(贴图热替换 + 账本)
  CheatPanelPlugin.SkinFx.cs      动态配色(顶点色)
  CheatPanelPlugin.Text.cs        文本替换(I2 Localization 拦截)
  CheatPanelPlugin.Blocky.cs      MC 方块人(网格/UV/程序化动画/手持物)
  CheatPanelPlugin.Borrow.cs      借别人骨架
  CheatPanelPlugin.Character.cs   换角色辅助
  CheatPanelPlugin.McPatches.cs   Harmony 补丁(攻击冷却等)
  CheatPanelPlugin.DebugUI.cs     官方调试面板(F2)解锁
_src/Installer/           命令行安装器(安装/卸载/校验)
_src/InstallerGui/        GUI 安装器(WinForms 全自绘)
_tools/                   编译与打包脚本、图集工具链
_work/                    中间产物(反汇编、诊断记录等)
文档/                      全部文档(见下)
```

## 快速开始(用现成安装器)

1. 下载 `ICEY V4 安装器.exe`(GUI)或命令行版安装器;
2. 选游戏根目录(与 `ICEY.exe` 同级),安装;
3. 启动游戏,按 **`F1`** 打开面板,按 **`F2`** 打开游戏自带的官方调试面板;
4. 卸载:安装器里选卸载,或删掉游戏根目录下的 `winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`、`BepInEx\`。

> **目录布局**:本包结构与开发者开发目录一致 —— 先复制一份游戏目录副本(不要在原版上开发),把 `_src`、`_tools`、`_work`、`文档` 放进去,详见 `README-源码包.md`。

## 从源码编译

编译引用游戏自带的 `ICEY_Data\Managed\*.dll`(保证与 Unity 5.4 的 mscorlib 完全匹配),因此**本机需要一份该游戏**:

```powershell
# 编译 + 自检 + 装到副本
powershell -ExecutionPolicy Bypass -File _tools\build.ps1
# 游戏正在运行时:只编到旁边,不覆盖正在使用的 dll
powershell -ExecutionPolicy Bypass -File _tools\build.ps1 -SideBuild
# 打包安装器
powershell -ExecutionPolicy Bypass -File _tools\pack-installer.ps1 -Presets sakura,_blocky
```

`build.ps1` 带两项**静态自检**,挡的是"插件静默不加载且日志无报错"这类最隐蔽的故障:

1. `[BepInPlugin]` 特性存在(否则 BepInEx 报 `0 plugins to load`,无其他线索);
2. 程序集元数据里**不出现游戏类型** —— 方法签名/字段/特性里一旦混入 `Assembly-CSharp`,BepInEx 会在链路加载期解析失败并**丢弃整个 dll**。

## 文档

| 文档 | 内容 |
|---|---|
| `文档/使用说明.txt` | 面向玩家的完整用法 |
| `文档/交接文档.md` | 开发全量记录:环境事实、血泪坑清单(含反射卡死等)、API 陷阱、每个功能的实现与迭代历史 |
| `文档/逆向文档.md` | 游戏文件地图(关卡/资产/程序集)、代码地图、可 hook 锚点、坑清单 |
| `文档/官方调试面板-解锁记录.md` | 游戏自带 F2 调试面板是什么、两处 `isDebugBuild` 门槛、如何解锁 |
| `文档/GUI安装器-设计记录.md` | GUI 安装器选型、视觉来源、体积构成、杀软处理 |
| `文档/第一人称调研.md`、`文档/PassthroughModding.md`、`文档/Passthrough-调研补遗.md` | 三份可行性/技术调研(结论与证据,含明确"做不到"的部分) |

## 附:工具性资料

- `_work/player_atlas/布局清单.md`:图集区域清单(1090 个区域),做换皮/换图集时查。
- `_work/设计决策.md`:若干实现取舍的记录。

## 已知限制(诚实清单)

- **刀光/剑光去不掉**:它是攻击时实例化的"特效骨架"(`NewPlayerTestEffect*`),目前没找到干净的拦截点,相关开关默认关闭;
- **真 3D 第一人称做不到**:游戏是 2D 分层美术,没有 3D 几何;能做的只有"贴眼视角";
- **改轮廓需要动 `.skel`**:运行时只能换资源引用,改顶点/比例属于"等长数值补丁"(工具链已打底,未完成);
- 借来的敌人骨架只有部分动作与主角同名(最多 7/22),动作会残缺;
- 换贴图吃显存:一张 2048² PNG 约 16MB,11 张全用约 200MB,不用时请关掉开关;
- 防反窗口拉太大会让"闪击反击"触发得过早,属于玩法破坏,自己看着调。

## 免责声明

- 本仓库**不含《ICEY》的任何游戏文件或素材**,也不提供游戏本体,请自备正版;
- 面向**单人、本地**使用;游戏本身没有联机与反作弊,本项目也不触碰任何在线服务;
- 一切改动可在运行时撤销,不修改游戏原始文件;
- 仅限个人学习与自用,请遵守游戏的最终用户许可协议。

## 第三方组件

| 组件 | 用途 | 许可 |
|---|---|---|
| [BepInEx 5](https://github.com/BepInEx/BepInEx) | 插件加载器 / Doorstop 注入 | LGPL-2.1 |
| [HarmonyX](https://github.com/BepInEx/HarmonyX) | 运行时方法拦截 | MIT |
| Spine Runtime | 由游戏自带,本项目不分发 | Spine 许可(Esoteric Software) |
| I2 Localization / CameraFilterPack | 由游戏自带,仅运行时调用 | 各自商业许可 |
| UnityPy / texture2ddecoder | 离线读图集、解码 DXT(开发工具,不随插件分发) | MIT |
| Minecraft 皮肤与物品贴图 | 方块人外观 | 素材版权归 Mojang;仓库内仅含自制/示例文件 |
