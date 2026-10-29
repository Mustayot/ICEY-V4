// ICEY 作弊面板 · 解锁「官方调试面板」—— partial class 第十二个文件。
//
// ── 背景(全部有 IL / 资源证据,见 _moddev\GUI安装器-设计记录.md) ────────────────
// 游戏自带一套开发商内部的调试面板:
//   * UIDebug      —— 挂在一个叫 "Debug" 的 GameObject 上,在 UI 层级里(父级 "UI"),
//                     下面有 ".Container_debug" 当按钮容器。**零售版正常加载**。
//   * DebugGUI     —— 独立预制体(Resources 路径 "Prefab/Core/DebugGUI"),
//                     root 上挂着 8 个面板组件:
//                       GameSettingDebugGui      ← Cheat On/Off、$+100、难度、全关卡、血宫、音量
//                       PlayerAttributeDebugGUI  ← Full HP / Kill All Enemies / Kill yourself / HP-20
//                       SaveDebugGUI             ← AutoSave / AutoLoad / Save / Load / Delete
//                       GameInfoDebugGUI         ← FPS / PhysX FPS / 关卡时间 / 场景名 / AI 状态
//                       EnemyGeneratorDebugGUI   ← 刷怪台(选怪/生成/测试/孤立/多次/移除)
//                       CameraFVDebugGUI / CameraFilterDebugGUI / KeyRebindDebugGUI
//                     (另有 PlayerEnemyHealth、FrozenTimeTest)
//
// **零售版被两处 Debug.isDebugBuild() 卡掉**:
//   1) GameSceneInit.Start():  isDebugBuild 为假 → 根本不实例化 DebugGUI 预制体
//      IL_003a call Debug::get_isDebugBuild  /  IL_003f brfalse IL_005c
//      IL_0044 ldstr "Prefab/Core/DebugGUI"
//   2) UIKeyInput.UpdateInput(): isDebugBuild 为假 → 按键分支直接跳过
//
// 所以我们**不碰那两处**(也就完全不动游戏代码),只做两件事:
//   A. 自己 Resources.Load + Instantiate 那个预制体 → 8 个面板的 Start() 跑起来,
//      它们会自动 UIDebug.Register 到现成的按钮池里
//   B. 按 F2 调 UIDebug.Open() / Close()
//
// ── 为什么全程反射 ────────────────────────────────────────────────────────
// build.ps1 的静态自检**禁止游戏类型出现在 .method / .field / .class / .custom 声明里**
// (出现 Assembly-CSharp 会被判为"元数据混入游戏类型")。UIDebug 是 Assembly-CSharp 里的类型,
// 所以它**不能当字段类型、不能当参数类型**,只能用 Type + 反射拿。
// 场景查询用 Resources.FindObjectsOfTypeAll(Type) —— 比抓 static Instance 稳(不用管泛型基类)。
using System;
using System.Reflection;
using UnityEngine;

namespace IceyCheatPanel
{
    public partial class CheatPanelPlugin
    {
        private GameObject _dbgPanelRoot;      // 实例化出来的 DebugGUI 预制体(GameObject 是 UnityEngine,合法)
        private Type _tUIDebug;
        private bool _dbgOpen;
        private string _dbgStatus = "未激活";

        // ---------------- 热键入口:Update() 里调 ----------------
        internal void ToggleOfficialDebug()
        {
            try
            {
                if (_dbgPanelRoot == null)
                {
                    if (!CreateOfficialDebugPanels()) return;
                }

                object ud = FindUIDebug();
                if (ud == null)
                {
                    _dbgStatus = "没找到 UIDebug 实例";
                    Logger.LogWarning("[dbg] " + _dbgStatus);
                    return;
                }

                string m = _dbgOpen ? "Close" : "Open";
                MethodInfo mi = _tUIDebug.GetMethod(m, BindingFlags.Public | BindingFlags.Instance);
                if (mi == null)
                {
                    _dbgStatus = "UIDebug 上没有 " + m + " 方法";
                    Logger.LogWarning("[dbg] " + _dbgStatus);
                    return;
                }
                mi.Invoke(ud, null);
                _dbgOpen = !_dbgOpen;
                _dbgStatus = _dbgOpen ? "已打开(按 F2 关)" : "已关闭";
                Logger.LogInfo("[dbg] 官方调试面板 " + m + " → " + _dbgStatus);
            }
            catch (Exception e)
            {
                _dbgStatus = "出错: " + e.Message;
                Logger.LogError("[dbg] ToggleOfficialDebug 失败: " + e);
            }
        }

        // ---------------- A. 实例化官方预制体 ----------------
        private bool CreateOfficialDebugPanels()
        {
            try
            {
                // Resources 路径来自 GameSceneInit 的 ldstr,照抄即可
                UnityEngine.Object prefab = Resources.Load("Prefab/Core/DebugGUI", typeof(GameObject));
                if (prefab == null)
                {
                    _dbgStatus = "Resources 里没有 Prefab/Core/DebugGUI";
                    Logger.LogWarning("[dbg] " + _dbgStatus);
                    return false;
                }

                _dbgPanelRoot = (GameObject)UnityEngine.Object.Instantiate(prefab);
                _dbgPanelRoot.name = "IceyPanel_DebugGUI";      // 改个名,免得和游戏自己的混淆
                UnityEngine.Object.DontDestroyOnLoad(_dbgPanelRoot);   // 和 GameSceneInit 一样
                _dbgPanelRoot.SetActive(true);

                // 把挂在 root 上的面板组件名字打出来(能自证:装上了哪些面板)
                string names = "";
                int n = 0;
                foreach (Component c in _dbgPanelRoot.GetComponents(typeof(Component)))
                {
                    if (c == null) continue;
                    string tn = c.GetType().Name;
                    if (tn == "Transform") continue;
                    names += (n > 0 ? ", " : "") + tn;
                    n++;
                }
                _dbgStatus = "已实例化,面板 " + n + " 个";
                Logger.LogInfo("[dbg] 官方 DebugGUI 预制体已实例化 —— " + n + " 个组件: " + names);
                return true;
            }
            catch (Exception e)
            {
                _dbgStatus = "实例化失败: " + e.Message;
                Logger.LogError("[dbg] 实例化 DebugGUI 失败: " + e);
                return false;
            }
        }

        // ---------------- B. 找 UIDebug 的运行时实例 ----------------
        private object FindUIDebug()
        {
            if (_tUIDebug == null)
            {
                // 只在游戏自己的程序集里找,别全扫(本项目踩过"扫所有程序集把主线程卡死"的坑)
                Assembly asm = typeof(R).Assembly;          // R 在 Assembly-CSharp 里
                _tUIDebug = asm.GetType("UIDebug", false);
                if (_tUIDebug == null)
                {
                    Logger.LogWarning("[dbg] Assembly-CSharp 里没有 UIDebug 类型");
                    return null;
                }
                Logger.LogInfo("[dbg] 找到类型 UIDebug: " + _tUIDebug.FullName);
            }

            UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(_tUIDebug);
            if (all == null || all.Length == 0) return null;
            return all[0];
        }

        internal string DebugPanelStatusText
        {
            get { return _dbgStatus; }
        }
    }
}
