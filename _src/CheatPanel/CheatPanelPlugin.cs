// ICEY Cheat Panel - core v0.5: state, cheat implementations, glass, persistence.
// UI lives in CheatPanelPlugin.Ui.cs (partial class).
// API facts verified against ildasm dumps; see _work\设计决策.md.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace IceyCheatPanel
{
    // Jump height is NOT a field: PlayerJumpAbility.Jump() hardcodes y=20 (first) / 16 (second).
    // The only clean way is a Harmony postfix that rescales the resulting vertical speed.
    //
    // IMPORTANT: this class must NOT carry [HarmonyPatch(typeof(<gameType>))] and must not name
    // any game type in its signature. BepInEx scans plugin assemblies at chainloader time, when
    // Assembly-CSharp is not loaded yet; a game-type token that the loader resolves eagerly makes
    // the whole plugin get dropped (symptom: "0 plugins to load"). Everything is resolved lazily
    // by name at runtime instead.
    internal static class JumpHeightPatch
    {
        internal static float Mult = 1f;
        internal static void Postfix()
        {
            try
            {
                if (Mathf.Abs(Mult - 1f) < 0.001f) return;
                var p = R.Player;
                if (p == null) return;
                var tc = p.TimeController;
                if (tc == null) return;
                Vector2 v = tc.GetCurrentSpeed();
                if (v.y > 0f) tc.SetSpeed(new Vector2(v.x, v.y * Mult));
            }
            catch { }
        }
    }

    // 防反(闪击反击)判定窗口。
    // 机制:PlayerAnimEventListener.FlashStart() 里写死
    //   _flashEndCount = WorldTime.SecondToFrame(8.3333336e-02f)   // 0.0833 秒, 约 5 帧 @60fps
    // 之后 UpdateFlashEnd() 每帧 _flashEndCount -= 1, 减到 0 就把 pAttr.flashFlag 关掉。
    // 攻击命中瞬间 PlayerHurtAbility.PlayeAllHurt 先看 flashFlag: 若为真且存档已解锁
    // Enhancement.FlashAttack, 就走闪击反击分支并吃掉这次伤害 —— 这就是"被打前一刻闪避"的防反。
    // 所以拉宽 _flashEndCount 等于拉宽判定窗口。常量被编进 IL 改不了, 但该字段是实例字段,
    // 用 Harmony postfix 在 FlashStart 之后把它顶大即可。
    //
    // 与 JumpHeightPatch 同样的约束: 不得带 [HarmonyPatch(typeof(<gameType>))],
    // 签名里也不得出现游戏类型, 否则 BepInEx 链式加载期解析不到 Assembly-CSharp, 整个插件被丢弃。
    internal static class FlashWindowPatch
    {
        internal static float ExtraSeconds = 0f;   // 额外判定窗口(秒), 0 = 原版约 0.083 秒
        private static System.Reflection.FieldInfo _fld;

        // 当前世界帧率。WorldTime.Fps 每 0.5 秒采样一次, 面板水印用的也是它。
        // 取不到(启动瞬间/暂停)时退回 60, 保证帧↔秒换算不会除零。
        internal static float NowFps
        {
            get
            {
                try
                {
                    float f = WorldTime.Fps;
                    if (f > 1f) return f;
                }
                catch { }
                return 60f;
            }
        }

        internal static int SecondsToFrames(float seconds)
        {
            int n = (int)(seconds * NowFps);
            return n < 0 ? 0 : n;
        }

        internal static float FramesToSeconds(float frames)
        {
            float fps = NowFps;
            if (fps <= 1f) return 0f;
            return frames / fps;
        }

        internal static void Postfix(object __instance)
        {
            try
            {
                if (ExtraSeconds <= 0.0001f) return;
                if (__instance == null) return;
                if (_fld == null) _fld = AccessTools.Field(__instance.GetType(), "_flashEndCount");
                if (_fld == null) return;
                int extra = SecondsToFrames(ExtraSeconds);
                if (extra <= 0) return;
                object cur = _fld.GetValue(__instance);
                if (cur == null) return;
                _fld.SetValue(__instance, (int)cur + extra);
            }
            catch { }
        }
    }

    // BepInEx identifies a plugin purely by this attribute - without it the assembly is loaded
    // and scanned but yields "0 plugins to load". It was lost when the class was split across
    // two files, which is exactly when loading stopped working.
    [BepInPlugin("icey.cheatpanel", "ICEY Cheat Panel", "0.7.2")]
    public partial class CheatPanelPlugin : BaseUnityPlugin
    {
        // ================= geometry (design units x S) =================
        public const float S = 1.6f;
        public const float CardW = 250f;
        public const float HeadH = 34f;
        public const float RowH = 30f;
        public const float SelH = 56f;
        public const float InpH = 56f;
        public const float GrpH = 20f;
        public const float RadiusCard = 10f;
        public const int CardCount = 11;
        public const float Snapping = 10f;

        // ================= palette (light + translucent glass) =================
        public static readonly Color ColHeader = RGBA(255, 249, 252, 0.48f);
        public static readonly Color ColBody = RGBA(255, 247, 250, 0.38f);
        public static readonly Color ColHover = RGBA(255, 240, 246, 0.52f);
        public static readonly Color ColShadow = RGBA(150, 80, 110, 0.14f);
        public static readonly Color ColOverlay = RGBA(60, 30, 45, 0.22f);
        public static readonly Color ColSearch = RGBA(255, 249, 252, 0.60f);
        public static readonly Color ColTabsBg = RGBA(255, 246, 250, 0.52f);
        public static readonly Color ColTabHover = RGBA(255, 240, 246, 0.62f);
        public static readonly Color ColTabActive = RGBA(214, 51, 132, 0.14f);
        public static readonly Color ColText = RGBA(74, 44, 58, 1f);
        public static readonly Color ColTextDim = RGBA(122, 92, 106, 1f);
        public static readonly Color ColAccent = RGBA(214, 51, 132, 1f);
        public static readonly Color ColOutline = RGBA(60, 30, 45, 0.30f);
        public static readonly Color ColSwTrackOff = RGBA(150, 118, 132, 0.38f);
        public static readonly Color ColSwThumbOff = RGBA(255, 255, 255, 0.95f);
        public static readonly Color ColSwTrackOn = RGBA(214, 51, 132, 0.36f);
        public static readonly Color ColSlideTrack = RGBA(150, 118, 132, 0.20f);
        public static readonly Color ColModal = RGBA(255, 250, 252, 0.94f);
        public static readonly Color ColGroup = RGBA(122, 92, 106, 0.55f);

        public static Color RGBA(int r, int g, int b, float a)
        { return new Color32((byte)r, (byte)g, (byte)b, (byte)Mathf.RoundToInt(a * 255f)); }

        // ================= cheat state =================
        public bool _godMode, _infiniteEnergy, _oneHitKill, _infFlash, _infCombo, _comboHud;
        public bool _aiOff, _hud, _dpsOn, _boundsOn, _timeScaleOn, _pauseTime, _camFree, _glassOn;
        public bool _noEnemyAtk, _freezeEnemy, _passWall;
        public float _atkMult = 1f, _timeScale = 1f, _fov = 52f, _blurRadius = 150f;
        public float _moveMult = 1f, _jumpMult = 1f, _scaleMult = 1f, _gravMult = 1f;
        public float _flashSeconds = 0f;   // 防反判定窗口额外秒数(0 = 原版约 0.083 秒)
        public float _flashFrames = 0f;    // 同一窗口的帧数表示, 与 _flashSeconds 联动
        public float _enemyHpMult = 1f, _enemyAtkMult = 1f;
        public int _coinInput;
        public int _jumpIdx, _enemyIdx, _enhIdx, _filterIdx;

        private int _writtenAtk, _atkBaseline = 40;
        private float _moveBase = -1f, _moveWritten = -1f;
        private float _gravBase = -1f, _gravWritten = -1f;
        private float _scaleBase = -1f;
        private bool _ownsTimeScale, _ownsPause, _freezeApplied, _patched;
        private int _comboLock;
        private Vector2 _lastSafe;
        private float _safeTimer;
        private bool _haveSafe;

        private readonly Dictionary<int, int> _enemyHpBase = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _enemyHpWritten = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _enemyAtkBase = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _enemyAtkWritten = new Dictionary<int, int>();

        private long _hpSum;
        private float _dpsAccum, _dpsTimer, _dps;

        private Component _blur;
        private Type _activeFilter;
        private Component _filterComp;

        // ================= panel state =================
        public bool _open;
        public float _fade;
        public string _search = "";
        public readonly bool[] _cardOpen = new bool[CardCount];
        public readonly float[] _cardAnim = new float[CardCount];
        public readonly float[] _cardX = new float[CardCount];
        public readonly float[] _cardY = new float[CardCount];
        public int _dragCard = -1;
        public Vector2 _dragOffset;
        public bool _confirmOpen;
        public string _confirmMsg = "";
        public Action _confirmAction;
        public bool _tpArmed;
        public string _toast = "";
        public float _toastUntil;

        // ================= data =================
        public static readonly string[] JumpScenes = {
            "C1L1S1","C1L2S1","C1L3S1","C2L1S1","C2L2S1","C3L1S1",
            "C4L1S1","C4L2S1","C5L1S1","C6L1S1",
            "palace0","palace1","palace2","palace3","palace4","palace5","palace6","palace7"
        };
        public static readonly string[] EnhNames = {
            "attack","knockout","avatarAttack","airAttack","airCombo2","airAvatarAttack",
            "airCombo1","tripleAttack","combo2","upperChop","combo1","bladeStorm",
            "shadeAttack","charging","chase","hitGround","maxHP","maxEnergy","flashAttack","recover"
        };
        public string[] EnemyNames = new string[0];
        public Type[] FilterTypes = new Type[0];

        public class Mod
        {
            public string Name;
            public int Kind;                 // 0 toggle 1 slider 2 action 3 selector 4 input
            public string Group = "";
            public Func<bool> Get;
            public Action<bool> Set;
            public Func<float> GetF;
            public Action<float> SetF;
            public float Min, Max, Def;
            public string Fmt;
            public Action Run;
            public float Anim;
            public Func<int> GetIdx;
            public Action<int> SetIdx;
            public Func<int> GetI;
            public Action<int> SetI;
            public Func<string> GetS;         // kind 5: 只读状态文字
        }

        public readonly List<Mod>[] Mods = new List<Mod>[CardCount];
        public readonly string[] CatName = { "战斗", "玩家", "敌人", "资源与进度", "世界与画面", "存档与剧情", "配色", "文本", "换角色", "方块人", "水印界面" };

        public string ConfigPath { get { return Path.Combine(Paths.ConfigPath, "icey-cheatpanel.txt"); } }

        // ================= lifecycle =================
        public bool _uiDark;          // 界面主题:false = 最开始那套(默认),true = 深色渐变卡片风
        public bool _devMode;         // 开发者模式:显示实验/调试卡片(正常玩看不到)

        // 切到"正常模式"时,把只属于实验卡片的功能关掉 —— 否则卡片被藏了,主人就没地方关这些效果了。
        public void SetDevMode(bool v)
        {
            _devMode = v;
            if (!v)
            {
                if (_blockyOn) SetBlocky(false);
                if (_borrowOn) { try { RevertBorrowedSkeleton(); } catch { } }
                if (_mcAtkMode) SetMcAtkMode(false);
                Toast("开发者模式已关;实验功能(方块人/借骨架/MC 攻击)一并关闭");
            }
            else Toast("开发者模式已开(顶部「开发」按钮也可切)");
            Save();
        }

        private void Awake()
        {
            Logger.LogInfo("ICEY Cheat Panel awake (v0.5.2).");
            BuildModules();
            SetDefaultLayout();
            Load();                                   // may override the default card layout
            RefreshSkinPresets();                     // 纯文件 IO,不碰 Unity API(Awake 里禁止碰)
            if (_textOn)                              // 文本替换:只做文件 IO + 静态字段赋值(Awake 安全)
            {
                TermTranslationPatch.Enabled = true;
                ReloadTextOverrides();
            }
            // MC 攻击模式:只赋静态字段(Awake 安全);真正的 Harmony 安装等 Update 里懒装
            // 正常模式下不该有实验功能在跑(卡片被藏了就没法关)—— 启动时兜一次
            if (!_devMode && (_blockyOn || _borrowOn || _mcAtkMode))
            {
                _blockyOn = false;
                _borrowOn = false;
                _mcAtkMode = false;
                Logger.LogInfo("[ui] 正常模式:实验功能已复位(方块人/借骨架/MC 攻击);点顶部「开发」可再开");
            }
            McNoAtkEffectPatch.Enabled = _mcAtkMode || _mcNoSlash;
            McAtkIntervalPatch.Enabled = _mcAtkMode;
            McAtkIntervalPatch.Interval = _mcAtkInterval;
            for (int i = 0; i < CardCount; i++) if (_cardOpen[i]) _cardAnim[i] = 1f;
        }

        private void SetDefaultLayout()
        {
            for (int i = 0; i < CardCount; i++)
            {
                int col = i % 3, row = i / 3;
                // Cards must start BELOW the toolbar: capsule bar occupies y 16..64 and the
                // search box 74..125 (design units). Row pitch must exceed the collapsed card
                // height (HeadH = 34) or the two rows overlap each other.
                _cardX[i] = (20f + col * (CardW + 12f)) * S;
                _cardY[i] = (92f + row * 44f) * S;
                _cardOpen[i] = false;
                _cardAnim[i] = 0f;
            }
            _cardOpen[0] = true;
        }

        private Mod T(string g, string n, Func<bool> get, Action<bool> set)
        { return new Mod { Group = g, Name = n, Kind = 0, Get = get, Set = set }; }
        private Mod Sl(string g, string n, float min, float max, float def, string fmt, Func<float> get, Action<float> set)
        { return new Mod { Group = g, Name = n, Kind = 1, Min = min, Max = max, Def = def, Fmt = fmt, GetF = get, SetF = set }; }
        private Mod Ac(string g, string n, Action run)
        { return new Mod { Group = g, Name = n, Kind = 2, Run = run }; }
        private Mod Sel(string g, string n, Func<int> gi, Action<int> si, Action run)
        { return new Mod { Group = g, Name = n, Kind = 3, GetIdx = gi, SetIdx = si, Run = run }; }
        private Mod Inp(string g, string n, Func<int> gi, Action<int> si, Action run)
        { return new Mod { Group = g, Name = n, Kind = 4, GetI = gi, SetI = si, Run = run }; }
        private Mod Lb(string g, string n, Func<string> val)
        { return new Mod { Group = g, Name = n, Kind = 5, GetS = val }; }

        private void BuildModules()
        {
            for (int i = 0; i < CardCount; i++) Mods[i] = new List<Mod>();

            // ---- 0 战斗 ----
            Mods[0].Add(T("", "无敌", () => _godMode, v => { _godMode = v; Save(); }));
            Mods[0].Add(T("", "无限能量", () => _infiniteEnergy, v => { _infiniteEnergy = v; Save(); }));
            Mods[0].Add(T("", "一击必杀", () => _oneHitKill, v => { _oneHitKill = v; Save(); }));
            Mods[0].Add(T("", "无限闪现", () => _infFlash, v => { _infFlash = v; Save(); }));
            Mods[0].Add(T("", "无限连击", () => _infCombo, v => { _infCombo = v; Save(); }));
            Mods[0].Add(T("", "连击数显示", () => _comboHud, v => { _comboHud = v; Save(); }));
            Mods[0].Add(Sl("倍率", "攻击力倍率", 1f, 100f, 1f, "F1", () => _atkMult, v => { _atkMult = v; Save(); }));
            // 防反窗口两个滑块: 拖哪个另一个按当前帧率同步换算
            Mods[0].Add(Sl("判定", "防反窗口(秒)", 0f, 10f, 0f, "F2", () => _flashSeconds, v =>
            {
                _flashSeconds = v;
                _flashFrames = FlashWindowPatch.SecondsToFrames(v);
                FlashWindowPatch.ExtraSeconds = v;
                Save();
            }));
            Mods[0].Add(Sl("判定", "防反窗口(帧)", 0f, 6000f, 0f, "F0", () => _flashFrames, v =>
            {
                _flashFrames = Mathf.Round(v);
                _flashSeconds = FlashWindowPatch.FramesToSeconds(_flashFrames);
                FlashWindowPatch.ExtraSeconds = _flashSeconds;
                Save();
            }));
            Mods[0].Add(Ac("操作", "回满血 / 复活", () => { try { PlayerAction.Reborn(); } catch (Exception e) { Warn("Reborn", e); } }));
            Mods[0].Add(Ac("操作", "清场(杀死全部敌人)", KillAll));

            // ---- 1 玩家 ----
            Mods[1].Add(Sl("", "移动速度倍率", 0.2f, 5f, 1f, "F2", () => _moveMult, v => { _moveMult = v; Save(); }));
            Mods[1].Add(Sl("", "跳跃高度倍率", 0.2f, 5f, 1f, "F2", () => _jumpMult, v => { _jumpMult = v; JumpHeightPatch.Mult = v; Save(); }));
            Mods[1].Add(Sl("", "角色缩放", 0.3f, 3f, 1f, "F2", () => _scaleMult, v => { _scaleMult = v; Save(); }));
            Mods[1].Add(Sl("", "重力倍率", 0f, 5f, 1f, "F2", () => _gravMult, v => { _gravMult = v; Save(); }));
            Mods[1].Add(T("机动", "穿墙(无视碰撞)", () => _passWall, v => { _passWall = v; Save(); }));
            Mods[1].Add(Ac("机动", "回到最后安全位置", GotoLastSafe));
            Mods[1].Add(Ac("机动", "瞬移到最近敌人", GotoNearestEnemy));
            Mods[1].Add(Ac("机动", "瞬移到鼠标(关面板后点目标)", () =>
            { _tpArmed = true; Toast("传送模式:关面板后点击目标位置"); Toggle(); }));
            Mods[1].Add(T("相机", "相机自由视角", () => _camFree, v => { _camFree = v; Save(); }));

            // ---- 2 敌人 ----
            Mods[2].Add(Sl("", "敌人血量倍率", 0.1f, 20f, 1f, "F1", () => _enemyHpMult, v => { _enemyHpMult = v; Save(); }));
            Mods[2].Add(Sl("", "敌人攻击力倍率", 0f, 20f, 1f, "F1", () => _enemyAtkMult, v => { _enemyAtkMult = v; Save(); }));
            Mods[2].Add(T("", "敌人不攻击(攻击力归零)", () => _noEnemyAtk, v => { _noEnemyAtk = v; Save(); }));
            Mods[2].Add(T("", "永久冻结敌人", () => _freezeEnemy, v => { _freezeEnemy = v; ApplyFreeze(); Save(); }));
            Mods[2].Add(T("", "敌人 AI 全关", () => _aiOff, v => { _aiOff = v; ApplyAiNow(); Save(); }));
            Mods[2].Add(Sel("生成", "选择敌人类型", () => _enemyIdx, i => _enemyIdx = i, SpawnEnemy));

            // ---- 3 资源与进度 ----
            Mods[3].Add(Ac("金币", "金币 +100", () => AddCoin(100)));
            Mods[3].Add(Ac("金币", "金币 +1000", () => AddCoin(1000)));
            Mods[3].Add(Ac("金币", "金币 +10000", () => AddCoin(10000)));
            Mods[3].Add(Inp("金币", "金币设为指定值", () => _coinInput, v => { _coinInput = v; Save(); }, SetCoinExact));
            Mods[3].Add(Ac("强化", "强化全部拉满", () => SetAllEnhancement(3)));
            Mods[3].Add(Ac("强化", "重置全部强化", () => SetAllEnhancement(0)));
            Mods[3].Add(Sel("强化", "选择强化项", () => _enhIdx, i => _enhIdx = i, () => SetOneEnhancement(3)));
            Mods[3].Add(Ac("强化", "所选强化项归零", () => SetOneEnhancement(0)));
            Mods[3].Add(Ac("强化", "刷新属性(让强化生效)", RefreshAttributes));
            Mods[3].Add(Ac("进度", "循环切换难度", CycleDifficulty));
            Mods[3].Add(Ac("进度", "一键解锁全部成就", () => Confirm("解锁全部 Steam 成就?", DoAwardAll)));
            Mods[3].Add(Sel("进度", "跳关", () => _jumpIdx, i => _jumpIdx = i,
                () => Confirm("跳到关卡 " + JumpScenes[_jumpIdx] + "?", DoJump)));

            // ---- 4 世界与画面 ----
            Mods[4].Add(T("信息", "HUD 信息叠加", () => _hud, v => { _hud = v; Save(); }));
            Mods[4].Add(T("信息", "DPS 统计", () => _dpsOn, v => { _dpsOn = v; Save(); }));
            Mods[4].Add(T("信息", "碰撞盒显示", () => _boundsOn, v => { _boundsOn = v; Save(); }));
            // ---- 10 水印 / 界面(从「世界与画面」拆出来:那张卡 19 行太高,把"视野大小"挤到屏幕外了)----
            Mods[10].Add(T("界面", "深色渐变主题(关=最初主题)", () => _uiDark, v => { _uiDark = v; Save(); }));
            Mods[10].Add(T("界面", "开发者模式(实验功能)", () => _devMode, v => SetDevMode(v)));
            Mods[10].Add(T("水印", "状态水印(右上角)", () => _wmOn, v => { _wmOn = v; Save(); }));
            Mods[10].Add(T("水印", "水印用英文 English", () => _wmEn, v => { _wmEn = v; Save(); }));
            Mods[10].Add(T("水印", "水印也列未开启项", () => _wmAll, v => { _wmAll = v; Save(); }));
            Mods[10].Add(Sl("水印", "水印大小", 0.6f, 2f, 1f, "F2", () => _wmScale, v => { _wmScale = v; _stWm = null; Save(); }));
            Mods[10].Add(Lb("说明", "提示", () => "「视野大小」在「世界与画面」卡片的相机分组"));
            Mods[4].Add(T("时间", "全局时间缩放", () => _timeScaleOn, v => { _timeScaleOn = v; Save(); }));
            Mods[4].Add(Sl("时间", "时间倍率", 0.1f, 3f, 1f, "F2", () => _timeScale, v => { _timeScale = v; Save(); }));
            Mods[4].Add(T("时间", "暂停游戏时间", () => _pauseTime, v => { _pauseTime = v; Save(); }));
            Mods[4].Add(Ac("时间", "冻结 30 帧", () =>
            { try { WorldTime.Instance.TimeFrozenByFixedFrame(30, WorldTime.FrozenArgs.FrozenType.All, true); } catch (Exception e) { Warn("Freeze", e); } }));
            Mods[4].Add(Sl("相机", "视野大小", 20f, 90f, 52f, "F0", () => _fov, v => { _fov = v; Save(); }));
            Mods[4].Add(T("画面", "毛玻璃背景模糊", () => _glassOn, v => { _glassOn = v; ApplyGlass(); Save(); }));
            Mods[4].Add(Sl("画面", "模糊强度", 0f, 500f, 150f, "F0", () => _blurRadius, v => { _blurRadius = v; PushBlurRadius(); Save(); }));
            Mods[4].Add(Sel("画面", "滤镜切换", () => _filterIdx, i => { _filterIdx = i; ApplyFilter(); }, ApplyFilter));
            Mods[4].Add(Ac("画面", "画面去色", () => ApplyNamedFilter("CameraFilterPack_Color_GrayScale")));
            Mods[4].Add(Ac("画面", "满屏特效(老电视)", () => ApplyNamedFilter("CameraFilterPack_FX_8bits")));

            // ---- 5 存档与剧情 ----
            Mods[5].Add(Ac("存档", "手动存档", () => Confirm("立即保存当前进度?", DoSaveNow)));
            Mods[5].Add(Ac("存档", "手动读档(自动备份)", () => Confirm("读取存档?\n会先把当前存档目录备份一份。", DoLoadNow)));
            Mods[5].Add(Ac("剧情", "直接进入血宫", () => Confirm("进入血宫模式?", DoBloodPalace)));
            Mods[5].Add(Ac("剧情", "退出血宫", () => DoExitBloodPalace()));

            // ---- 6 配色(任务 A②:运行时换皮,不碰 assets) ----
            Mods[6].Add(T("", "配色替换(开 / 关)", () => _skinOn, v =>
            {
                _skinOn = v;
                if (v) { RefreshSkinPresets(); _skinScanned = false; _skinNextScan = 0f; _skinDirty = true; Toast("配色已开启"); }
                else { _skinDirty = false; PushSkinTextures(true); FreeSkinTextures(); Toast("配色已关闭,已还原原色"); }
                Save();
            }));
            Mods[6].Add(Sel("", "配色预设", () => _skinIdx, i => SetSkinPreset(i), ApplySkinNow));
            Mods[6].Add(Ac("", "重新扫描材质", RescanSkin));
            Mods[6].Add(Ac("", "重新读取 PNG", ReloadSkinPngs));
            Mods[6].Add(T("", "PNG 上下翻转(必须开)", () => _skinFlip, v => SetSkinFlip(v)));
            Mods[6].Add(Ac("", "刷新预设列表", () => { RefreshSkinPresets(); Toast("预设 " + (SkinPresets.Length - 2) + " 套 + 原色 + 隐形"); }));
            Mods[6].Add(Ac("", "导出材质诊断", ExportSkinDiagnostics));
            Mods[6].Add(Lb("", "状态", () => _skinStatus));
            Mods[6].Add(Lb("", "PNG 位置", () => "player_skin\\<预设>\\<贴图名>.png"));

            // ---- 动态配色(颜色随时间流动;和上面的"换贴图"是叠加关系)----
            Mods[6].Add(T("动态配色", "动态配色(随时间变幻)", () => _fxOn, v => SetSkinFx(v)));
            Mods[6].Add(T("动态配色", "模式:彩虹循环(关=呼吸)", () => _fxRainbow, v => { _fxRainbow = v; Save(); }));
            Mods[6].Add(Sl("动态配色", "变幻速度", 0.05f, 2f, 0.35f, "F2", () => _fxSpeed, v => { _fxSpeed = v; Save(); }));
            Mods[6].Add(Sl("动态配色", "饱和度", 0f, 1f, 0.75f, "F2", () => _fxSat, v => { _fxSat = v; Save(); }));
            Mods[6].Add(Sl("动态配色", "最暗亮度", 0.2f, 1f, 0.45f, "F2", () => _fxMin, v => { _fxMin = v; Save(); }));
            Mods[6].Add(Lb("动态配色", "状态", () => _fxStatus));

            // ---- 9 方块人 / 角色渲染(独立卡片;藏原角色·骨点·方块人都在这里)----
            Mods[9].Add(T("方块人", "方块人(换建模)", () => _blockyOn, v => SetBlocky(v)));
            Mods[9].Add(Sel("方块人", "原角色显示", () => _origOverlay, i => SetOrigOverlay(i), () => SetOrigOverlay(_origOverlay)));
            Mods[9].Add(Sl("方块人", "尺寸倍率", 0.4f, 2.5f, 1f, "F2", () => _blockyScale, v => { _blockyScale = v; if (_blockyOn) { DestroyBlocky(); BuildBlocky(); } Save(); }));
            Mods[9].Add(Sl("方块人", "3/4 偏航角(度)", 0f, 45f, 22f, "F0", () => _blockyYaw, v => { _blockyYaw = v; Save(); }));
            Mods[9].Add(Sl("方块人", "挥动幅度", 0.2f, 2f, 1f, "F2", () => _blockySwing, v => { _blockySwing = v; Save(); }));
            Mods[9].Add(Sel("方块人", "手持物品", () => _blockySword, i => SetSword(i), () => SetSword(_blockySword)));
            Mods[9].Add(Sl("方块人", "剑角度(度)", -120f, 40f, -45f, "F0", () => _swordRot, v => { _swordRot = v; if (_blockyOn) { DestroyBlocky(); BuildBlocky(); } Save(); }));
            Mods[9].Add(Sl("方块人", "剑俯仰", -80f, 80f, -16f, "F0", () => _swordRotX, v => { _swordRotX = v; if (_blockyOn) { DestroyBlocky(); BuildBlocky(); } Save(); }));
            Mods[9].Add(Sl("方块人", "剑偏航", -80f, 80f, 24f, "F0", () => _swordRotY, v => { _swordRotY = v; if (_blockyOn) { DestroyBlocky(); BuildBlocky(); } Save(); }));
            Mods[9].Add(Sl("方块人", "剑前后偏移(px)", -6f, 12f, 3f, "F1", () => _swordZPx, v => { _swordZPx = v; if (_blockyOn) { DestroyBlocky(); BuildBlocky(); } Save(); }));
            Mods[9].Add(Sl("方块人", "剑长倍率", 0.2f, 1.2f, 0.62f, "F2", () => _swordLen, v => { _swordLen = v; if (_blockyOn) { DestroyBlocky(); BuildBlocky(); } Save(); }));
            Mods[9].Add(T("MC 攻击", "MC 攻击方式(战斗手感)", () => _mcAtkMode, v => SetMcAtkMode(v)));
            Mods[9].Add(T("MC 攻击", "去剑光 / 攻击特效", () => _mcNoSlash, v => SetNoSlash(v)));
            Mods[9].Add(Sl("MC 攻击", "攻击间隔(秒)", 0.2f, 2f, 0.625f, "F3", () => _mcAtkInterval, v => { _mcAtkInterval = v; McAtkIntervalPatch.Interval = v; Save(); }));
            Mods[9].Add(T("方块人", "藏掉剑光特效", () => _blockyHideVfx, v => { _blockyHideVfx = v; if (!v) RestoreVfx(); else if (_blockyOn) HideVfx(); Save(); }));
            Mods[9].Add(Ac("方块人", "重载方块人皮肤", ReloadBlockySkin));
            Mods[9].Add(Ac("方块人", "重建方块人", () => { if (_blockyOn) { DestroyBlocky(); BuildBlocky(); Toast(_blockyStatus); } }));
            Mods[9].Add(Lb("方块人", "方块人状态", () => _blockyStatus));
            Mods[9].Add(Lb("方块人", "皮肤位置", () => "player_skin\\_blocky\\skin.png"));
            Mods[9].Add(T("调试", "骨点叠加(调试)", () => _boneHud, v => { _boneHud = v; Save(); }));
            Mods[9].Add(Sl("调试", "藏特效半径(米)", 5f, 40f, 20f, "F0", () => _vfxRadius, v => { _vfxRadius = v; Save(); }));
            Mods[9].Add(Ac("调试", "列出场景渲染器(日志)", DumpSceneRenderers));
            Mods[9].Add(Ac("调试", "重新扫描骨架", () => { ScanSkeletons(); Toast(_charStatus); }));
            Mods[9].Add(Ac("调试", "导出骨架信息", LogSkeletonInfo));
            Mods[9].Add(Ac("调试", "强制恢复原角色(兜底)", ForceRestoreOriginal));
            Mods[9].Add(Lb("调试", "角色状态", () => _charStatus));

            // ---- 7 文本(任务 B:运行时替换,不碰 assets)----
            Mods[7].Add(T("", "文本替换(开 / 关)", () => _textOn, v => SetTextOn(v)));
            Mods[7].Add(Ac("", "重新读取替换表", () => { ReloadTextOverrides(); RefreshLocalizedText(); }));
            Mods[7].Add(Ac("", "导出全部文本", DumpAllTerms));
            Mods[7].Add(Ac("", "生成替换表模板", MakeOverrideTemplate));
            Mods[7].Add(Ac("", "刷新界面文本", RefreshLocalizedText));
            Mods[7].Add(Ac("", "打开替换表(记事本)", () => OpenTextFile("override")));
            Mods[7].Add(Ac("", "打开全量术语(记事本)", () => OpenTextFile("all")));
            Mods[7].Add(Ac("", "打开配置文件夹", OpenConfigFolder));
            Mods[7].Add(Lb("", "状态", () => _textStatus));
            Mods[7].Add(Lb("", "替换表位置", () => "config\\text_override.txt"));

            // ---- 8 换角色(任务 C:运行时"借"别人的骨架)----
            Mods[8].Add(Ac("", "扫描可借骨架", ScanBorrowSources));
            Mods[8].Add(Sel("", "选择骨架来源", () => _borrowIdx, i => SetBorrowIdx(i), ApplyBorrowedSkeleton));
            Mods[8].Add(Ac("", "套用选中的骨架", ApplyBorrowedSkeleton));
            Mods[8].Add(Ac("", "还原原骨架", RevertBorrowedSkeleton));
            Mods[8].Add(Ac("", "列出动画重叠", () => { if (_borrowSources.Count > 0) LogAnimOverlap(_borrowSources[Mathf.Clamp(_borrowIdx, 0, _borrowSources.Count - 1)]); else ScanBorrowSources(); }));
            Mods[8].Add(Lb("", "状态", () => _borrowStatus));
            Mods[8].Add(Lb("", "说明", () => "换的是运行时骨架数据,不碰游戏文件"));
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) Toggle();                    // 开/关本面板
            if (Input.GetKeyDown(KeyCode.F2)) ToggleOfficialDebug();       // 解锁并开/关「官方调试面板」

            float dt = Time.unscaledDeltaTime / 0.2f;
            _fade = Mathf.Clamp01(_fade + (_open ? dt : -dt));
            for (int i = 0; i < CardCount; i++)
                _cardAnim[i] = Mathf.Clamp01(_cardAnim[i] +
                    (_open && _cardOpen[i] ? Time.unscaledDeltaTime / 0.28f : -Time.unscaledDeltaTime / 0.28f));
            for (int c = 0; c < CardCount; c++)
                for (int m = 0; m < Mods[c].Count; m++)
                {
                    Mod mm = Mods[c][m];
                    if (mm.Kind != 0) continue;
                    mm.Anim = Mathf.MoveTowards(mm.Anim, mm.Get() ? 1f : 0f, Time.unscaledDeltaTime / 0.35f);
                }

            UpdateSkin();          // 面板开着也要能换色(所以放在 _open 提前返回之前)
            UpdateCharacter();     // 角色替换地基:定时扫骨架 + 维持"隐藏原角色"的状态
            UpdateBlocky();        // 方块人:构建/同步位置
            UpdateText();          // 文本替换:懒安装补丁 + 启动后自动导出一次
            UpdateSkinFx();        // 动态配色:颜色随时间流动

            if (_open) return;

            ApplyGameplayCheats();
            UpdateDps();
            UpdateTeleport();
            UpdateSafePoint();
            ApplyFilter();
        }

        private void UpdateSafePoint()
        {
            _safeTimer += Time.unscaledDeltaTime;
            if (_safeTimer < 0.5f) return;
            _safeTimer = 0f;
            try
            {
                var p = R.Player;
                if (p == null || p.Attribute == null) return;
                if (p.Attribute.isOnGround && !p.Attribute.isDead)
                { _lastSafe = p.transform.position; _haveSafe = true; }
            }
            catch { }
        }

        private void UpdateTeleport()
        {
            if (!_tpArmed || !Input.GetMouseButtonDown(0)) return;
            try
            {
                Camera cam = Camera.main;
                if (cam != null && R.Player != null)
                {
                    Vector3 w = cam.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0f));
                    R.Player.SetPosition(new Vector2(w.x, w.y));
                    Toast("已传送");
                }
            }
            catch (Exception e) { Warn("Teleport", e); }
            _tpArmed = false;
        }

        private void UpdateDps()
        {
            if (!_dpsOn || R.Enemy == null) return;
            try
            {
                var list = R.Enemy.EnemyAttributes;
                if (list == null) return;
                long sum = 0;
                for (int i = 0; i < list.Count; i++) if (list[i] != null) sum += list[i].currentHp;
                if (_hpSum > sum) _dpsAccum += (_hpSum - sum);
                _hpSum = sum;
                _dpsTimer += Time.unscaledDeltaTime;
                if (_dpsTimer >= 1f) { _dps = _dpsAccum / _dpsTimer; _dpsAccum = 0f; _dpsTimer = 0f; }
            }
            catch { }
        }

        // ================= cheat application =================
        private void ApplyGameplayCheats()
        {
            try
            {
                if (_aiOff && R.SceneData != null) R.SceneData.CanAIRun = false;

                var player = R.Player;
                if (player == null) return;
                var attr = player.Attribute;
                if (attr == null) return;

                if (_godMode)
                {
                    var ab = player.Abilities;
                    if (ab != null && ab.hurt != null && !ab.hurt.Invincible) ab.hurt.Invincible = true;
                }
                if (_infiniteEnergy && attr.currentEnergy != attr.maxEnergy) attr.currentEnergy = attr.maxEnergy;

                if (_infFlash)
                {
                    if (attr.currentFlashTimes != attr.flashTimes) attr.currentFlashTimes = attr.flashTimes;
                    if (attr.FlashCd != 0) attr.FlashCd = 0;
                }

                // attack: baseline capture (game may rewrite baseAtk on level/section change)
                if (_atkMult > 1.0001f)
                {
                    if (attr.baseAtk != _writtenAtk) _atkBaseline = attr.baseAtk;
                    _writtenAtk = (int)(_atkBaseline * _atkMult);
                    if (attr.baseAtk != _writtenAtk) attr.baseAtk = _writtenAtk;
                }

                // move speed: PlayerAttribute.SetBaseLevelData resets it to 9 on every ResetData
                if (Mathf.Abs(_moveMult - 1f) > 0.001f)
                {
                    float cur = attr.moveSpeed;
                    if (_moveBase < 0f || Mathf.Abs(cur - _moveWritten) > 0.001f) _moveBase = cur;
                    _moveWritten = _moveBase * _moveMult;
                    if (Mathf.Abs(cur - _moveWritten) > 0.001f) attr.moveSpeed = _moveWritten;
                }
                else _moveBase = -1f;

                // scale: no native field, so drive transform.localScale and keep facing sign
                if (Mathf.Abs(_scaleMult - 1f) > 0.001f)
                {
                    Vector3 sc = player.transform.localScale;
                    if (_scaleBase < 0f) _scaleBase = Mathf.Abs(sc.x) < 0.0001f ? 1f : Mathf.Abs(sc.x);
                    float s = _scaleBase * _scaleMult;
                    if (Mathf.Abs(Mathf.Abs(sc.x) - s) > 0.001f)
                        player.transform.localScale = new Vector3(sc.x < 0f ? -s : s, s, sc.z);
                }
                else _scaleBase = -1f;

                // gravity: TimeController.SetGravity(scale) is the supported path (enemies use it too)
                if (Mathf.Abs(_gravMult - 1f) > 0.001f)
                {
                    var rb = player.Rigidbody2D;
                    if (rb != null)
                    {
                        float cur = rb.gravityScale;
                        if (_gravBase < 0f || Mathf.Abs(cur - _gravWritten) > 0.001f) _gravBase = cur;
                        _gravWritten = _gravBase * _gravMult;
                        if (Mathf.Abs(cur - _gravWritten) > 0.001f) rb.gravityScale = _gravWritten;
                    }
                }
                else _gravBase = -1f;

                if (_passWall) NoClip();

                if (_infCombo) LockCombo();

                if (_oneHitKill || _noEnemyAtk || Mathf.Abs(_enemyHpMult - 1f) > 0.001f || Mathf.Abs(_enemyAtkMult - 1f) > 0.001f)
                    TouchEnemies();

                if (Mathf.Abs(_fov - 52f) > 0.01f)
                {
                    var cam = SingletonMono<CameraController>.Instance;
                    if (cam != null && Mathf.Abs(cam.FieldOfView - _fov) > 0.01f) cam.FieldOfView = _fov;
                }

                if (_camFree) { var cam = SingletonMono<CameraController>.Instance; if (cam != null && cam.IsFollowPivot) cam.IsFollowPivot = false; }

                if (_timeScaleOn) { Time.timeScale = _timeScale; _ownsTimeScale = true; }
                else if (_ownsTimeScale) { Time.timeScale = 1f; _ownsTimeScale = false; }

                if (_pauseTime) { if (!_ownsPause) { WorldTime.Pause(); _ownsPause = true; } }
                else if (_ownsPause) { WorldTime.Resume(); _ownsPause = false; }

                // Harmony patch must be installed once the game assembly is definitely loaded
                if (!_patched) InstallPatch();
            }
            catch (Exception e) { Logger.LogWarning("Apply: " + e.Message); }
        }

        private void InstallPatch()
        {
            _patched = true;
            try
            {
                Type t = AccessTools.TypeByName("PlayerJumpAbility");
                if (t == null) { Logger.LogWarning("PlayerJumpAbility not found; jump slider disabled."); return; }
                var m = AccessTools.Method(t, "Jump");
                if (m == null) { Logger.LogWarning("Jump() not found; jump slider disabled."); return; }
                var method = AccessTools.Method(typeof(JumpHeightPatch), "Postfix");
                new Harmony("icey.cheatpanel").Patch(m, null, new HarmonyMethod(method), null);
                JumpHeightPatch.Mult = _jumpMult;
                Logger.LogInfo("JumpHeightPatch installed on " + t.Name + ".Jump");
            }
            catch (Exception e) { Logger.LogWarning("Harmony patch failed: " + e.Message); }

            // ---- 防反判定窗口(FlashAttack) ----
            try
            {
                Type ft = AccessTools.TypeByName("PlayerAnimEventListener");
                var fm = ft != null ? AccessTools.Method(ft, "FlashStart") : null;
                if (fm == null)
                {
                    Logger.LogWarning("PlayerAnimEventListener.FlashStart not found; flash window slider disabled.");
                    return;
                }
                var post = AccessTools.Method(typeof(FlashWindowPatch), "Postfix");
                new Harmony("icey.cheatpanel.flashwin").Patch(fm, null, new HarmonyMethod(post), null);
                FlashWindowPatch.ExtraSeconds = _flashSeconds;
                Logger.LogInfo("FlashWindowPatch installed on " + ft.Name + ".FlashStart");
            }
            catch (Exception e) { Logger.LogWarning("FlashWindowPatch failed: " + e.Message); }
        }

        // no-clip via the game's own PlatformMovement.AddIgnoreCollider(collider, duration).
        // Takes no game type in its signature on purpose (see JumpHeightPatch note above).
        private void NoClip()
        {
            try
            {
                var player = R.Player;
                if (player == null) return;
                PlatformMovement pm = player.GetComponent<PlatformMovement>();
                if (pm == null) return;
                Vector2 pos = player.transform.position;
                Collider2D[] cols = Physics2D.OverlapCircleAll(pos, 2.5f);
                if (cols == null) return;
                for (int i = 0; i < cols.Length; i++)
                {
                    Collider2D c = cols[i];
                    if (c == null) continue;
                    if (c.transform.IsChildOf(player.transform)) continue;
                    pm.AddIgnoreCollider(c, 0.2f);
                }
            }
            catch { }
        }

        private void LockCombo()
        {
            try
            {
                var sd = R.SceneData;
                if (sd == null || sd.assessmentData == null) return;
                if (sd.assessmentData.CurrentComboNum < _comboLock) sd.assessmentData.CurrentComboNum = _comboLock;
                else _comboLock = sd.assessmentData.CurrentComboNum;
            }
            catch { }
        }

        private void TouchEnemies()
        {
            var list = R.Enemy != null ? R.Enemy.EnemyAttributes : null;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var en = list[i];
                if (en == null) continue;
                int id = en.GetInstanceID();

                if (_oneHitKill && !en.isDead && en.currentHp > 1) en.currentHp = 1;

                int hpBase, hpWritten, atkBase, atkWritten;
                _enemyHpBase.TryGetValue(id, out hpBase);
                _enemyHpWritten.TryGetValue(id, out hpWritten);
                _enemyAtkBase.TryGetValue(id, out atkBase);
                _enemyAtkWritten.TryGetValue(id, out atkWritten);

                if (hpBase <= 0 || en.maxHp != hpWritten) hpBase = en.maxHp;
                if (atkBase == 0 || en.atk != atkWritten) atkBase = en.atk;

                int wantHp = Mathf.Max(1, (int)(hpBase * _enemyHpMult));
                int wantAtk = _noEnemyAtk ? 0 : (int)(atkBase * _enemyAtkMult);

                if (en.maxHp != wantHp) en.maxHp = wantHp;
                if (en.atk != wantAtk) en.atk = wantAtk;

                _enemyHpBase[id] = hpBase; _enemyHpWritten[id] = wantHp;
                _enemyAtkBase[id] = atkBase; _enemyAtkWritten[id] = wantAtk;
            }
        }

        // ================= glass (game's own Blur_Movie on Camera.main) =================
        private void ApplyGlass()
        {
            try
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                GameObject go = cam.gameObject;
                if (_glassOn)
                {
                    if (_blur == null && go.GetComponent<CameraFilterPack_Blur_Movie>() == null)
                    {
                        CameraFilterPack_Blur_Movie m = go.AddComponent<CameraFilterPack_Blur_Movie>();
                        m.SCShader = Shader.Find("CameraFilterPack/Blur_Movie");
                        _blur = m;
                    }
                    PushBlurRadius();
                }
                else if (_blur != null) { Destroy(_blur); _blur = null; }
            }
            catch (Exception e) { Warn("Glass", e); }
        }

        private void PushBlurRadius()
        {
            try
            {
                if (_blur == null) return;
                // must go through the STATIC field: Update() copies it into the instance field each frame
                CameraFilterPack_Blur_Movie.ChangeRadius = _blurRadius;
                CameraFilterPack_Blur_Movie.ChangeFactor = _blurRadius * 1.33f;
                CameraFilterPack_Blur_Movie.ChangeFastFilter = 2;
            }
            catch { }
        }

        private void ApplyFreeze()
        {
            try
            {
                if (_freezeEnemy)
                {
                    // Enemy type skips the player (PlayerTimeController.ClipFrozen returns early for Enemy)
                    WorldTime.Instance.TimeFrozenByFixedFrame(int.MaxValue, WorldTime.FrozenArgs.FrozenType.Enemy, false);
                    _freezeApplied = true;
                }
                else if (_freezeApplied)
                {
                    WorldTime.Instance.FrozenResume();
                    _freezeApplied = false;
                }
            }
            catch (Exception e) { Warn("Freeze", e); }
        }

        // ================= filters =================
        private void EnsureFilterTypes()
        {
            if (FilterTypes.Length > 0) return;
            try
            {
                FilterTypes = CameraFilterUtils.FilterTypes;
                Logger.LogInfo("FilterTypes = " + (FilterTypes != null ? FilterTypes.Length : 0));
            }
            catch (Exception e) { Warn("FilterTypes", e); FilterTypes = new Type[0]; }
        }

        private void ApplyFilter()
        {
            EnsureFilterTypes();
            Camera cam = Camera.main;
            if (cam == null || FilterTypes == null || FilterTypes.Length == 0) return;
            try
            {
                Type want = _filterIdx <= 0 ? null : FilterTypes[Mathf.Clamp(_filterIdx - 1, 0, FilterTypes.Length - 1)];
                if (want == _activeFilter) return;
                if (_filterComp != null) { Destroy(_filterComp); _filterComp = null; }
                _activeFilter = want;
                if (want != null) _filterComp = cam.gameObject.AddComponent(want);
            }
            catch (Exception e) { Warn("Filter", e); }
        }

        private void ApplyNamedFilter(string typeName)
        {
            EnsureFilterTypes();
            try
            {
                for (int i = 0; i < FilterTypes.Length; i++)
                {
                    if (FilterTypes[i] != null && FilterTypes[i].Name == typeName)
                    { _filterIdx = i + 1; _activeFilter = null; ApplyFilter(); Toast("滤镜: " + typeName); return; }
                }
                Toast("没找到滤镜 " + typeName);
            }
            catch (Exception e) { Warn("NamedFilter", e); }
        }

        // ================= actions =================
        private void Warn(string tag, Exception e) { Logger.LogWarning(tag + ": " + e.Message); }
        public void Toast(string s) { _toast = s; _toastUntil = Time.unscaledTime + 3f; }
        public void Confirm(string msg, Action act) { _confirmMsg = msg; _confirmAction = act; _confirmOpen = true; }
        private void ApplyAiNow() { try { if (R.SceneData != null) R.SceneData.CanAIRun = !_aiOff; } catch (Exception e) { Warn("AI", e); } }

        private void CycleDifficulty()
        {
            try
            {
                var gd = R.GameData;
                if (gd == null) return;
                gd.Difficulty = (gd.Difficulty + 1) % 4;
                Toast("难度 = " + gd.Difficulty + " (0易 1普 2难 3地狱)");
            }
            catch (Exception e) { Warn("Difficulty", e); }
        }

        private void RefreshAttributes()
        {
            try
            {
                var p = R.Player;
                if (p == null || p.Attribute == null) return;
                p.Attribute.AllAttributeRecovery();
                Toast("属性已刷新");
            }
            catch (Exception e) { Warn("Refresh", e); Toast("刷新失败: " + e.Message); }
        }

        private void DoAwardAll()
        { try { if (R.Trophy != null) R.Trophy.AwardAllTrophies(); Toast("已解锁全部成就"); } catch (Exception e) { Warn("Trophy", e); } }

        private void DoSaveNow()
        { try { var gd = R.GameData; if (gd != null) gd.Save(false); Toast("已手动存档"); } catch (Exception e) { Warn("SaveNow", e); } }

        private void DoLoadNow()
        {
            try { BackupSaveDir(); } catch { }
            StartCoroutine(LoadRoutine());
        }

        // mirrors Tools.SaveDebugGUI's own LoadGame() sequence.
        // Two constraints here: no yield inside try/catch (CS1626), and NO game-typed local may
        // live across a yield - the compiler would hoist it into an iterator field, i.e. a metadata
        // field carrying a game type, which is exactly what makes BepInEx drop the plugin.
        private IEnumerator LoadRoutine()
        {
            if (R.GameData == null) yield break;

            yield return R.GameData.Load();
            yield return new WaitForEndOfFrame();

            string scene = null;
            try { scene = R.GameData.SceneName; } catch (Exception e) { Warn("Load/scene", e); }

            bool needScene = false;
            try { needScene = !string.IsNullOrEmpty(scene) && scene != LevelManager.SceneName; }
            catch (Exception e) { Warn("Load/compare", e); }

            if (needScene) yield return LevelManager.LoadLevelByGateId(scene, (SceneGate.OpenType)1);
            else SyncAfterLoad();

            Toast("读档完成");
        }

        // Everything game-typed lives here, in a plain method - nothing gets hoisted.
        private void SyncAfterLoad()
        {
            try
            {
                var gd = R.GameData;
                var p = R.Player;
                if (gd != null && p != null)
                {
                    p.transform.position = gd.PlayerPosition;
                    if (p.Action != null) p.Action.TurnRound(gd.PlayerAttributeGameData.faceDir);
                }
                var cam = R.Camera;
                if (cam != null && cam.Controller != null) cam.Controller.CameraResetPostionAfterSwitchScene();
            }
            catch (Exception e) { Warn("Load/sync", e); }
        }

        private void BackupSaveDir()
        {
            try
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string src = Path.Combine(Path.Combine(root, "Low"),
                    Path.Combine("Shanghai FantaBlade Network Technology Co., Ltd", "ICEY"));
                if (!Directory.Exists(src)) { Toast("存档目录不存在"); return; }
                string baseDir = Path.Combine(Paths.ConfigPath, "_savebackup");
                string dst = Path.Combine(baseDir, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(dst);
                string[] files = Directory.GetFiles(src);
                for (int i = 0; i < files.Length; i++)
                {
                    string n = Path.GetFileName(files[i]);
                    if (n.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
                    File.Copy(files[i], Path.Combine(dst, n), true);
                }
                PruneBackups(baseDir, 10);
            }
            catch (Exception e) { Warn("Backup", e); }
        }

        private void PruneBackups(string baseDir, int keep)
        {
            try
            {
                string[] dirs = Directory.GetDirectories(baseDir);
                Array.Sort(dirs);
                for (int i = 0; i < dirs.Length - keep; i++) Directory.Delete(dirs[i], true);
            }
            catch { }
        }

        private void DoBloodPalace()
        {
            try
            {
                var bam = SingletonMono<BattleAssessmentManager>.Instance;
                if (bam == null) { Toast("拿不到血宫管理器"); return; }
                bam.EnterBloodPalace();
                Toast("已进入血宫");
            }
            catch (Exception e) { Warn("BloodPalace", e); Toast("进血宫失败: " + e.Message); }
        }

        private void DoExitBloodPalace()
        {
            try
            {
                var bam = SingletonMono<BattleAssessmentManager>.Instance;
                if (bam == null) return;
                bam.ExitBloodPalace();
                Toast("已退出血宫");
            }
            catch (Exception e) { Warn("ExitBloodPalace", e); }
        }

        private void DoJump()
        {
            try { LevelManager.LoadScene(JumpScenes[Mathf.Clamp(_jumpIdx, 0, JumpScenes.Length - 1)]); }
            catch (Exception e) { Warn("Jump", e); }
        }

        private void GotoLastSafe()
        {
            try
            {
                if (!_haveSafe || R.Player == null) { Toast("还没记录到安全位置"); return; }
                R.Player.SetPosition(_lastSafe);
                Toast("已回到最后安全位置");
            }
            catch (Exception e) { Warn("LastSafe", e); }
        }

        private void GotoNearestEnemy()
        {
            try
            {
                if (R.Player == null || R.Enemy == null) return;
                Vector3 pp = R.Player.transform.position;
                GameObject e = R.Enemy.GetNearestRangeEnemys(new Vector2(pp.x, pp.y), 200f);
                if (e == null) { Toast("附近没有敌人"); return; }
                R.Player.SetPosition(new Vector2(e.transform.position.x, e.transform.position.y));
                Toast("已瞬移到最近敌人");
            }
            catch (Exception ex) { Warn("NearestEnemy", ex); }
        }

        private void SpawnEnemy()
        {
            try
            {
                EnsureEnemyNames();
                if (EnemyNames.Length == 0) return;
                string name = EnemyNames[Mathf.Clamp(_enemyIdx, 0, EnemyNames.Length - 1)];
                EnemyType t = (EnemyType)Enum.Parse(typeof(EnemyType), name);
                Vector2? pos = R.Player != null ? (Vector2?)R.Player.transform.position : null;
                Singleton<EnemyGenerator>.Instance.GenerateEnemy(t, pos, true, true);
                Toast("已生成 " + name);
            }
            catch (Exception e) { Warn("Spawn", e); Toast("生成失败: " + e.Message); }
        }

        private void EnsureEnemyNames()
        {
            if (EnemyNames.Length > 0) return;
            try
            {
                Array vals = Enum.GetValues(typeof(EnemyType));
                List<string> names = new List<string>();
                for (int i = 0; i < vals.Length; i++) names.Add(vals.GetValue(i).ToString());
                EnemyNames = names.ToArray();
            }
            catch (Exception e)
            { EnemyNames = new string[] { "达哈尔", "骑兵", "蜜蜂", "斩轮式一型", "炮击式一型" }; Warn("EnemyEnum", e); }
        }

        private void SetCoinExact()
        { try { if (R.Equipment != null) { R.Equipment.CoinNum = _coinInput; Toast("金币 = " + _coinInput); } } catch (Exception e) { Warn("CoinExact", e); } }

        // indexer is the reliable path; Post() only refreshes maxHP on the player side
        private void SetOneEnhancement(int level)
        {
            try
            {
                var p = R.Player;
                if (p == null || p.Enhancement == null) return;
                string n = EnhNames[Mathf.Clamp(_enhIdx, 0, EnhNames.Length - 1)];
                p.Enhancement[n] = level;
                if (n == "maxHP" || n == "maxEnergy") p.Enhancement.Post(n, level);
                if (p.Attribute != null) p.Attribute.AllAttributeRecovery();
                Toast(n + " -> " + level);
            }
            catch (Exception e) { Warn("OneEnh", e); Toast("失败: " + e.Message); }
        }

        private void AddCoin(int n) { try { if (R.Equipment != null) R.Equipment.CoinNum += n; } catch (Exception e) { Warn("AddCoin", e); } }

        private void SetAllEnhancement(int level)
        {
            try
            {
                var gd = R.GameData;
                if (gd == null || gd.Enhancement == null) return;
                if (level == 0) gd.Enhancement.SetToDefault(); else gd.Enhancement.SetAllTo(level);
            }
            catch (Exception e) { Warn("Enhancement", e); }
        }

        private void KillAll() { try { if (R.Enemy != null) R.Enemy.KillAllEnemy(); } catch (Exception e) { Warn("KillAll", e); } }

        // ================= open / close =================
        private void Toggle()
        {
            _open = !_open;
            Logger.LogInfo("Panel " + (_open ? "OPEN" : "CLOSED"));
            try
            {
                if (_open)
                {
                    R.Mode.EnterMode(Mode.AllMode.UI);
                    R.Ui.Pause.Enabled = false;
                    R.PauseGame();
                    Cursor.visible = true;
                    if (_glassOn) ApplyGlass();
                }
                else
                {
                    if (_blur != null) { Destroy(_blur); _blur = null; }
                    R.Mode.ExitMode(Mode.AllMode.UI);
                    R.Ui.Pause.Enabled = true;
                    R.ResumeGame();
                    Save();          // captures card positions / expanded state / selector indices
                }
            }
            catch (Exception e) { Warn("Pause/cursor", e); }
        }

        // ================= persistence =================
        private void Save()
        {
            try
            {
                List<string> l = new List<string>();
                l.Add("god=" + (_godMode ? 1 : 0));
                l.Add("energy=" + (_infiniteEnergy ? 1 : 0));
                l.Add("onehit=" + (_oneHitKill ? 1 : 0));
                l.Add("flash=" + (_infFlash ? 1 : 0));
                l.Add("combo=" + (_infCombo ? 1 : 0));
                l.Add("combohud=" + (_comboHud ? 1 : 0));
                l.Add("ai=" + (_aiOff ? 1 : 0));
                l.Add("hud=" + (_hud ? 1 : 0));
                l.Add("dps=" + (_dpsOn ? 1 : 0));
                l.Add("bounds=" + (_boundsOn ? 1 : 0));
                l.Add("wall=" + (_passWall ? 1 : 0));
                l.Add("noeatk=" + (_noEnemyAtk ? 1 : 0));
                l.Add("freeze=" + (_freezeEnemy ? 1 : 0));
                l.Add("camfree=" + (_camFree ? 1 : 0));
                l.Add("glass=" + (_glassOn ? 1 : 0));
                l.Add("pause=" + (_pauseTime ? 1 : 0));
                l.Add("atk=" + _atkMult.ToString("R"));
                l.Add("move=" + _moveMult.ToString("R"));
                l.Add("jump=" + _jumpMult.ToString("R"));
                l.Add("flashwinsec=" + _flashSeconds.ToString("R"));
                l.Add("flashwinframes=" + _flashFrames.ToString("R"));
                l.Add("scale=" + _scaleMult.ToString("R"));
                l.Add("grav=" + _gravMult.ToString("R"));
                l.Add("ehp=" + _enemyHpMult.ToString("R"));
                l.Add("eatk=" + _enemyAtkMult.ToString("R"));
                l.Add("time=" + (_timeScaleOn ? 1 : 0));
                l.Add("timescale=" + _timeScale.ToString("R"));
                l.Add("fov=" + _fov.ToString("R"));
                l.Add("blur=" + _blurRadius.ToString("R"));
                l.Add("coin=" + _coinInput);
                l.Add("jumpi=" + _jumpIdx);
                l.Add("enemyi=" + _enemyIdx);
                l.Add("enhi=" + _enhIdx);
                l.Add("filteri=" + _filterIdx);
                l.Add("skin=" + (_skinOn ? 1 : 0));
                l.Add("skini=" + _skinIdx);
                l.Add("skinflip=" + (_skinFlip ? 1 : 0));
                l.Add("hidebones=" + (_hideOriginal ? 1 : 0));
                l.Add("bonehud=" + (_boneHud ? 1 : 0));
                l.Add("blocky=" + (_blockyOn ? 1 : 0));
                l.Add("blockyscale=" + _blockyScale.ToString("R"));
                l.Add("texton=" + (_textOn ? 1 : 0));
                l.Add("borrowi=" + _borrowIdx);
                l.Add("origover=" + _origOverlay);
                l.Add("blockyaw=" + _blockyYaw.ToString("R"));
                l.Add("blockswing=" + _blockySwing.ToString("R"));
                l.Add("blockysword=" + _blockySword);
                l.Add("blockyvfx=" + (_blockyHideVfx ? 1 : 0));
                l.Add("vfxradius=" + _vfxRadius.ToString("R"));
                l.Add("mcatk=" + (_mcAtkMode ? 1 : 0));
                l.Add("mcnoslash=" + (_mcNoSlash ? 1 : 0));
                l.Add("mcatkint=" + _mcAtkInterval.ToString("R"));
                l.Add("swordrot=" + _swordRot.ToString("R"));
                l.Add("swordrotx=" + _swordRotX.ToString("R"));
                l.Add("swordroty=" + _swordRotY.ToString("R"));
                l.Add("swordz=" + _swordZPx.ToString("R"));
                l.Add("swordlen=" + _swordLen.ToString("R"));
                l.Add("wmon=" + (_wmOn ? 1 : 0));
                l.Add("wmall=" + (_wmAll ? 1 : 0));
                l.Add("wmscale=" + _wmScale.ToString("R"));
                l.Add("wmen=" + (_wmEn ? 1 : 0));
                l.Add("uidark=" + (_uiDark ? 1 : 0));
                l.Add("devmode=" + (_devMode ? 1 : 0));
                l.Add("scrollinvert=" + (_scrollInvert ? 1 : 0));
                l.Add("fxon=" + (_fxOn ? 1 : 0));
                l.Add("fxrainbow=" + (_fxRainbow ? 1 : 0));
                l.Add("fxspeed=" + _fxSpeed.ToString("R"));
                l.Add("fxsat=" + _fxSat.ToString("R"));
                l.Add("fxmin=" + _fxMin.ToString("R"));
                // layout is stored in DESIGN units (pixels / S) so it survives a resolution change
                for (int i = 0; i < CardCount; i++)
                    l.Add("card" + i + "=" + (_cardX[i] / S).ToString("F1") + "," +
                          (_cardY[i] / S).ToString("F1") + "," + (_cardOpen[i] ? "1" : "0"));
                File.WriteAllLines(ConfigPath, l.ToArray());
            }
            catch (Exception e) { Warn("Save", e); }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                foreach (string line in File.ReadAllLines(ConfigPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "god": _godMode = v == "1"; break;
                        case "energy": _infiniteEnergy = v == "1"; break;
                        case "onehit": _oneHitKill = v == "1"; break;
                        case "flash": _infFlash = v == "1"; break;
                        case "combo": _infCombo = v == "1"; break;
                        case "combohud": _comboHud = v == "1"; break;
                        case "ai": _aiOff = v == "1"; break;
                        case "hud": _hud = v == "1"; break;
                        case "dps": _dpsOn = v == "1"; break;
                        case "bounds": _boundsOn = v == "1"; break;
                        case "wall": _passWall = v == "1"; break;
                        case "noeatk": _noEnemyAtk = v == "1"; break;
                        case "freeze": _freezeEnemy = v == "1"; break;
                        case "camfree": _camFree = v == "1"; break;
                        case "glass": _glassOn = v == "1"; break;
                        case "pause": _pauseTime = v == "1"; break;
                        case "atk": TryF(v, ref _atkMult); break;
                        case "move": TryF(v, ref _moveMult); break;
                        case "jump": TryF(v, ref _jumpMult); break;
                        case "flashwinsec": TryF(v, ref _flashSeconds); break;
                        case "flashwinframes": TryF(v, ref _flashFrames); break;
                        case "scale": TryF(v, ref _scaleMult); break;
                        case "grav": TryF(v, ref _gravMult); break;
                        case "ehp": TryF(v, ref _enemyHpMult); break;
                        case "eatk": TryF(v, ref _enemyAtkMult); break;
                        case "time": _timeScaleOn = v == "1"; break;
                        case "timescale": TryF(v, ref _timeScale); break;
                        case "fov": TryF(v, ref _fov); break;
                        case "blur": TryF(v, ref _blurRadius); break;
                        case "coin": int.TryParse(v, out _coinInput); break;
                        case "jumpi": int.TryParse(v, out _jumpIdx); break;
                        case "enemyi": int.TryParse(v, out _enemyIdx); break;
                        case "enhi": int.TryParse(v, out _enhIdx); break;
                        case "filteri": int.TryParse(v, out _filterIdx); break;
                        case "skin": _skinOn = v == "1"; break;
                        case "skini": int.TryParse(v, out _skinIdx); break;
                        case "skinflip": _skinFlip = v == "1"; break;
                        case "hidebones": _hideOriginal = v == "1"; break;
                        case "bonehud": _boneHud = v == "1"; break;
                        case "blocky": _blockyOn = v == "1"; break;
                        case "blockyscale": TryF(v, ref _blockyScale); break;
                        case "texton": _textOn = v == "1"; break;
                        case "borrowi": int.TryParse(v, out _borrowIdx); break;
                        case "origover": int.TryParse(v, out _origOverlay); break;
                        case "blockyaw": TryF(v, ref _blockyYaw); break;
                        case "blockswing": TryF(v, ref _blockySwing); break;
                        case "blockysword": int.TryParse(v, out _blockySword); break;
                        case "blockyvfx": _blockyHideVfx = v == "1"; break;
                        case "vfxradius": TryF(v, ref _vfxRadius); break;
                        case "mcatk": _mcAtkMode = v == "1"; break;
                        case "mcnoslash": _mcNoSlash = v == "1"; break;
                        case "mcatkint": TryF(v, ref _mcAtkInterval); break;
                        case "swordrot": TryF(v, ref _swordRot); break;
                        case "swordrotx": TryF(v, ref _swordRotX); break;
                        case "swordroty": TryF(v, ref _swordRotY); break;
                        case "swordz": TryF(v, ref _swordZPx); break;
                        case "swordlen": TryF(v, ref _swordLen); break;
                        case "wmon": _wmOn = v == "1"; break;
                        case "wmall": _wmAll = v == "1"; break;
                        case "wmscale": TryF(v, ref _wmScale); _stWm = null; break;
                        case "wmen": _wmEn = v == "1"; break;
                        case "uidark": _uiDark = v == "1"; break;
                        case "devmode": _devMode = v == "1"; break;
                        case "scrollinvert": _scrollInvert = v == "1"; break;
                        case "fxon": _fxOn = v == "1"; break;
                        case "fxrainbow": _fxRainbow = v == "1"; break;
                        case "fxspeed": TryF(v, ref _fxSpeed); break;
                        case "fxsat": TryF(v, ref _fxSat); break;
                        case "fxmin": TryF(v, ref _fxMin); break;
                        default: LoadCardLayout(k, v); break;
                    }
                }
                JumpHeightPatch.Mult = _jumpMult;
                // 防反窗口以"秒"为准; 秒缺失而帧有值时(换了帧率/旧配置)用帧反推一次
                if (_flashSeconds <= 0.0001f && _flashFrames > 0.5f)
                    _flashSeconds = FlashWindowPatch.FramesToSeconds(_flashFrames);
                _flashFrames = FlashWindowPatch.SecondsToFrames(_flashSeconds);
                FlashWindowPatch.ExtraSeconds = _flashSeconds;
                Logger.LogInfo("Config loaded.");
            }
            catch (Exception e) { Warn("Load", e); }
        }

        // "card3=120.0,88.0,1" -> position (design units) + expanded flag
        private void LoadCardLayout(string key, string val)
        {
            try
            {
                if (key == null || !key.StartsWith("card")) return;
                int idx;
                if (!int.TryParse(key.Substring(4), out idx)) return;
                if (idx < 0 || idx >= CardCount) return;
                string[] parts = val.Split(',');
                if (parts.Length < 3) return;
                float x, y;
                if (!float.TryParse(parts[0], out x)) return;
                if (!float.TryParse(parts[1], out y)) return;
                _cardX[idx] = x * S;
                _cardY[idx] = y * S;
                _cardOpen[idx] = parts[2].Trim() == "1";
            }
            catch { }
        }

        private static void TryF(string s, ref float f)
        { float r; if (float.TryParse(s, out r)) f = r; }
    }
}
