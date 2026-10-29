# Passthrough 调研补遗 · 「艾希能不能做直通 Mod」

> 起因:主人给了一份任务书和三个外部链接,要求**只调研、不动手**,
> 重点回答:MC 要什么版本 / 能不能互通 / 能不能融入那三份材料。
> 调研日期:2026-10-07 · 范围:**纯调研**,没有改任何游戏文件、没有启动任何游戏进程、没有动插件源码。
> 与前作 `PassthroughModding.md`(2026-10-07,定义/架构/规范)的关系:那份讲"是什么",这份讲"**在艾希上到底哪一半成立**",并且补了**本机实测的硬结论**。

---

## 0. 三条速答

| 问题 | 结论 |
|---|---|
| **MC 要什么版本?** | passthrough **不绑定某个 MC 版本**——版本由"你选的客机 mod"决定。当前生态主战场是 **26.3**(因为 26.x 不混淆,Mixin 直接打 Mojang 名);Forge 路线锁 **1.20.1**。**本机 14 个实例 + 5 套 Java 已全量盘过,两条路的工具链都齐,一个都不用装。** 见 §2、§7 |
| **能不能互通?** | **传输层能照抄,语义层必须重写。** ICEY 侧实测:**本机 TCP 可行**、**共享内存要自己 P/Invoke**、**具名事件可用**、**命名管道不可用**。见 §4 |
| **能不能融入那三份材料?** | 方法指南(guide 02)**能直接融入**;amiibodoctor 那篇是**博客/科普,没有技术件可依赖**,但能反过来用我们的实测去补它的空白;Nexus 那条**已查实**是同类项目,是**可参考的宿主侧实现**。见 §6 |

**最关键的一句(主人的假设要拆开看,而且结论比"一半"更狠):**
> "能用在 MC 就应该能用艾希" —— **技术上对,产品上不对。**
> 第一层:passthrough 的招牌是"把客机的**像素+深度**合成进宿主的帧",卖点是"两边互相遮挡";
> **艾希有深度缓冲,但深度里只有一张平面背景和几个 2D 精灵,没有世界几何** —— 这一半不成立。
> 第二层(★ 主人指出的,比第一层更准):**就算只做"数据直通",也只是能跑通、没有价值** ——
> 因为①降维后 MC 不再是 MC(要么变 Terraria,要么只是个精灵,30 行脚本就能造);②物理喂不动且手感是**相减**;
> ③**最要命的是这两个游戏根本不互补**(艾希没有世界/物品/建造,MC 给不了它需要的东西)。
> 所以准确回答是:**能做出来的是个 demo,不是个 mod。** 见 §5.5。
> 真正值得做的方向是 §5.6:**要么别用桥(内容移植 = 我们已经在做的),要么把不对称性反过来(MC 当宿主)。**

---

## 0.5 ★ 最终裁决:能不能做出网上那个"在 GTA5 里玩 MC"视频的样子?

> **不能。差距不是工程量,是维度。**

把视频里那个效果拆成四条,逐条对艾希:

| 视频里的效果(GTA5 × MC) | 它靠什么成立 | 艾希 | 结论 |
|---|---|---|---|
| **MC 的画面被 GTA 的建筑正确遮挡,也正确遮挡后面的车** | 宿主深度缓冲里装着**真 3D 世界几何** | 深度里只有一张平面背景 + 几个 2D 精灵 | ❌ **没有可遮挡的对象**。最卖座的那一帧做不出来 |
| **GTA 的 3D 相机驱动 MC 的相机**(自由视角/第三人称/升降绕) | 两个都是 3D,相机语义可互换 | 艾希是**2D 正交跟随相机**,只有 X/Y + 脚本化缩放抖动 | ❌ 用 2D 相机驱动 3D 世界视图不成立;硬加 3D 相机就不是艾希了 |
| **GTA 的地面变成 MC 的碰撞**(Steve 走在洛圣都街上) | Los Santos 是连续 3D 地形,可体素化 | 艾希是**手工搭的 2D 平台** | ❌ 只能挤成一条 1 格厚的板,Steve **走不出那个平面** |
| **Steve 飞 elytra、炸车、和 LAPD 打** | 有可自由活动的开放空间 | 艾希是**线性关卡 + 脚本化演出** | ❌ 要么被墙挡住,要么直接穿出关卡 |

**一句话**:
> 那个视频能成立的前提是"**两个都是 3D、都有世界几何、相机可互换**"。**艾希一条都不满足。**
> **所以那个样子在艾希上做不到——不是难做,是"没有地方让它发生"。**

**能做出来的最接近的东西,以及它离视频有多远**:
- 一个方块人在艾希的 2D 平面上跟着走、挥剑、被打 → **本质上就是一个自绘的 2D 精灵**,
  MC 需要在那里跑只是为了"真",而视频的震撼恰恰来自"两个真游戏在一个画面里互相作用"。
  少了那一帧合成,**它退化成"艾希里多了一个 MC 皮肤的角色"——而**这个我们现在已经有了**(`Blocky.cs`)**。
- 唯一形态上接近的可行版本是**反过来**:MC 当宿主(有 3D 世界和深度),在 MC 里做一个 2D 横版竞技场、
  把艾希的战斗系统搬进去。但那是"**在 MC 里玩艾希的战斗**",**不是"在艾希里玩 MC"**,而且艾希的画面进不去(素材不能进仓库)。

---

| 文件 | 来源 | 用途 |
|---|---|---|
| `guides_02-passthrough-mods.md` | `trevaintdead/ai-game-modding-guides` guide 02 | 方法论主源(定义/传输三条铁律/可行性判据/"行不通的想法"表) |
| `readme.txt` | 同仓库 README | 14 篇指南目录 + 项目清单 + 法务红线 |
| `guide08_loaders.md` | 同仓库 guide 08 | 引擎/加载器表(Unity → BepInEx/MelonLoader) |
| `guide09_worked.md` | 同仓库 guide 09 | 端到端实例流程 |
| `skycraft_readme.txt` / `skycraft_DESIGN.md` | `chasmlol/SkyCraft` | **架构范本**:组件图、坐标映射、协议消息表、分阶段计划、风险表 |
| `um_skill_mashup-mods.md` | `rehan-remade/universal-modder` | **五种 mashup 模式**的取舍(含 AI 专用规则) |
| `um_example_mc-gta5.md` / `um_kb_gta5_passthrough.md` | 同上 | MC×GTA5 直通实例 + **21 条踩坑实录** |
| `um_kb_blackmyth_depth-dead-end.md` | 同上 | **"深度合成走不通"的负结果**(UE5 + ReShade) |
| `um_kb_portalcraft.md` / `um_kb_hl2-guns-mc.md` | 同上 | Portal 2 宿主 / **MC 当宿主**的两个变体 |
| `terraria_example_README.md` | 同上 | **2D 游戏 + JSON-lines socket** 的 AgentBridge 先例 |
| `terraria_AgentBridge.cs` | 同上 | **已核实源码**:`new TcpListener(IPAddress.Loopback, Port)` + 换行请求/应答(2D 游戏里的本机 TCP 桥) |
| **`2d-host-feasibility.md`**(386 行) | 本报告派出的第二路专项调研 | **2D 宿主可行性专章**:2D 先例判定、宿主原生对象这条路、OWCraft 的四个 Unity/Mono 实锤坑 |
| `_raw\`(60+ 个快照) | 同上,抓回的一手证据 | OWCraft DESIGN/MODLOG、Unity 5.4 官方手册、MS Learn、Mono 源码树、各搜索引擎结果页。**纯 scratch,可删** |
| `witherstorm_readme.md` | `VortexisTV/wither-storm-gta5-passthrough` | Forge 1.20.1 改编版(版本分叉的实证) |
| `universal-modder_README.md` | `rehan-remade/universal-modder` | 工具链/知识库/规则 |
| `amiibodoctor.txt` | 主人给的博客 | §6.1 |
| `um_knowledge_INDEX.md` | `universal-modder` 知识库索引 | 按引擎/路线查先例 |

**网络状况(记录一下,别再摸索)**:`read_page` / `web_search` 依然 **403 不可用**;
shell 走本地策略代理**端口已变成 `127.0.0.1:7107`**(文档里写的 6092 是旧的,直接用会 `000`)。
可用姿势:`curl.exe -s -L --retry 3 -x http://127.0.0.1:7107 <raw 直链>`。GitHub raw 通;`api.github.com` 会 rate limit;Nexus 直连 **403(Cloudflare)**、API **401**,必须走 **Wayback**。

---

## 2. 代表项目的技术参数对照(全部有出处)

| 项目 | 宿主(host) | 客机(guest) | 客机 MC 版本 / 加载器 / Java | 宿主侧注入方式 | 传输 |
|---|---|---|---|---|---|
| **SkyCraft** | Skyrim SE/AE(1.7.104) | Minecraft **Java** | **26.3** + Fabric(+Fabric API 0.161.0+26.3) / **Java 25** | SKSE 插件(CommonLibSSE-NG,C++) | 具名共享内存 `Local\SkyCraft_v1` + 命名事件 + **共享 GPU 纹理** |
| **universal-modder MC×GTA5** | GTA V Legacy(build 3889) | Minecraft **Java** | **26.3** + Fabric(Loom) / **JDK 25** | ScriptHookV `.asi` + **ReShade add-on** | **本地 WebSocket `127.0.0.1:25599`** + 具名共享内存 `Local\MCPassthroughFrame`(画面) |
| **wither-storm × GTA5** | GTA V Legacy | Minecraft **Java** | **1.20.1** + **Forge 47.4.10** / **JDK 17** | 同上 | 同上 |
| **Minecraft PassthroughIV**(Nexus gta4/1498) | GTA IV Complete Edition 1.2.0.59 | Minecraft **Java** | **26.3** + **Fabric Loader 0.19.5+** / **Java 25** | **Ultimate ASI Loader(`dinput8.dll`)** + ReShade 合成 | **本地 WebSocket(Java-WebSocket)** |
| **FalloutCraft** | Fallout 4 | Minecraft Java | (SkyCraft 系) | F4SE 插件 | SkyCraft 同款 |
| **OWCraft** | Outer Wilds(**Unity**!) | Minecraft Java | (SkyCraft 系) | Outer Wilds Mod Loader | SkyCraft 同款 |
| **黑神话×MC** | Black Myth: Wukong(UE5) | (MC 从未安装) | — | RE-UE4SS / B1CSharpLoader | ReShade —— **深度读出来是平的,合成路线死在这里** |

**两条立刻能用上的判断**:
1. **版本不是约束,加载器才是。** guide 02 原文:"**Does the host game have a mod loader or script extender?** … With one, this is a realistic weekend project. Without one, the agent has to reverse engineer the game first." —— 艾希**有 BepInEx**,这一关直接过。
2. **OWCraft 证明 Unity 宿主可行**(它是 Unity + Outer Wilds Mod Loader)。艾希同为 Unity,而且我们的 BepInEx 已经能 hook 渲染与逻辑(`Skin.cs` 热换贴图、`McPatches.cs` prefix 拦攻击)——guide 02 说的"宿主 loader 能不能 hook 渲染和玩家代码"这条,我们**已经有实证**。

---

## 3. 传输层三档(按需选,不要一上来就做最重的)

| 档 | 手段 | 什么时候需要 | 出处 |
|---|---|---|---|
| **A. 本机 TCP / WebSocket** | 换行/帧分隔的 JSON,或 WebSocket,`127.0.0.1:25599` 这类 | **只要传数据**(位置/动作/血量/事件) | universal-modder:"control: JSON lines or HTTP";PassthroughIV 的 credits 列 `Java-WebSocket` |
| **B. 具名共享内存 + 命名事件** | `CreateFileMapping` → 头部(魔数/版本/双 PID/心跳)+ seqlock 的"最新值槽"+ 双向 SPSC 环形缓冲 + 命名事件唤醒 | 每帧高频数据、不想走内核拷贝 | SkyCraft DESIGN §10(写得很细,消息目录表也在那) |
| **C. 共享 GPU 纹理** | D3D11 shared NT handle / `WGL_NV_DX_interop2` / `VK_KHR_external_memory_win32`,失败退 PBO 回读 | **只有要做像素+深度合成才需要** | SkyCraft DESIGN §9、mashup-mods 第 2 条 |

guide 02 给的三条铁律(建议照抄进我们的实现记录):
- **一份 schema,两种语言**:消息只定义一次,两边生成/镜像,CI 跑布局测试(手写两份结构体第一次加字段就会漂);
- **全部定长 + 小端**:热路径不放序列化库;变长数据(比如一堆碰撞盒)在环形缓冲里用"计数 + 定长记录";
- **两边都要能扛住对方死掉**:心跳检测,一边死了另一边回安全态(SkyCraft:MC 死 → Skyrim 交还控制权;Skyrim 死 → MC 暂停)。**这条第一天就要定。**

> 对我们最划算的组合:**A 档起步**(就是提示词里的 `bridge.py` TCP + 换行 JSON),等真有每帧高频需求再上 B 档。

---

## 4. ★ 艾希侧实测硬结论(本报告新增,方法可复现)

**方法**:用 BepInEx 自带的 `Mono.Cecil.dll`,在 PowerShell 里 `AssemblyDefinition.ReadAssembly` 扫
`_moddev\ICEY_Data\Managed\` 下的程序集,按类型名/构造函数签名核对。**没有运行游戏、没有改任何文件。**

### 4.1 环境

| 项 | 实测值 | 证据 |
|---|---|---|
| Unity 版本 | **5.4.0f3**(a6d8d714de6f) | `ICEY_Data\globalgamemanagers` 头部字符串 + `output_log.txt` 首行 |
| `mscorlib.dll` | **2.0.5.0**(Mono 文件版本 3.0.40818.0) | Cecil |
| `System.dll` / `System.Core.dll` | **均 2.0.5.0** | Cecil |
| BepInEx 状态 | 已装,日志有 `Loading [ICEY Cheat Panel 0.5.0]` | `BepInEx\LogOutput.log` |
| 游戏进程 | **当时没在跑**(全程未启动/未结束任何进程) | `Get-Process ICEY` 空 |

### 4.2 跨进程能力逐条核对

| 能力 | 结论 | 说明 |
|---|---|---|
| `System.IO.MemoryMappedFiles`(`MemoryMappedFile`) | ❌ **不存在** | 三个程序集里 `MemoryMapped*` 命中 **0**。这是 .NET 4.0 才进 BCL 的;游戏是 .NET 2.0 档 |
| `System.IO.Pipes`(命名管道) | ❌ **不存在** | `System.dll` 里没有 `System.IO.Pipes.*` |
| `System.Threading.EventWaitHandle(bool, EventResetMode, string)` | ✅ **存在** | 具名事件跨进程唤醒可用(另还有带 `out bool createdNew` 的重载) |
| `System.Threading.Mutex(bool, string)` | ✅ **存在** | 具名互斥体可用 |
| `System.Threading.Semaphore(int, int, string)` | ✅ **存在** | 在 `System.dll` 里 |
| `System.Net.Sockets.TcpListener` / `TcpClient` / `Socket` / `NetworkStream` | ✅ **全部存在** | 本机 TCP 通道零门槛 |
| `UnityEngine`: `Texture2D.LoadImage` / `SetPixels32` / `Apply` | ✅ **存在** | 从桥收来的像素能塞进贴图(和现有 `Skin.cs` 同款手段) |
| `UnityEngine`: `Graphics.CopyTexture` / `Blit` / `DrawTexture`、`RenderTexture`、`GL` | ✅ **全部存在** | 即使要画"客机画面层",Unity 侧的原语也够 |

### 4.3 由实测直接得出的三条实现约束

1. **阶段 1 的 `bridge.py`(TCP + 换行 JSON)是唯一"零 P/Invoke"的路径**,而且被同类项目反向印证
   —— PassthroughIV 走的就是本机 WebSocket。**先走这条,别一上来写共享内存。**
2. **想要 SkyCraft 那套具名共享内存,就得自己 P/Invoke**:`CreateFileMappingW` / `MapViewOfFile` / `UnmapViewOfFile` / `CloseHandle`。
   可行(就是 DllImport),但**必须自己写头部校验、seqlock、环形缓冲和崩溃恢复**——BCL 一点忙都帮不上,工作量比在 .NET 4 上大一截。
   注意 mashup-mods 的原文写的正是 `CreateFileMapping`,说明社区本来就不指望 BCL 那层。
3. **具名事件/互斥体可以直接用**,所以"B 档"里最省事的那半(握手唤醒、单实例锁)不需要额外代价。

### 4.4 ★ OWCraft 的四个 Unity/Mono 实锤坑(第二路调研带回,直接决定协议怎么设计)

来源:`Yaekai/OWCraft` 的 `MODLOG.md`。**OWCraft 是这一族里唯一的 Unity 宿主**(Unity 2019.4 + Mono + C#),
所以它的坑对我们**几乎是逐条命中**。这四个都是"踩了才写下来"的,不是推测:

| # | 坑 | 症状 → 原因 → 修法 |
|---|---|---|
| ① | **Mono 的 `Stopwatch.GetTimestamp()` 和 kernel32 `QueryPerformanceCounter` 不是一个时间基** | 插值一直被钳在 0/1、玩家 20Hz 台阶跳。原文:"Under Mono that counts 100 ns units from its own origin, so the age was nonsense" → **时间戳必须两边同源**:P/Invoke `QueryPerformanceCounter`。**或者干脆别用绝对时间戳,只用序号 + 本地插值** |
| ② | **共享内存名字里的反斜杠会被配置系统吃掉** | 日志打出 `Local/OWCraft_v1`——OWML 的设置系统把 `\` 变成了 `/`。→ **`Local\Name` 硬编码在代码里,绝不走配置文件**(BepInEx 的 `.cfg` 同理,一个转义就能静默废掉整条桥) |
| ③ | **宿主重启时把共享头部清零,抹掉了客机 pid** | "每 pid 只做一次"的逻辑永不触发。→ 重置头部时**保留对方字段**,别把一次性逻辑挂在 pid 上 |
| ④ | **隐藏窗口的 present 被 Windows 限流** | MC 帧"averaged exactly 40.0 ms (25 fps)"→ **跳过 present 后 25→60fps**。→ 客机渲染要绕过 present 限流 |

### 4.5 关于"`.NET 2.0 Subset` 会不会把 socket 剔掉"——**本项目已实测排除**

第二路调研提出一个待验证风险:Unity 5.4 的 Player Settings 提供 `.Net 2.0` 与 `.Net 2.0 Subset` 两档,
社区称 Subset 会剔掉 socket/网络 API(无官方清单)。**这条对艾希不成立**,证据是本次 §4.2 的反编译结果:
- BepInEx 插件编译时 `/reference:` 的就是 `ICEY_Data\Managed\System.dll`,**运行期 Unity 的 Mono 加载的也是同一份**;
- 而这份 `System.dll` 里 **`TcpListener` / `TcpClient` / `Socket` / `NetworkStream` 实测全部存在**。
→ **TCP 通道在本项目可用**。残留风险只剩"Unity 构建期的托管代码剥离",而 ICEY **实际发布出来的这份 System.dll 里类型就在**,这个风险同样不成立。
(仍然建议:**第一次实测只发一个包验证**,别一上来把整条管线压在它上面。)

---

## 5. ★ 「能用在 MC 就能用在艾希」拆开看

### 5.1 成立的那一半:**数据直通**

宿主(艾希)提供世界与物理,客机(MC)提供玩法与实体,两边用桥换数据,艾希把客机的实体**画成自己的 2D 实体**。
这在艾希上不但成立,**而且起点已经建好了**:

| 现成资产 | 在直通架构里的角色 |
|---|---|
| `CheatPanelPlugin.Blocky.cs`(972 行,真 6 面盒子 + 程序化 MC 动画 + 手持物 + UV 照 MC 盒展开) | **外部实体渲染器**。现在数据源是"跟随主角/读游戏动画名",改成"读桥的 JSON"即可 |
| `CheatPanelPlugin.Skin.cs`(贴图/材质热替换 + 账本 + 精确还原) | 客机传"材质/皮肤变体"时的运行时贴图切换 |
| `_work\mc_assets\`(官方 skin + 5 把剑贴图) | 实体外观素材 |
| `CheatPanelPlugin.McPatches.cs`(Harmony prefix 拦攻击冷却) | "客机发来的伤害/事件"接进艾希玩法层的入口范例 |
| `CheatPanelPlugin.Borrow.cs`(借敌人骨架数据,可一键还原) | 真想上 MC 形态时的骨架来源 |

### 5.2 不成立的那一半:**像素 + 深度合成**

**先把话说准**:艾希**有**深度缓冲,不是"没有"。项目自己的方块人就是**真 6 面立方体靠深度缓冲自遮挡**的
(`CheatPanelPlugin.Blocky.cs` 头注释:"shader:优先找'不透明/会写深度'的(真 3D 盒子靠深度缓冲自遮挡)")。
**问题不在"有没有深度",而在"深度里有没有可用的世界几何"。**

- guide 02 的"行不通的想法"表里明确写着:**"Running the gameplay game truly headless — The visuals are the whole point. SkyCraft hides the window but still renders into an offscreen texture, because the host needs depth to composite against. Without rendering there's no mod."**
- 既有 passthrough 实现**全都依赖宿主的深度缓冲里装着"世界"**:SkyCraft 拿 Skyrim 的城镇/地形深度,让 MC 的方块被城墙正确遮挡、也正确遮挡后面的车;**深度合成卖的就是这个"互相遮挡"**。
- **艾希的深度里装的是**:一张平面背景 + 几个 Y 层次上的 2D 精灵 + 主角。**没有"世界几何"可言**。
  所以"把 MC 的画面按深度合成进来"这件事**没有意义**:MC 的像素要么永远压在最前面(看起来像贴纸),
  要么被背景整片挡住(什么也看不见)。**中间没有可解释的层次。**
- 结论:不是"技术上禁止",是"**这一环的语义在 2D 宿主上不成立**"。
  替代方案不是硬做深度,而是**放弃像素合成,把客机的状态交给艾希的渲染器画成 2D 实体**(§5.3 模式 2 的数据版)。
- 反向印证:`um_kb_blackmyth_depth-dead-end.md` 记了一条**负结果**——ReShade 6.8.0 明明 hook 上了 D3D12、
  也检测到 depth-stencil,但读出来的深度**整帧是平的**(左半法线全 `(127,127,255)`、右半深度全 `1.0`,
  512 万像素只有 1 种颜色),原文结论:"**depth-correct compositing is not reachable through ReShade here**"。
  **连 3D 宿主都会在这一环翻车**——这条是"别把工期压在深度合成上"的最好证据。

#### ★ 5.2.1 补:官方自己留了第二条路,而且唯一的 Unity 先例走的就是它

第二路调研补上了我第一版漏掉的一条。mashup-mods SKILL 的 Pattern 2 第 3 步原文是:

> "**Host injection:** a host-side plugin … draws the guest's geometry inside the host's pass. It uses the host's view-projection matrices … and depth buffer,
> **or spawns host-native objects so host lighting and shadows apply.**"

**关键证据:这一族里唯一的 Unity 宿主 OWCraft 走的是后一条**——它**根本没做深度合成**:Minecraft 的每个 16³
section 直接**变成宿主 Unity 的 mesh**(用游戏自己的 shader),HUD/物品栏走**无深度**的全屏 RGBA overlay(IMGUI)。
(来源:`Yaekai/OWCraft` 的 `docs/DESIGN.md`,快照在 `_raw\owc__DESIGN.md`。)

**这条对 2D 完全可搬**,而且 2D 有现成的官方对应物 —— Unity 5.4 的 SpriteRenderer 自带层级:
> "**Sorting Layer / Order In Layer** — The overlay priority of this sprite within its layer."
(来源:Unity 5.4 官方手册 `class-SpriteRenderer.html`)

**落地方案(照抄 OWCraft 的思路,换成 2D 的原语)**:
- 客机实体 → **宿主原生 Sprite/自绘网格**,`sortingOrder = f(客机层级, y)` —— **这正是 `CheatPanelPlugin.Blocky.cs` 已经在做的事**(它就是把自绘网格挂进游戏的 `sortingOrder` / `renderQueue` 体系)。
- 客机的 UI/HUD → **全屏无深度 overlay**(和 OWCraft 的 IMGUI overlay 同型)。
- **别在 2D 管线里造深度缓冲**(Godot 社区为这事开过 proposal,说明这不是常规能力)。

> 所以准确说法是:**"像素+深度合成"这一环在 2D 宿主上不成立,但"把客机内容变成宿主原生对象"这条路是通的、而且有 Unity 先例。**
> 我第一版把替代方案写成了"只能是数据版",方向没错但漏了这条**官方承认的正式路径**。**修正后:渲染这一环的技术风险比我说的更低。**

### 5.3 替代路线(mashup-mods 的五种模式,逐条判艾希适配度)

| 模式 | 内容 | 对艾希的适配度 |
|---|---|---|
| **1. 内容移植** | 把客机的内容当**宿主自己的内容**重做,无 IPC | ✅ 最容易,我们其实已经在做(方块人 + MC 素材)。稳、无桥、任何有 loader 的宿主都行 |
| **2. passthrough** | 双进程 + 桥 | ⚠️ **要降级**:保留"数据直通",砍掉"像素/深度合成",客机实体改由宿主生成**原生 Sprite/网格**(§5.2.1,有 OWCraft 先例) |
| **3. 把反编译版当库内嵌**(如 libsm64) | 宿主喂碰撞、库返回状态 | ⚠️ 需要 MC 的反编译/重实现,坑深,不适合第一版 |
| **4. 重实现后融合** | 最重,完全掌控 | ❌ 不是本次范围 |
| **5. 把客机规则无头重写成纯模拟,宿主只当视图** | 客机**规则**是纯数值模拟,宿主管画面 | ✅✅ **对 2D 宿主往往最稳**(不注入任何在线客机)。BTD6 进 Minecraft 就是这么做的 |

**建议的取舍**:阶段 1 走 **模式 2 的数据版**(真 MC 逻辑驱动方块人,客机实体由宿主生成原生 Sprite);
如果发现"让 MC 在后台跑"太重或太麻烦,**模式 5** 是随时可退的台阶(一个纯 Python/C# 的 MC 规则模拟器喂同一套 JSON 协议,桥和渲染器一行都不用改)。

### 5.4 一句话结论

> 艾希能做的是 **passthrough 的"数据版"**:宿主照旧(2D、BepInEx、不碰资源),客机在后台跑并**只输出状态**,
> 艾希用**已有的方块人渲染器**把状态画成 2D 实体。**"把客机的画面合成进来"这一步在 2D 宿主上不存在**,
> 别把工期压在它上面。

### 5.5 ★★ 修正:主人指出"很可能做不到互通,因为艾希是 2D" —— **这个判断比我上面写的更准**

我上面 5.1 说"数据直通成立",那是**技术上成立**。但"技术上能传字节"和"能做出值得做的东西"**是两回事**。
把 2D 这件事想透,结论要往下调一档:

**理由一:降维之后,MC 不再是 MC —— 那还要 MC 干什么?**
- 3D 体素世界塞进一个 2D 平面,只有两种取法:
  - 取**一个深度切片** → 等于把 MC 变成 Terraria,**体素世界这个卖点直接归零**;
  - 取**位置 + 动作名 + 血量** → 那就只是一个精灵。**这个数据量,30 行 Python 随便造,根本不需要跑一个 Minecraft。**
- **桥变成了空转的仪式**:客机提供不了宿主自己造不出来的东西,直通就失去意义。
- (对照 §5.2.1:OWCraft 能把 MC 的 section 变成宿主 Unity **mesh**,是因为 **Outer Wilds 是 3D**,
  mesh 落进 3D 空间里是对的。**2D 宿主再往下压一层,就只剩一个平面上的方块贴图** —— 正好落回上面这句。)

**理由二:物理喂不动,而且是相减不是相加**
- SkyCraft 的核心价值是**双向物理**:宿主碰撞形状喂给客机物理查询,MC 的移动跑在 Skyrim 的地形上。
- 艾希这边:它的碰撞是**手工设计的 2D 关卡**(平台 + 边界),把 MC 的碰撞塞进去,唯一能做的就是"把平台挤成一条 1 格厚的板"。
- 更要命的是**手感不叠加**:**艾希是高速连段/空战动作游戏**(冲刺、浮空连、演出式战斗),**MC 是慢速走跳**。
  两个塞一起不是"1+1",是**互相拖累**——MC 让艾希变钝,艾希让 MC 没法玩。Skyrim 之所以吃得下 MC 的移动,是因为 Skyrim 本身也是慢速第一人称探索。

**理由三(最要命):这两个游戏根本不互补 —— 直通成立的前提就不在**
- 去看所有成功的 passthrough,它们成立的真正原因是**两边互补**:
  Skyrim 给"世界 / NPC / 任务 / 存档",MC 给"移动 / 建造 / 战斗系统"。**两个都是富系统,而且拼起来更强。**
  Guide 02 的原话也正是这条:"**Skyrim tells Minecraft what the world is shaped like and where the NPCs are. Minecraft tells Skyrim where the player is and what to draw on top.**"
- **艾希什么都没有可给,也什么都不需要**:
  - 它是**线性关卡 + 脚本化演出**的短流程动作游戏(**没有世界模拟、没有探索、没有物品栏、没有建造**)。
  - 它有的是**一套打磨好的连段战斗系统 + 一个元叙事 narrator**——这两样 MC 一样都用不上;
  - MC 有的是**体素世界 + 建造 + 生存**——这三样艾希一样都装不下(塞进去就直接破坏手工设计的关卡和演出)。
- 一句话:**SkyCraft 成立是因为"世界模拟"和"玩法系统"互补;艾希和 MC 之间,没有任何一边能补另一边。**
  **这不是 2D 的技术问题,是"设计上不互补"的问题——而 2D 只是把这个问题放大了。**

**结论(把话说到位)**
> passthrough 在艾希上:**技术通道能跑通,产品价值是零。**
> 换句话说——**能做出来的是个 demo,不是个 mod。**
> 主人说的"很可能做不到互通",在**工程意义**上可以说"能",在**mod 意义**上应当直接回答:**做不到。**

**★ 但要分清:两路调研之后,"技术难度"是往下调的,"产品价值"没动。**
- 下调的是渲染那一环:§5.2.1 找到了官方承认的第二条路(生成宿主原生对象)+ Unity 先例(OWCraft),而**我们连现成的方块人渲染器都有了**。
- 没动的是上面三条理由——它们**不是技术问题**。换句话说:**这条路的瓶颈根本不是"能不能做出来",而是"做出来没意义"。**
  所以别再往"怎么把技术打通"上使劲了,那个问题**已经有解**;真要有价值,得先回答**"艾希要 MC 的什么"**,而目前找不到答案。

### 5.6 那正确的方向是什么?—— **把不对称性反过来用**

有个关键的不对称,值得记下来:

> **3D 宿主吃得下 2D 客机;2D 宿主吃不下 3D 客机。**

- 3D 宿主(MC / GTA / 上古卷轴)有**深度缓冲和真世界几何**,2D 客机的内容进去可以当**一层玩法 / UI / 精灵平面**被正确遮挡、正确摆放 → **成立**。
  已证实的先例就摆在知识库里:`Half-Life 2 guns in Minecraft (passthrough)` —— **宿主是 MC**。
- 2D 宿主(艾希)没有世界几何,3D 客机的内容进去**无处安放** → **不成立**(就是 §5.5)。

**所以真正的三条路,按推荐度排序:**

| 优先级 | 路线 | 说明 |
|---|---|---|
| **1(推荐)** | **内容移植(模式 1),不要桥** | 就是**我们已经在做的形态**:方块人 + MC 素材 + MC 式攻击冷却。**没有第二进程、没有 IPC、没有法律面**,而且它给艾希的是**真实增益**。真想要"MC 味",这条路是对的 |
| **2(如果主人要的是"艾希的内容进 MC")** | **反过来:MC 当宿主,艾希当客机** | 不对称性站在我们这边(同型先例已证实)。艾希提供的是**一套 2D 连段战斗系统**,这在 MC 里天然可以是一层玩法/一个 2D 竞技场维度。**风险:艾希的素材不能随仓库发,只能在用户自己的安装上运行时转换**(参考 BTD6 → MC 的 converter 做法) |
| **3(只想要 demo / 想给 amiibodoctor 写一篇)** | 纯演示版数据直通 | MC 后台跑,实体投影到艾希 2D 平面上当"影子/贴纸"。**必须明说这是玩具,不是 mod**;价值在文章和实测(§4 那些硬数据),不在游戏 |

**★ 万一真要走路线 2 或 3,有一条设计决策必须先定死(第二路调研给的,和 SkyCraft 相反)**
- SkyCraft 让 **MC 权威于玩家位置/物理**(MC 的移动算完,再把坐标塞给 Skyrim 的木偶)。
- 艾希必须**反着来**:因为**横版手感就是艾希的命根子**,所以 **艾希权威于移动 / 碰撞 / 关卡**,
  客机只权威于**它自己的规则**(血量、AI、物品那一层)。
- 理由:一旦让客机权威于移动,艾希的连段/冲刺/浮空会被 MC 的慢速物理直接吃掉(§5.5 理由二)。
- **这条要写进 DESIGN 再动手**——权威划分搞反了,后期改起来极贵(guide 09 把它列进常见返工点)。

---

## 6. 主人给的三条链接,逐条评估

### 6.1 `amiibodoctor.com/2026/10/03/what-is-passthrough-modding/`

- **性质:博客/科普问答帖,不是框架、不是工具、不是规范。** 正文只有 2270 字符。所以**没有技术件可以"融入"当依赖**。
- 它的正文自己承认没搞清关键机制(原文):
  > "I'm not entirely sure how the games stay in sync, because nobody's explained the timing and setup, and most of this is clips rather than releases with some commentary that I need to watch. So… file it under **'very cool, unverified.'**"
- 它列的例子(原文):Tobyn Jacobs 的 **Minecraft 进《艾尔登法环》**、Chasm 的 **MC × Skyrim**、X 上的 Tarkov 机制进 Skyrim / MC 移动进 GTA V / CoD:WaW 僵尸配 Mario Kart;并明确区分"**Recomp/反编译 ≠ passthrough**"。
- **能不能融入?** —— **作为引用可以,作为依赖不行。** 但反过来有个价值点:**它缺的正是"同步机制"和落地细节,而我们手上有 guide 02 + SkyCraft DESIGN 的一手材料,加上本次艾希侧的实测**。如果主人想往那边写东西,这些正好能补它的空白(尤其"**passthrough 在一款 2D Unity 游戏上哪一环不成立**"是个没人写过、我们实测过的角度)。

### 6.2 `github.com/trevaintdead/ai-game-modding-guides/blob/main/guides/02-passthrough-mods.md`

- **性质:方法论。可以直接融入,而且我们的提示词已经和它同源同调。** 逐条对照:

| 指南要求 | 我们现状 |
|---|---|
| 先查宿主有没有 mod loader / 脚本扩展器 | ✅ 有 BepInEx 5 + HarmonyX |
| 两个游戏都要有 mod 能力 | ✅ 宿主侧插件在跑;客机侧 Fabric 就绪(本机已装) |
| 仓库里**绝不提交游戏文件**,玩家自备 | ✅ 现有铁律第 2 条就是"不改原版游戏文件 + 记账可还原" |
| 别碰 DRM / 反作弊;纯联机游戏不做 | ✅ 艾希无壳、无联机、无反作弊 |
| 从小目标开始,一步一步 | ✅ 提示词的分阶段(打通 ping → 驱动方块人 → 多实体 → 接真 MC)和指南给的阶梯一致 |
| 宿主 loader 要能 hook 渲染和玩家代码 | ✅ 已实证(热换贴图/材质、Harmony 拦攻击、自绘网格挂进 sortingOrder) |
| 客机要能注入物理 | ✅ MC + Fabric 过关(指南原话:"**Minecraft passes both because Fabric gives you a mod inside it and it has an integrated server**") |
| "版本会咬人":记下你测过的版本 | 待做(见 §7) |
| 预期很多轮:先玩、再报现象、再改 | ✅ 和本项目"让主人在真机上验"的工作流一致 |

- 它还有个**"错配的引擎对会把难度翻倍"**的警告("A mismatched pair doubles the problem")。这条**对我们不构成障碍**:它担心的是"宿主和客机同时都缺 mod 能力",而我们是**两边都齐**(宿主 Unity/C#、客机 Java),跨语言不影响——桥是字节流/文本,和语言无关。SkyCraft 自己就是 **C++ 宿主 + Java 客机**。

### 6.3 `nexusmods.com/gta4/mods/1498` —— 已查实

**直连 403(Cloudflare)、API 401,最后走 Wayback 拿到整页。**

| 项 | 事实 |
|---|---|
| 标题 | **Minecraft PassthroughIV (NOW WORKING ON WINDOWS)** |
| 分类 / 版本 | Grand Theft Auto IV / Gameplay · **v0.2.2** · 2026-10-02 上传 |
| 作者披露 | 页面写 **"Created by: Claude"**、Uploaded by **Akatoshthe2nd** |
| 摘要原文 | "Real Minecraft Java Edition runs next to GTA IV and is drawn into it. GTA's camera drives Minecraft's, GTA's ground becomes collision in Minecraft, and Minecraft's picture is depth-tested against GTA's, so a car hides Steve and its windows show him." |
| 宿主 | GTA IV: The Complete Edition **1.2.0.59** |
| 客机 | Minecraft Java **26.3** + **Fabric Loader 0.19.5+** + Fabric API + **Java 25**;页面明确写 **"Both games on the same PC"** |
| 宿主侧注入 | **Ultimate ASI Loader(`dinput8.dll`)**——**不是** ScriptHook / .NET ScriptHook;代码模式与 native 哈希参考了 **FusionFix**(ThirteenAG) |
| 传输 | credits 列 **Java-WebSocket**(TooTallNate)→ **本地 WebSocket**;画面 depth-test 合成 |
| 谱系自证 | 页面写明基于 **Rehan / universal-modder** 的 MC×GTA5 直通(MIT) |

**证据**:Wayback 整页 `http://web.archive.org/web/20261004000635/https://www.nexusmods.com/gta4/mods/1498`；
DuckDuckGo HTML 精确短语命中标题与 credits 原句；universal-modder README 的 `examples/minecraft-gta5-passthrough` 印证谱系。**未取到** GTA IV 版专属的第三方独立评测或代码仓库。

**对我们的意义(这条最有用)**:
1. 它证明"**把这套东西的宿主换掉**"有人**已经干过一次**——从 GTA V 换到 GTA IV,而且**是 AI 干的**(页面自曝 Claude 创建)。**换到 Unity 宿主在工程难度上是同类事情,不是天方夜谭。**
2. 它的**宿主侧依赖是 ASI Loader + WebSocket**,不是 ScriptHook —— 说明**宿主侧越小越好**这个思路是主流。
3. 客机那半个 Fabric mod **是 MIT、有公开前身**;我们的工作重心应该放在**宿主侧(艾希插件)**和**协议语义**上。

---

## 7. 本机现状盘点(决定第一版挑哪条路)

### 7.1 MC 侧(实测,**全量 14 个实例**)

> ⚠️ **上一版这段是错的,已更正**:第一版只扫了 PATH 里的 `java`,漏了 PCL 管理的 `D:\Games\MC\jre\` 和
> Mojang 的 `runtime\` 目录,还因为 PowerShell 把 `[` `]` 当通配符漏报了两个实例。
> 主人指出"本机有很多 MC"之后重新全量核对,结论**反过来了**:**Java 全齐,一个都不用装。**

**A. MC 实例(逐个解析版本 json 得出 `fmlloader` / `fabric-loader` 精确版本)**

| 实例目录 | MC | 加载器 | Java | 适合作直通客机? |
|---|---|---|---|---|
| `1.18.2-Fabric 0.19.3` | 1.18.2 | Fabric 0.19.3 | 17 | ✅✅ **最干净**(mods 里只有 `fabric-api-0.77.0+1.18.2.jar`),且**现有 MC 素材就是 1.18.2 的** |
| `meteor` | **26.1.2** | Fabric 0.19.3 | **25** | ⚠️ 版本最贴近 26.3 主线,但是**重度改造客户端**(Meteor Client、Sodium、Iris、ViaFabricPlus、Xaero 全家桶,25+ mod) → **只适合当"版本可行性"参考,不适合当客机** |
| `Create-Delight-Remake` | 1.20.1 | **Forge 1.20.1-47.4.10** | 17 | ✅✅ **正好命中 wither-storm-gta5 要求的 Forge 47.4.10**——想抄 Forge 版路线,这个实例现成 |
| `ZombieCraft：Zombie Apocalypse` | 1.20.1 | Forge 1.20.1-47.4.18 | 17 | ⚠️ 同 Forge 1.20.1 系,但版本号不等于 47.4.10 |
| `Better MC [FORGE] BMC4` | 1.20.1 | Forge 1.20.1-47.4.20 | 17 | ⚠️ 同上 |
| `Backrooms：Found Footage` | 1.20.1 | Fabric 0.19.3 | 17 | ⚠️ 整合包 |
| `更完美的MC  1.20.1` | 1.20.1 | Fabric **0.16.9**(旧) | 17 | ❌ 加载器太旧 |
| `Better MC [FABRIC] - BMC2` | (1.20.1 系) | Fabric | — | ❌ 整合包 |
| `Ultramodern optimization-0.3` | **1.21.1** | **NeoForge 21.1.224** | 21 | ⚠️ 如果要走 NeoForge 路线才有用 |
| `1.20.1` | 1.20.1 | **原版(无加载器)** | 17 | ✅ 干净的**原版底板**,可以往上装 Forge/Fabric |
| `1.18.2` | 1.18.2 | **原版** | 17 | ✅ 干净底板 |
| `1.10.2` | 1.10.2 | 原版 | 8 | ❌ |
| `1.16.5-Fabric 0.19.3-OptiFine_G8` | 1.16.5 | Fabric 0.19.3 | 8 | ❌ 太旧 |
| `Xiaoben's Diamond Land & Slash Blade` | (1.10.2 系) | 原版 | 8 | ❌ |

**B. Java(5 个,实测路径 + 是否有 `javac`)**

| 版本 | 路径 | `javac`? | 用途 |
|---|---|---|---|
| **Java 25**(25.0.1 LTS) | `C:\Users\Administrator\AppData\Roaming\.minecraft\runtime\java-runtime-epsilon\bin` | ✅ **有**(`javac.exe` / `jar.exe` / `jshell.exe` 都在)→ **是完整 JDK,不是 JRE** | **26.x / Fabric + Loom 路线** |
| **Java 17**(17.0.18) | `D:\Games\MC\jre\jdk17` | ✅ | **Forge 1.20.1 / 1.18.2 路线** |
| Java 21(21.0.10) | `D:\Games\MC\jre\jdk21` | ✅ | NeoForge 1.21.1 |
| Java 22(22.0.2) | `D:\Games\MC\jre\jdk22` | ✅ | 备用 |
| Java 8(1.8.0_482) | `D:\Games\MC\jre\jdk8` | ❌ 纯 JRE | 1.10.2 / 1.16.5 |

→ **结论:两条路的工具链本机都齐了。走 26.3 用 Java 25(JDK,有 javac);走 Forge 1.20.1 用 JDK 17。一个都不用装。**
(PATH 里那个 Temurin 21.0.12 只是恰好也在,不是唯一。)

**C. 顺便记一个小坑(本项目会反复踩)**
MC 实例目录名里带 `[` `]`(如 `Better MC [FORGE] BMC4`)。PowerShell 的 `-Path` / `Join-Path` 把 `[` `]` **当通配符**,
`Test-Path` 会**误报"目录不存在"**(我第一次就中招,把两个存在的实例判成不存在)。
→ **凡是要碰 MC 实例路径,一律用 `-LiteralPath`;`Get-ChildItem` 同理。**

**D. 版本选择建议(有据,已按实测更正)**

| 你的目标 | 建议客机 | 本机是否就绪 | 理由 |
|---|---|---|---|
| **最小成本、先证明桥** | **1.18.2 + Fabric** | ✅ **完全就绪**(JDK 17 有;实例只有 fabric-api) | 实例干净、**现有 MC 素材就是 1.18.2**;只需写一个小 Fabric mod 发 JSON |
| **想抄现成代码(推荐主线)** | **26.3 + Fabric Loader 0.19.5+** | ⚠️ **Java 25 已就绪**;MC 版本要新开实例(现有 `meteor` 是 26.1.2 + Fabric 0.19.3,差两个小版本,而且是重度改造客户端) | SkyCraft / universal-modder / PassthroughIV 全在这条线;26.x 不混淆,Mixin 直接用 Mojang 名 |
| **想抄 Forge 版实现** | **1.20.1 + Forge 47.4.10** | ✅✅ **`Create-Delight-Remake` 就是 `1.20.1-47.4.10`** + JDK 17 | 正好命中 wither-storm-gta5 的要求,**本机已有该实例** |
| **想用特定客机 mod** | 跟着那个 mod 走 | — | 例:凋灵风暴 = 1.20.1 + Forge 47.4.10 + JDK 17 |

> 注意:**艾希这边不需要 MC 版本一致**,只需要**协议一致**。MC 版本只影响"客机那半个 mod 怎么写"。
> 另外——**直通客机要单独一个干净实例**(通用指南明确要求:桥梁会自建/自开一个 creative 虚空世界并改图形与焦点设置)。
> 本机最干净的起点是 `1.18.2-Fabric 0.19.3` 或原版 `1.20.1` 底板,别拿 `meteor` 当客机。

### 7.2 宿主侧(艾希)

- Unity **5.4.0f3** / Mono / .NET **2.0** 档;BepInEx 5 已装、插件在跑。
- 11 个 partial 源码,含 972 行的方块人渲染器 —— **外部实体渲染器已就绪**。
- 缺口:**没有任何 socket / IPC 代码**;协议、状态机、崩溃恢复、实体数组渲染、插值,全都要新写。

---

## 8. 硬规矩与风险(合并指南 + 本项目血泪)

**红线(照旧,不得违反)**
1. 不许启动/关闭游戏进程;游戏在跑时只能 `build.ps1 -SideBuild`。
2. 不许改原版游戏文件;一切记账可还原 + 哈希校验。
3. **仓库里只放代码,绝不提交任何游戏文件**(MC 或 ICEY)——这条恰好就是 passthrough 社区的铁律(`GameMerge`:No game files included)。
4. 不碰 DRM/反作弊;只本地离线玩。艾希无壳无联机,天然干净;**别去注入 MC 的在线服务**。
5. 别在启动路径做重活(8.4MB 日志卡死主线程那次教训)。

**新增风险(这次调研暴露出来的)**
| 风险 | 说明 | 缓解 |
|---|---|---|
| 共享内存要自己 P/Invoke | .NET 2.0 没有 `MemoryMappedFile`,头部/seqlock/环缓都要手写 | **先走 TCP**,B 档作为后续优化 |
| **时间基不同源**(实锤) | Mono 的 `Stopwatch.GetTimestamp()` ≠ kernel32 QPC → 插值会被钳在 0/1(OWCraft 踩过) | 时间戳统一 **P/Invoke `QueryPerformanceCounter`**,或只用序号 + 本地插值 |
| **共享内存名被配置系统改写**(实锤) | OWML 把 `Local\OWCraft_v1` 的 `\` 变成 `/` → 静默失效 | `Local\Name` **硬编码**,绝不走 `.cfg` |
| 头部重置抹掉对方 pid(实锤) | "每 pid 一次"的逻辑永不触发 | 重置时保留对方字段,一次性逻辑别挂 pid |
| 隐藏窗口 present 被限流(实锤) | 帧率被压在 25fps(正好 40ms) | 客机渲染绕过 present(OWCraft:25→60fps) |
| 2D 无深度合成 | 别把工期压在"把 MC 画面合进艾希"上 | 走**数据版 + 宿主原生 Sprite**(§5.2.1,有 OWCraft 先例);必要时退到模式 5 |
| MC 侧实例被污染 | 现成 26.1.2 实例是重度改造客户端 | **单开一个专用干净实例**(指南明确要求) |
| 两边帧率不同步 | 宿主 60fps 与客机 20 TPS | 容忍度 + 插值(方块人已有渲染帧)+ 上面那条"时间基同源" |
| 一边死了另一边僵住 | 指南要求第一天就定 | 心跳 + 超时 → 方块人回"跟随主角"、桥断开无副作用(正好是提示词阶段 1 的验收项) |
| 版本漂移 | 版本会咬人 | 实测记录写进实现记录 |

---

## 9. 未核实 / 待补

1. **Nexus 那条**:页面本身取到了;但**没有** GTA IV 版的第三方评测或源码仓库可交叉验证(只有 GTA V 前身)。它自曝 AI 创建、只有 3 个 Endorsements,证据链到此为止。
2. **2D 宿主的 passthrough 先例:判定为"空白"(已系统检索,不是没找)**。
   第二路调研把 guide 02/09 的 4 个参考项目、universal-modder 知识库里所有 `route=passthrough` 条目都过了一遍——
   **宿主全是 3D**。库内的 2D 游戏(Terraria / BTD6 / AoE2 / Darkest Dungeon)全是 `data` / `loader-api` / `native-hook`,
   **没有一个是 passthrough 宿主**。2D 只在 mashup-mods 的 Pattern 5 里作为**客机**被提过("…top-down 2D…")。
   检索 "Minecraft in Terraria" / "2D game passthrough" / "Hollow Knight passthrough" 多组零命中。
   → **结论:没有 2D 先例可抄,第一条路要自己趟,风险和当年 3D 首例同级。**
   同时有一条**形态对得上**的旁证(已核实源码):universal-modder 的 Terraria `reference/AgentBridge.cs`(**2D / XNA-FNA**)——
   文件头原文 "**a JSON-lines socket that let an outside agent read menus, click and play**",
   实现是 `new TcpListener(IPAddress.Loopback, Port)` + `ReadLine()` 逐行请求/应答,端口来自环境变量 `AGENT_BRIDGE_PORT`(例 `7878`)。
   **它是"外部程序驱动 2D 游戏",不是"两个游戏互传玩法",但通道形态和提示词阶段 1 的 `bridge.py` 一模一样** —— 阶段 1 最有力的可行性旁证。
3. **共享 GPU 纹理在 Unity 5.4 上的可行性**:本次只核实了 API 存在(`Graphics.CopyTexture` 等),没验证 DXGI 共享句柄导入到 Unity 5.4 的实际路径。**但按 §5 的结论,这条路我们本来就不需要。**
   (`Graphics.CopyTexture` 本身是 Unity 5.4+ 才有的,存在但只解决"同设备纹理拷贝"。)
4. **已排除的疑点(记一笔,省得下一个 AI 再查)**:
   - ~~"Unity 5.4 随包的 `System.Core` 里到底有没有 `MemoryMappedFiles`"~~ → **本次反编译实测:没有**(§4.2,三程序集 `MemoryMapped*` 命中 0)。Mono 源码树里确实有该类的 `.cs`,但**不等于随包 profile 里有**。
   - ~~"`.NET 2.0 Subset` 会不会把 socket 剔掉"~~ → **本次实测:**ICEY 发布的 `System.dll` 里 `TcpListener`/`TcpClient`/`Socket`/`NetworkStream` **全在**,且插件编译与运行用的是同一份(§4.5)。**TCP 可用。**
5. **交接文档里"405 张实体贴图"这条现在对不上**(顺带查出来的,建议记一笔):
   交接文档写 `_moddev\_work\mc_assets\` 与 `BepInEx\config\player_skin\_blocky\` 里"来自 1.18.2 jar 的 405 张实体贴图"。
   实测:`mc_assets\` **9 个文件**(alex/steve 皮肤 + 5 把剑 + critical_hit/flash),`player_skin\_blocky\` **6 个文件**,
   `_work\mcskin\` 只有 1 张 `German.png`;`_moddev` 全盘 **png 总数 208**(绝大多数是 `_work\player_atlas*` 的图集导出),
   且**没有任何目录的 png 数量 > 30**,`_work` 下也没有 zip/jar 容器。
   → **那 405 张实体贴图目前不在工作区**(可能当时是临时解出来看过、没留)。
   **阶段 2 要用"多实体多材质"之前,得先从 `D:\Games\MC\.minecraft\versions\1.18.2\1.18.2.jar` 重新解一次。**

---

## 10. 引用

- 定义与规范:[trevaintdead/ai-game-modding-guides](https://github.com/trevaintdead/ai-game-modding-guides) · [guide 02 Passthrough mods](https://github.com/trevaintdead/ai-game-modding-guides/blob/main/guides/02-passthrough-mods.md) · [guide 08 加载器](https://github.com/trevaintdead/ai-game-modding-guides/blob/main/guides/08-mod-loaders-and-script-extenders.md)
- 架构范本:[chasmlol/SkyCraft](https://github.com/chasmlol/SkyCraft)(README + `docs/DESIGN.md`)
- 五种模式与实战笔记:[rehan-remade/universal-modder](https://github.com/rehan-remade/universal-modder)(`skills/mashup-mods`、`examples/minecraft-gta5-passthrough`、`knowledge/`)
- Forge 分叉版:[VortexisTV/wither-storm-gta5-passthrough](https://github.com/VortexisTV/wither-storm-gta5-passthrough)
- GTA IV 版:[Minecraft PassthroughIV(Nexus gta4/1498,Wayback 快照)](http://web.archive.org/web/20261004000635/https://www.nexusmods.com/gta4/mods/1498)
- 博客(主人的示例):[amiibodoctor — What is Passthrough Modding?](https://amiibodoctor.com/2026/10/03/what-is-passthrough-modding/)
- 前作:[`_moddev\PassthroughModding.md`](PassthroughModding.md) · 任务书:见 `逆向文档.md` 末尾附录
