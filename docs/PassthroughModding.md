# Passthrough Modding(直通式 Mod)技术笔记

> 起因:主人让我"了解并记录 Passthrough Modding 技术"。
> 调研时间:2026-10-07。术语出处见文末**引用**(都有链接,不是我编的)。
> 说明:**web_search 工具被挡**(modsearch 的 firecrawl 无 key → 403),搜索引擎域名在本机 DNS 下被判"非公网 IP";
> 最后是用 **shell 走策略代理**直接打 GitHub API + `raw.githubusercontent.com` 取到的一手材料 ✔

---

## 1. 一句话定义

**Passthrough Mod = 让"两个游戏同时跑、并链接在一起",把 B 游戏的内容/系统"直通"进 A 游戏里。**
A 是**宿主(host)**,它自己的画面、操作、物理全都不动;B 在后台**隐着跑**,把它独有的系统(玩法/实体/AI)通过一座**桥**实时喂给 A。

出处原话(`trevaintdead/ai-game-modding-guides` README):

> **Passthrough mods:** two games running at once and linked together, like SkyCraft (Minecraft inside Skyrim).
> **Rust rewrites and ports:** rebuilding a game's engine in Rust so it reads data from your own copy, like IW4L.

也就是:**passthrough ≠ 重写引擎 ≠ 把资源塞进别人的文件**。它刻意**不碰目标游戏的资源文件**,
所以"不包含任何游戏文件"是这类项目的铁律(`GameMerge` README 原文:No game files included)。

## 2. 两种主流路线对照

| | Passthrough(直通) | Rewrite / Recomp(重写) |
|---|---|---|
| 做法 | 两个游戏各自跑,实时互通 | 用 Rust/Bevy 之类重写引擎,**读你自己那份**游戏数据 |
| 代表 | SkyCraft(MC 进老滚 5)、GameMerge | IW4L(MW2 2009)、benilla(WoW 1.12.1) |
| 改动量 | 一个插件 + 一个 guest 端 mod | 极大,但成果是独立程序 |
| 平台 | 基本**只保证 Windows**(Wine/Proton 不保证) | 跨平台(自己编) |
| 风险面 | 依赖两边的 mod loader / IPC | 依赖逆向与数据格式 |

## 3. 架构解剖(以 `GameMerge` 为例,写得最清楚)

```
PCSX2 #1(可见,宿主)                PCSX2 #2(隐藏,null 渲染器,客机)
  ↑ 城市/车/渲染/操作全保留            ↑ 只跑 Burnout 3 的玩法系统
  └──────── Rust 桥(PINE IPC)实时互传数据 ────────┘
     客机提供:takedown / 氮气 / 子弹时间 / 慢镜 / 撞击镜头 / aftertouch / Road Rage / Crash 模式
```

四个关键设计点:
1. **宿主不变**:宿主游戏的渲染、输入、存档照旧,玩家"感觉还是在玩原来那个游戏";
2. **客机隐藏渲染**:客机用 `null renderer` 把画面关掉,只留逻辑跑(省性能、也不抢窗口);
3. **桥是核心**:进程间通道(IPC/RPC/共享内存/模拟器自带 API,如 PCSX2 的 PINE),双向传状态与指令;
4. **两边都要有 mod 能力**:宿主侧要有脚本扩展器/BepInEx 之类,客机侧要有 Fabric/Forge/Lua 之类 ——
   官方指南明确说"**先查这个游戏有没有 mod loader,这决定你的想法现不现实**"。

## 4. 代表项目(可直接参考)

| 项目 | 说明 |
|---|---|
| [SkyCraft](https://github.com/chasmlol/SkyCraft) | **MC 进《上古卷轴5》**(脚本扩展插件 + Fabric mod)—— 这类项目的鼻祖级范例 |
| [FalloutCraft](https://github.com/zeyvu/FalloutCraft) | 同设计搬到《辐射4》 |
| [OWCraft](https://github.com/Yaekai/OWCraft) | 同设计搬到《Outer Wilds》,带开发日志 + 设计文档 |
| [GTA San AnSkateas](https://github.com/ryglizzy/GTA-San-AnSkateas) | 《Skate 3》的引擎跑进《GTA:SA》 |
| [wither-storm-gta5-passthrough](https://github.com/VortexisTV/wither-storm-gta5-passthrough) | MC 1.20.1 的凋灵风暴进《GTA5》洛圣都 |
| [GameMerge](https://github.com/siddharthakumar-98/GameMerge) | 《Burnout 3》玩法进《Midnight Club 3》(PCSX2 双实例 + Rust 桥) |
| [ai-game-modding-guides](https://github.com/trevaintdead/ai-game-modding-guides) | **给 AI 代理写的整套指南**(含 passthrough 专章、测试排错、法务、发布) |
| [game-modder](https://github.com/AaronWard/game-modder) | 指南手册 + 给代理的项目模板(封面就是"MC 小人站在《艾尔登法环》里") |

## 5. 和我们这个 ICEY 项目的关系(重要)

我们做的不是"双游戏直通",而是**同一族思路的"单进程内直通"**:不重打包资源,而是在运行时**借用游戏自己的对象与调用链**。
已经落地的四类,逐条对应(都有日志/文件证据):

| 类型 | 我们的实例 | 证据 |
|---|---|---|
| **数据直通** | 主角 6 个槽位的 `skeletonDataAsset` 热替换(借敌人骨架,一键还原) | `CheatPanelPlugin.Borrow.cs` |
| **数据直通** | 图集贴图运行时替换(配色预设 / 隐形形态),原贴图记账可还原 | `CheatPanelPlugin.Skin.cs`,`_skinWrites` 账本 |
| **调用直通** | Harmony postfix 挂在 `I2.Loc.TermData.GetTranslation` 上做文本替换 | 日志 `[text] 补丁已装` + 导出 1262 条术语 |
| **调用直通** | Harmony prefix 挂在攻击入口上做"MC 式攻击冷却"(可开关) | `CheatPanelPlugin.McPatches.cs` |
| **渲染托管** | 关掉原骨架渲染器,把自己的 6 面盒子挂进游戏的 sortingOrder/renderQueue 体系 | 截图 + 日志 `[mc] 材质: ... queue=3000` |
| **状态直通** | 读游戏自己的 `SkeletonAnimation.state.GetCurrent(0).Animation.Name` 驱动方块人动画 | `ReadAnimName()` |

**与"改 assets"路线的取舍(我们实测的结论)**
- 改 assets(把 `.skel`/`.atlas` 塞进去)要**重写整个 assets 结构**,长度一变后面所有对象偏移都得重算 → 高风险;
- 原地覆盖 resS(同尺寸纹理)是安全的,且可完整还原(我们有哈希证据);
- 运行时直通**天然可逆**:关开关即恢复,不碰游戏文件,也就**不会破坏联机/校验/更新**。

**如果哪天真要做"MC 进 ICEY"的双进程版**,形态会是:ICEY 当宿主(不动渲染/操作),
MC 客机(或一个 MC 协议模拟器,如原版服务端 + Fabric 端)隐着跑,再用一座桥(本地 socket/共享内存)
把实体、方块、伤害事件喂给宿主,由宿主的 BepInEx 插件把它们画出来 —— 也就是把我们现在的"方块人"换成
"**由真实 MC 逻辑驱动、数据从客机直通过来**"。第一步永远是最小可跑:先打通一条 ping/位置同步。

## 6. 规范清单(指南 + 我们踩过的坑合并)

**来自官方指南**
1. 先确认两边有没有 mod loader / 脚本扩展器,再谈可行性;
2. 游戏由**用户自己安装**,代理只读本机文件 —— **仓库里绝不提交游戏文件**;
3. 别碰 DRM / 反作弊;纯联机游戏不做;
4. 预期很多轮:先玩、再报现象、再改(第一版提示词几乎不可能一次成);
5. 保留原文件与存档备份;破坏性操作前先问;
6. 证据不足时**不许猜**(这条和我们项目里"先查实再动手"完全一致)。

**我们自己踩出来的(更具体)**
7. **别在启动路径上做重活**:我扫"所有程序集的类型 + 每个名字用 `AccessTools` 查一遍" → HarmonyX 每次失败写日志 → 8.4MB 日志把主线程卡死(游戏"未响应");
   正解:只扫目标游戏程序集 + 每类型一次 `GetMethods(DeclaredOnly)` + 进场景后再装。
8. **关键词匹配别用通用词**:用 `line`/`trail` 去藏特效 → 误伤场景装饰 17 个;
   正解:按**组件类型名/资源名**精确匹配,或从日志的"附带组件名清单"里读真身。
9. **一切可写的东西都要记账**(原贴图/原材质/原 `enabled`/原 asset),重扫时**不许覆盖**第一次记下的状态
   (我们就因为每 3 秒重扫把"已隐藏"当成原始状态,导致关不掉、人物消失)。
10. **能自证**:日志要打印命中哪个 shader、命中多少个、拦住几次 —— 否则只能靠猜,而猜一次就要让主人再进一次游戏。

## 7. 引用
- trevaintdead/ai-game-modding-guides(README,passthrough 定义与项目清单):https://github.com/trevaintdead/ai-game-modding-guides
- AaronWard/game-modder(指南 + 代理项目模板):https://github.com/AaronWard/game-modder
- siddharthakumar-98/GameMerge(双实例 + Rust 桥架构说明):https://github.com/siddharthakumar-98/GameMerge
- VortexisTV/wither-storm-gta5-passthrough:https://github.com/VortexisTV/wither-storm-gta5-passthrough
- Wikipedia:["Passthrough"](https://en.wikipedia.org/wiki/Passthrough)(通用含义:电子/架构/经济上的"透传、直通")
- ⚠ 未核实项:该术语**没有官方/学术定义**,以上两处社区仓库是目前能找到的最接近"权威用法"的来源;
  若主人指的是某个具体社区(例如某个 Discord/某位作者)的特殊用法,给我链接我再补。
