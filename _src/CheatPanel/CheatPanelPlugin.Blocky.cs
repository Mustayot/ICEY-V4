// ICEY Cheat Panel - 「MC 方块人」v2 内核:真 6 面立方体 + 程序化 MC 式动画 + MC 素材。partial class 第五个文件。
//
// 目标(主人定的):不是"艾希的动作方块化",而是**MC 小人本人在 ICEY 里打**。
//   * 皮肤:主人给的艾利克斯(64×64 经典 4px 手臂布局)
//   * 真 6 面立方体,UV 直接照 MC 的盒展开布局(头/身/臂/腿各 6 个面)
//   * 动画**程序化**(不跟艾希的骨头):站→呼吸微摆;走/跑→摆腿摆臂;跳→双臂张开;
//     攻击→每段连击都挥(横挥/回手/上挑);受击→后仰;死亡→倒
//   * 右手拿**钻石剑**(贴图从 MC 1.18.2 的 jar 里取的物品贴图),剑是手臂的子物体 → 自动跟着挥
//   * 攻击时**藏掉游戏原本的剑光特效**(按材质/贴图名筛,记账可还原)
//
// 骨架(部位局部坐标 = "以关节为原点",这样旋转关节 = MC 的旋转模型):
//   root(脚底,朝向 = scale.x 的正负,偏航 = _blockyYaw)
//     ├ body(腰部为原点,盒子向上长 12)
//     │   ├ head(颈部为原点,向上 8)
//     │   ├ armR(肩为原点,向下 12;x = +6)
//     │   ├ armL(同,x = -6)
//     │   └ sword(右臂的子物体,挂在手部)
//     ├ legR(髋为原点,向下 12;x = +2)
//     └ legL(同,x = -2)
//   总高 = 12(腿) + 12(身) + 8(头) = 32 个单位 → 世界高度按主角包围盒实测后除以 32 得 dH。
//
// 踩过的点(改这里必读):
//   * 贴图方向:载入后**翻一次**(和配色替换同一个结论)→ 四边形**上面**两个顶点取 `v = y/64`(矩形的顶行),
//     下面取 `(y+h)/64`。写反的表现是整块上下颠倒。
//   * shader:优先找"不透明/会写深度"的(真 3D 盒子靠深度缓冲自遮挡),找不到再退透明那批;
//     命中哪个写进日志(搜 `[mc]`)。
//   * 动画名:优先读骨架的 `state.GetCurrent(0).Animation.Name`,取不到再反射读游戏控制器的 `currentAnim`。

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace IceyCheatPanel
{
    public partial class CheatPanelPlugin
    {
        public bool _blockyOn;
        public float _blockyScale = 1f;
        public float _blockyYaw = 22f;       // 偏航角(度):横版正面视角下给一点 3/4,才看得出体积
        public float _blockySwing = 1f;      // 挥动幅度倍率
        public bool _blockyHideVfx = true;   // 藏掉剑光特效(独立开关,不再跟着 MC 攻击方式)
        public bool _mcNoSlash;              // ★ 去剑光:★暂时默认**关**(主人说"先不做这个")★
                                             //   原因:刀光是 NewPlayerTestEffect* 那套"特效骨架",目前还没找到干净的拦法;
                                             //   留着开关和代码,等以后有思路再打开,别让它每秒钟扫全场浪费性能。
        public int _blockySword = 5;         // 0=空手 1=木 2=石 3=铁 4=金 5=钻石
        public string _blockyStatus = "未构建";
        public int _origOverlay;             // 0 = 藏原角色(剑和刀光一起没),1 = 原角色实体叠加
        public static readonly string[] OrigOverlayNames = { "隐藏(只显示方块人)", "原角色实体叠加(武器/刀光回来)" };
        public static readonly string[] SwordNames = { "空手", "木剑", "石剑", "铁剑", "金剑", "钻石剑" };

        // 部位顺序固定:0 头 1 身 2 右臂 3 左臂 4 右腿 5 左腿
        private static readonly string[] McPartName = { "头", "身体", "右臂", "左臂", "右腿", "左腿" };
        // 每部位:宽, 高, 厚(Minecraft 单位);yDown = 盒子从关节点向下长
        private static readonly float[] McW = { 8, 8, 4, 4, 4, 4 };
        private static readonly float[] McH = { 8, 12, 12, 12, 12, 12 };
        private static readonly float[] McD = { 8, 4, 4, 4, 4, 4 };
        private static readonly bool[] McDown = { false, false, true, true, true, true };
        // 关节点(相对 root,单位 = 皮肤像素):x, y
        private static readonly float[] McPx = { 0, 0, 6, -6, 2, -2 };
        private static readonly float[] McPy = { 24, 12, 24, 24, 12, 12 };
        // 父部位(0 头 → 身体;2/3 臂 → 身体;其余 → root)
        private static readonly int[] McParent = { 1, -1, 1, 1, -1, -1 };

        // MC 64×64 盒展开 UV:每部位 6 个面(右 前 左 后 顶 底),每面 x,y,w,h
        private static readonly int[,,] McUv = new int[6, 6, 4] {
            { {0,8,8,8},   {8,8,8,8},   {16,8,8,8},  {24,8,8,8},  {8,0,8,8},   {16,0,8,8}  }, // 头
            { {16,20,4,12},{20,20,8,12},{28,20,4,12},{32,20,8,12},{20,16,8,4},{28,16,8,4} }, // 身
            { {40,20,4,12},{44,20,4,12},{48,20,4,12},{52,20,4,12},{44,16,4,4},{48,16,4,4} }, // 右臂
            { {32,52,4,12},{36,52,4,12},{40,52,4,12},{44,52,4,12},{36,48,4,4},{40,48,4,4} }, // 左臂
            { {0,20,4,12}, {4,20,4,12}, {8,20,4,12}, {12,20,4,12},{4,16,4,4}, {8,16,4,4}  }, // 右腿
            { {16,52,4,12},{20,52,4,12},{24,52,4,12},{28,52,4,12},{20,48,4,4},{24,48,4,4} }, // 左腿
        };

        private sealed class McPart
        {
            public GameObject Node;
            public MeshRenderer Mr;
            public Mesh Mesh;
            public float D;              // 该部位的"每皮肤像素 → 世界单位"
        }

        private GameObject _mcRoot;
        private readonly McPart[] _mcParts = new McPart[6];
        private MeshRenderer _mcSwordMr;
        private Mesh _mcSwordMesh;
        private Material _mcSwordSideMat;
        private Texture2D _mcSkin, _mcSwordTex;
        private Material _mcMat, _mcSwordMat;
        private Shader _mcShader;
        private float _mcD, _mcH;
        private float _mcFootDelta;
        private float _mcAnimPhase, _mcAttackT = -1f, _mcAttackKind;
        private bool _mcCanCull;             // 材质能不能改 _Cull(决定"镜像朝向"还是"转向")
        private float _mcNextSync, _mcNextVfx;
        private string _mcAnim = "", _mcLastAnim = "";
        private Component _mcCtrl;                       // 游戏自己的 MultiSpineAnimationController(反射用)
        private static FieldInfo _fCtrlAnim;

        // 藏掉的剑光/特效渲染器(记账,关掉方块人要原样还回去)
        private readonly List<Renderer> _vfxOff = new List<Renderer>();
        public float _swordRot = -45f;       // 剑在手里的角度(度)
        public float _swordRotX = -16f;      // 俯仰(立体感:别正对镜头)
        public float _swordRotY = 24f;       // 偏航
        public float _swordZPx = 3f;         // 剑往"镜头方向"偏多少(皮肤像素),让它握在身体前面
        public float _swordLen = 0.62f;      // 剑长倍率(相对身高)

        // ★★ 刀光真身(从日志的"带组件名清单"里读出来的,别再猜):
        //   Player(Clone)/NewAirATKHv5(Clone)  大小 7.4×48.7  贴图 NewPlayerTestEffect2
        //   组件: SkeletonAnimation, AutoDestroy, PlayerEffectSync
        //   NewATK1(Clone) / NewATK2(Clone)    贴图 NewPlayerTestEffect
        // → 它们是**挂在主角身上的"特效骨架"实例**,不是材质、不是 FantaBlade。
        private static readonly string[] VfxKeys = {
            "newplayertesteffect", "newairatk", "newatk", "playereffectsync",
            "fullscreencolormask", "lineinscene", "propertyinscene",
            "playerflash", "simpleflash", "thinring", "windy", "xft", "slash"
        };
        private static readonly string[] VfxStrong = {
            "newplayertesteffect", "newairatk", "newatk", "playereffectsync", "fullscreencolormask"
        };

        public string BlockySkinPath
        {
            get { return Path.Combine(Path.Combine(Paths.ConfigPath, "player_skin"), Path.Combine("_blocky", "skin.png")); }
        }
        public string SwordTexDir
        {
            get { return Path.Combine(Path.Combine(Paths.ConfigPath, "player_skin"), "_blocky"); }
        }
        private static readonly string[] SwordFiles = { "", "wooden_sword.png", "stone_sword.png", "iron_sword.png", "golden_sword.png", "diamond_sword.png" };

        // ================= 开关 =================
        public void SetBlocky(bool v)
        {
            _blockyOn = v;
            if (v)
            {
                _hideOriginal = (_origOverlay == 0);
                if (_skels.Count == 0) ScanSkeletons();
                ApplyHideOriginal();
                BuildBlocky();
                if (_blockyHideVfx) HideVfx();
            }
            else
            {
                DestroyBlocky();
                RestoreVfx();
                _hideOriginal = false;
                ApplyHideOriginal();
            }
            Save();
        }

        public void SetOrigOverlay(int v)
        {
            _origOverlay = Mathf.Clamp(v, 0, OrigOverlayNames.Length - 1);
            _hideOriginal = (_origOverlay == 0);
            if (_skels.Count == 0) ScanSkeletons();
            ApplyHideOriginal();
            Toast("原角色: " + OrigOverlayNames[_origOverlay]);
            Save();
        }

        public void ReloadBlockySkin()
        {
            try { if (_mcSkin != null) Destroy(_mcSkin); } catch { }
            try { if (_mcSwordTex != null) Destroy(_mcSwordTex); } catch { }
            _mcSkin = null; _mcSwordTex = null;
            if (_blockyOn) { DestroyBlocky(); BuildBlocky(); }
            Toast("方块人素材已重载");
        }

        public void SetSword(int idx)
        {
            _blockySword = Mathf.Clamp(idx, 0, SwordNames.Length - 1);
            try { if (_mcSwordTex != null) Destroy(_mcSwordTex); } catch { }
            _mcSwordTex = null;
            if (_blockyOn) { DestroyBlocky(); BuildBlocky(); }
            Toast("手持: " + SwordNames[_blockySword]);
            Save();
        }

        // ================= 每帧 =================
        public void UpdateBlocky()
        {
            if (!_blockyOn) return;
            try
            {
                if (_mcRoot == null) { BuildBlocky(); return; }
                float now = Time.unscaledTime;
                if (now >= _mcNextSync) { _mcNextSync = now + 0.02f; SyncMcRoot(); }
                AnimateParts(now);
                if (_mcNoSlash && now >= _mcNextVfx) { _mcNextVfx = now + 1f; HideVfx(); }   // 只在"去剑光"开着时才扫(已搁置)
                if (now >= _mcNextAuto && _mcNoSlash) { _mcNextAuto = now + 3f; AutoDumpNearby(); }   // 只在"去剑光"开着时才记(已搁置,默认不动)
            }
            catch (Exception e) { Warn("MC/update", e); }
        }

        // ================= 构建 =================
        private void BuildBlocky()
        {
            try
            {
                DestroyBlocky();
                var p = R.Player;
                if (p == null || p.transform == null) { _blockyStatus = "还没生成玩家"; return; }

                Texture2D skin = LoadMcSkin(BlockySkinPath, ref _mcSkin, "皮肤");
                if (skin == null) { _blockyStatus = "缺皮肤: " + BlockySkinPath; return; }
                if (_mcShader == null) _mcShader = PickMcShader();
                if (_mcShader == null) { _blockyStatus = "没找到可用 shader(看日志 [mc])"; return; }

                _mcMat = new Material(_mcShader);
                _mcMat.mainTexture = skin;
                if (_mcMat.HasProperty("_Color")) _mcMat.color = Color.white;
                _mcCanCull = _mcMat.HasProperty("_Cull");
                if (_mcCanCull) _mcMat.SetFloat("_Cull", 0f);      // 0 = Off:双面。这样"镜像朝向"不会因为绕序翻转而变成里外反过来
                // 关键:排进透明队列(3000)。不透明队列(2000)的物体会先画、且 sortingOrder 无效,
                // 容易被游戏后画的场景/背景盖住;队列设成 3000 就还按 sortingOrder 排队,同时照样写深度做自遮挡。
                _mcMat.renderQueue = 3000;
                Logger.LogInfo("[mc] 材质: shader=" + _mcShader.name + " _Cull属性=" + (_mcCanCull ? "有(设 Off)" : "没有(改用转向代替镜像)") + " queue=3000");

                // 实测身高 → 每像素的世界尺寸
                float height, footY, cx;
                MeasurePlayer(out height, out footY, out cx);
                _mcH = height;
                try { var pp = R.Player; _mcFootDelta = (pp != null && pp.transform != null) ? (footY - pp.transform.position.y) : 0f; } catch { }
                _mcD = Mathf.Max(0.2f, height * _blockyScale) / 32f;

                _mcRoot = new GameObject("IceyMcModel");

                // 创建顺序很重要:子部位的父物体(身体)必须先建好,否则挂父物体时取到 null。
                // 身体(1)→ 头(0)→ 双臂(2,3)→ 双腿(4,5)
                int[] order = { 1, 0, 2, 3, 4, 5 };
                for (int oi = 0; oi < order.Length; oi++)
                {
                    int i = order[oi];
                    McPart part = new McPart();
                    part.D = _mcD;
                    GameObject go = new GameObject("mc_" + McPartName[i]);
                    int par = McParent[i];
                    if (par < 0) go.transform.parent = _mcRoot.transform;
                    else
                    {
                        if (_mcParts[par] == null || _mcParts[par].Node == null)
                        {
                            Logger.LogWarning("[mc] 父部位还没建好: " + McPartName[i] + " → " + (par >= 0 ? McPartName[par] : "root") + ",先挂到 root");
                            go.transform.parent = _mcRoot.transform;
                            par = -1;
                        }
                        else go.transform.parent = _mcParts[par].Node.transform;
                    }
                    go.transform.localPosition = (par < 0)
                        ? new Vector3(McPx[i] * _mcD, McPy[i] * _mcD, 0f)
                        : new Vector3((McPx[i] - McPx[par]) * _mcD, (McPy[i] - McPy[par]) * _mcD, 0f);
                    go.transform.localRotation = Quaternion.identity;

                    part.Mesh = MakeBoxMesh(i, McW[i] * _mcD, McH[i] * _mcD, McD[i] * _mcD, McDown[i]);
                    MeshFilter mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = part.Mesh;
                    MeshRenderer mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _mcMat;
                    MeshRenderer srcMr = FirstSkeletonRenderer();
                    if (srcMr != null) { try { mr.sortingLayerName = srcMr.sortingLayerName; mr.sortingOrder = srcMr.sortingOrder + 1 + i; } catch { } }
                    part.Node = go; part.Mr = mr;
                    _mcParts[i] = part;
                }

                BuildSword();
                _blockyStatus = "已构建 身高 " + height.ToString("F2") + " shader " + _mcShader.name + " 手持 " + SwordNames[_blockySword];
                Logger.LogInfo("[mc] " + _blockyStatus);
                SyncMcRoot();
                AnimateParts(Time.unscaledTime);
            }
            catch (Exception e)
            {
                _blockyStatus = "构建失败: " + e.Message + " @" + FirstLineOf(e.StackTrace);
                Logger.LogError("[mc] 构建异常: " + e);
                Warn("MC/build", e);
            }
        }

        private static string FirstLineOf(string s)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return "?";
                string[] lines = s.Split('\n');
                string l = lines[0].Trim();
                int i = l.IndexOf(" in ");
                if (i > 0) l = l.Substring(0, i);
                return l;
            }
            catch { return "?"; }
        }

        private void DestroyBlocky()
        {
            try { if (_mcRoot != null) Destroy(_mcRoot); } catch { }
            _mcRoot = null;
            for (int i = 0; i < 6; i++) { if (_mcParts[i] != null && _mcParts[i].Mesh != null) { try { Destroy(_mcParts[i].Mesh); } catch { } } _mcParts[i] = null; }
            try { if (_mcSwordMesh != null) Destroy(_mcSwordMesh); } catch { }
            try { if (_mcMat != null) Destroy(_mcMat); } catch { }
            try { if (_mcSwordMat != null) Destroy(_mcSwordMat); } catch { }
            try { if (_mcSwordSideMat != null) Destroy(_mcSwordSideMat); } catch { }
            _mcSwordSideMat = null;
            _mcSwordMesh = null; _mcMat = null; _mcSwordMat = null; _mcSwordMr = null;
        }

        // 一个盒子(6 面),以"关节点"为原点;UV 取自 McUv。yDown=true 时盒子从原点往下长。
        private Mesh MakeBoxMesh(int part, float w, float h, float d, bool yDown)
        {
            Mesh m = new Mesh();
            List<Vector3> vs = new List<Vector3>(24);
            List<Vector2> uv = new List<Vector2>(24);
            List<int> ts = new List<int>(36);
            float hw = w * 0.5f, hd = d * 0.5f;
            float y0 = yDown ? -h : 0f, y1 = yDown ? 0f : h;

            // 六个面:0 右(+X) 1 前(+Z) 2 左(-X) 3 后(-Z) 4 顶(+Y) 5 底(-Y)
            // 每面 4 个角(逆时针,从外侧看),顺序:左下/右下/右上/左上(在面自己的局部里)
            Vector3[][] corners = new Vector3[6][];
            corners[0] = new Vector3[] { new Vector3(hw, y0, hd), new Vector3(hw, y0, -hd), new Vector3(hw, y1, -hd), new Vector3(hw, y1, hd) };   // +X
            corners[1] = new Vector3[] { new Vector3(-hw, y0, hd), new Vector3(hw, y0, hd), new Vector3(hw, y1, hd), new Vector3(-hw, y1, hd) };   // +Z
            corners[2] = new Vector3[] { new Vector3(-hw, y0, -hd), new Vector3(-hw, y0, hd), new Vector3(-hw, y1, hd), new Vector3(-hw, y1, -hd) }; // -X
            corners[3] = new Vector3[] { new Vector3(hw, y0, -hd), new Vector3(-hw, y0, -hd), new Vector3(-hw, y1, -hd), new Vector3(hw, y1, -hd) }; // -Z
            corners[4] = new Vector3[] { new Vector3(-hw, y1, hd), new Vector3(hw, y1, hd), new Vector3(hw, y1, -hd), new Vector3(-hw, y1, -hd) };   // +Y
            corners[5] = new Vector3[] { new Vector3(-hw, y0, -hd), new Vector3(hw, y0, -hd), new Vector3(hw, y0, hd), new Vector3(-hw, y0, hd) }; // -Y

            for (int f = 0; f < 6; f++)
            {
                int x = McUv[part, f, 0], y = McUv[part, f, 1], uw = McUv[part, f, 2], uh = McUv[part, f, 3];
                // 贴图翻转过了 → 四边形"上面"取 v = y/64(矩形顶行),"下面"取 (y+h)/64
                float u0 = (float)x / 64f, u1 = (float)(x + uw) / 64f;
                float vTop = (float)y / 64f, vBot = (float)(y + uh) / 64f;
                Vector2[] fuv = new Vector2[] {
                    new Vector2(u0, vBot), new Vector2(u1, vBot), new Vector2(u1, vTop), new Vector2(u0, vTop)
                };
                int b = vs.Count;
                for (int k = 0; k < 4; k++) { vs.Add(corners[f][k]); uv.Add(fuv[k]); }
                ts.Add(b + 0); ts.Add(b + 1); ts.Add(b + 2);
                ts.Add(b + 0); ts.Add(b + 2); ts.Add(b + 3);
            }
            m.vertices = vs.ToArray();          // Unity 5.4 稳妥用数组赋值,不用 SetVertices
            m.uv = uv.ToArray();
            m.triangles = ts.ToArray();
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;        }

        // 手里的方块剑:一片扁平 quad(MC 的物品本来就是平的),挂在右臂下面
        private void BuildSword()
        {
            try
            {
                if (_blockySword <= 0 || _mcParts[2] == null) return;
                string file = SwordFiles[Mathf.Clamp(_blockySword, 0, SwordFiles.Length - 1)];
                string path = Path.Combine(SwordTexDir, file);
                Texture2D tex = LoadMcSkin(path, ref _mcSwordTex, "剑");
                if (tex == null) { Logger.LogWarning("[mc] 剑贴图缺失: " + path); return; }
                if (_mcSwordMat == null)
                {
                    // 剑必须用**支持透明**的 shader:16×16 物品贴图有一大半是透明的,
                    // 用不透明 shader(Diffuse)会把透明像素画成**黑色方块**(主人截图里剑旁边那坨黑就是它)。
                    Shader sw = null;
                    try { sw = Shader.Find("Spine/Skeleton"); } catch { }        // 引擎"总是包含"的 4 个之一 ✔
                    if (sw == null) { try { sw = Shader.Find("Sprites/Default"); } catch { } }
                    if (sw == null) sw = _mcShader;                               // 最后才退回不透明(会黑)
                    _mcSwordMat = new Material(sw);
                    if (_mcSwordMat.HasProperty("_Color")) _mcSwordMat.color = Color.white;
                    if (_mcSwordMat.HasProperty("_Cull")) _mcSwordMat.SetFloat("_Cull", 0f);
                    _mcSwordMat.renderQueue = 3001;      // 比身体晚一点画 → 剑在身体前面
                    Logger.LogInfo("[mc] 剑 shader = " + (sw != null ? sw.name : "空") + "(必须支持透明)");
                }
                _mcSwordMat.mainTexture = tex;

                float len = _mcH * Mathf.Clamp(_swordLen, 0.1f, 2f) * _blockyScale;   // 剑长(可调)
                float hw = len * 0.5f * 0.25f;                    // 剑宽 ≈ 长度 1/4(16×16 贴图的正方形比例)
                float th = Mathf.Max(_mcD * 0.9f, hw * 0.45f);    // ★ 厚度:不再是"一张纸",做成薄板 → MC 物品那种立体感

                // 两块子网格:0 = 正/背两面(贴物品贴图),1 = 四条边(深色,做出"厚度")
                Mesh m = new Mesh();
                System.Collections.Generic.List<Vector3> vs = new System.Collections.Generic.List<Vector3>();
                System.Collections.Generic.List<Vector2> uv = new System.Collections.Generic.List<Vector2>();
                System.Collections.Generic.List<int> sub0 = new System.Collections.Generic.List<int>();
                System.Collections.Generic.List<int> sub1 = new System.Collections.Generic.List<int>();
                float y0 = -len * 0.5f, y1 = len * 0.5f, zf = th * 0.5f, zb = -th * 0.5f;   // +Z 朝镜头

                // 前面(+Z)与背面(-Z):UV 按"四边形上面取矩形顶行"的约定
                for (int f = 0; f < 2; f++)
                {
                    float z = (f == 0) ? zf : zb;
                    int b = vs.Count;
                    vs.Add(new Vector3(-hw, y0, z)); vs.Add(new Vector3(hw, y0, z));
                    vs.Add(new Vector3(hw, y1, z)); vs.Add(new Vector3(-hw, y1, z));
                    uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
                    uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 0));
                    sub0.Add(b); sub0.Add(b + 2); sub0.Add(b + 1);
                    sub0.Add(b); sub0.Add(b + 3); sub0.Add(b + 2);
                }
                // 四条边(左右上下各一片),UV 全给中心点(用纯色材质)
                float[][] edges = new float[][] {
                    new float[]{ -hw, y0, zb,  -hw, y0, zf,  -hw, y1, zf,  -hw, y1, zb },   // 左
                    new float[]{  hw, y0, zf,   hw, y0, zb,   hw, y1, zb,   hw, y1, zf },   // 右
                    new float[]{ -hw, y1, zf,   hw, y1, zf,   hw, y1, zb,  -hw, y1, zb },   // 上
                    new float[]{ -hw, y0, zb,   hw, y0, zb,   hw, y0, zf,  -hw, y0, zf }    // 下
                };
                for (int e = 0; e < 4; e++)
                {
                    int b = vs.Count;
                    for (int k = 0; k < 4; k++) { vs.Add(new Vector3(edges[e][k * 3], edges[e][k * 3 + 1], edges[e][k * 3 + 2])); uv.Add(new Vector2(0.5f, 0.5f)); }
                    sub1.Add(b); sub1.Add(b + 1); sub1.Add(b + 2);
                    sub1.Add(b); sub1.Add(b + 2); sub1.Add(b + 3);
                }
                m.vertices = vs.ToArray();
                m.uv = uv.ToArray();
                m.subMeshCount = 2;
                m.SetTriangles(sub0.ToArray(), 0);
                m.SetTriangles(sub1.ToArray(), 1);
                m.RecalculateNormals();
                m.RecalculateBounds();
                _mcSwordMesh = m;

                GameObject go = new GameObject("mc_sword");
                go.transform.parent = _mcParts[2].Node.transform;      // 右臂的子物体 → 自动跟着挥
                // 手部(臂长 12,取 0.85);再往**镜头方向**偏一点,让它握在身体前面而不是插在身体里
                // (模型整体绕 Y 转了 180°,所以"朝镜头"在模型局部是 +Z)
                go.transform.localPosition = new Vector3(0f, -McH[2] * _mcD * 0.85f, _swordZPx * _mcD);
                // MC 那种"手里的物品有立体朝向":给它俯仰 + 偏航,不再正对镜头(正对镜头就会像贴纸)
                go.transform.localRotation = Quaternion.Euler(_swordRotX, _swordRotY, _swordRot);
                MeshFilter mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = m;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                // 两面用物品贴图,四条边用深色 → 看起来是"一块有厚度的东西",不是贴纸
                Material side = new Material(_mcSwordMat);
                side.mainTexture = _texWhite;
                if (side.HasProperty("_Color")) side.color = new Color(0.20f, 0.20f, 0.24f, 1f);
                side.renderQueue = 3001;
                _mcSwordSideMat = side;
                mr.sharedMaterials = new Material[] { _mcSwordMat, side };
                MeshRenderer src = FirstSkeletonRenderer();
                if (src != null) { try { mr.sortingLayerName = src.sortingLayerName; mr.sortingOrder = src.sortingOrder + 8; } catch { } }
                _mcSwordMr = mr;
                Logger.LogInfo("[mc] 剑已挂上: " + file + " 长 " + len.ToString("F2"));
            }
            catch (Exception e) { Warn("MC/sword", e); }
        }

        // ================= 位置 / 朝向 =================
        private void SyncMcRoot()
        {
            try
            {
                if (_mcRoot == null) return;
                var p = R.Player;
                if (p == null || p.transform == null) return;
                Vector3 bp = p.transform.position;
                _mcRoot.transform.position = new Vector3(bp.x, bp.y + _mcFootDelta, 0f);
                float face = 1f;
                try { if (p.transform.localScale.x < 0f) face = -1f; } catch { }
                // 朝向两种做法(按 shader 能不能改剔除自动选):
                //   能改剔除(_Cull 存在,已设 Off)→ 用 scale.x = ±1 整体镜像,左右肢体互换,最像"转身";
                //   不能改剔除 → 不能镜像(绕序会反、模型会里外翻),改成"偏航角取反",看起来是转向。
                if (_mcCanCull)
                {
                    _mcRoot.transform.localScale = new Vector3(face, 1f, 1f);
                    _mcRoot.transform.rotation = Quaternion.Euler(0f, 180f + _blockyYaw, 0f);
                }
                else
                {
                    _mcRoot.transform.localScale = Vector3.one;
                    _mcRoot.transform.rotation = Quaternion.Euler(0f, 180f + _blockyYaw * face, 0f);
                }
                // ↑ 为什么要 +180°:Unity 的正交 2D 相机在 -Z 往 +Z 看,能看到的是盒子**朝 -Z 的那一面**;
                //   我原来把"正面(前脸)"贴到了 +Z 那一面 → 结果看到的是后脑勺。

                // 朝向两种做法(按 shader 能不能改剔除自动选):
            }
            catch { }
        }

        private MeshRenderer FirstSkeletonRenderer()
        {
            try
            {
                if (_skels.Count == 0) ScanSkeletons();
                for (int i = 0; i < _skels.Count; i++) if (_skels[i] != null && _skels[i].Mr != null) return _skels[i].Mr;
            }
            catch { }
            return null;
        }

        private void MeasurePlayer(out float height, out float footY, out float cx)
        {
            height = 2f; footY = 0f; cx = 0f;
            try
            {
                var p = R.Player;
                if (p != null && p.transform != null) { cx = p.transform.position.x; footY = p.transform.position.y; }
                MeshRenderer mr = FirstSkeletonRenderer();
                if (mr != null)
                {
                    Bounds b = mr.bounds;
                    if (b.size.y > 0.01f) { height = b.size.y; footY = b.min.y; cx = b.center.x; }
                }
            }
            catch { }
        }

        // ================= 程序化动画 =================
        private void AnimateParts(float now)
        {
            if (_mcParts[1] == null) return;
            _mcAnim = ReadAnimName();
            bool atk = IsAttack(_mcAnim);
            if (_mcAnim != _mcLastAnim)
            {
                if (atk) { _mcAttackT = 0f; _mcAttackKind = AttackKind(_mcAnim); }
                _mcLastAnim = _mcAnim;
            }
            if (_mcAttackT >= 0f) { _mcAttackT += Time.deltaTime; if (_mcAttackT > 0.55f) _mcAttackT = -1f; }

            float sw = _blockySwing;
            bool run = Has(_mcAnim, "run") || Has(_mcAnim, "dash");
            bool walk = run || Has(_mcAnim, "walk");
            bool air = Has(_mcAnim, "jump") || Has(_mcAnim, "fall");
            bool hit = Has(_mcAnim, "hit") || Has(_mcAnim, "flash");
            bool dead = Has(_mcAnim, "dead") || Has(_mcAnim, "die");

            float aArmR = 0f, aArmL = 0f, aLegR = 0f, aLegL = 0f, aBody = 0f, aHead = 0f, bob = 0f;

            if (dead)
            {
                aBody = -80f; aArmR = -20f; aArmL = -20f; aLegR = 15f; aLegL = -15f;
            }
            else if (air)
            {
                aArmR = -35f; aArmL = 35f; aLegR = 18f; aLegL = -18f;
            }
            else if (walk)
            {
                _mcAnimPhase += Time.deltaTime * (run ? 11f : 7f);
                float s = Mathf.Sin(_mcAnimPhase);
                float amp = (run ? 40f : 27f) * sw;
                aLegR = s * amp; aLegL = -s * amp;
                aArmR = -s * amp * 0.85f; aArmL = s * amp * 0.85f;
                bob = Mathf.Abs(Mathf.Cos(_mcAnimPhase)) * (run ? 0.035f : 0.02f) * _mcH * _blockyScale;
                aBody = run ? 6f : 2f;
            }
            else
            {
                _mcAnimPhase += Time.deltaTime * 1.6f;
                float idle = Mathf.Sin(_mcAnimPhase) * 3f * sw;
                aArmR = idle; aArmL = -idle;
                aBody = Mathf.Sin(_mcAnimPhase * 0.8f) * 1.2f;
                aHead = Mathf.Sin(_mcAnimPhase * 0.6f) * 2f;
            }

            // 攻击:按段位做一次挥砍(横挥 / 回手 / 上挑),用 easeOut 收尾
            if (_mcAttackT >= 0f)
            {
                float p = Mathf.Clamp01(_mcAttackT / 0.32f);
                float e = 1f - (1f - p) * (1f - p);
                float from, to;
                if (_mcAttackKind < 0.5f) { from = -75f; to = 95f; }          // 横挥
                else if (_mcAttackKind < 1.5f) { from = 85f; to = -95f; }     // 回手
                else { from = -115f; to = 55f; }                              // 上挑/重击
                aArmR = Mathf.Lerp(from, to, e) * sw;
                aBody = Mathf.Lerp(0f, (_mcAttackKind < 1.5f ? 10f : -12f), e);
            }

            if (hit) { aBody = -14f; aHead = 8f; aArmR = -25f; aArmL = 25f; }

            SetLocal(0, 0f, 0f, aHead);
            SetLocal(1, 0f, 0f, aBody);
            SetLocal(2, 0f, 0f, aArmR);
            SetLocal(3, 0f, 0f, aArmL);
            SetLocal(4, 0f, 0f, aLegR);
            SetLocal(5, 0f, 0f, aLegL);
            try { _mcParts[1].Node.transform.localPosition = new Vector3(0f, 12f * _mcD + bob, 0f); } catch { }
        }

        private void SetLocal(int i, float rx, float ry, float rz)
        {
            try
            {
                if (_mcParts[i] == null || _mcParts[i].Node == null) return;
                _mcParts[i].Node.transform.localRotation = Quaternion.Euler(rx, ry, rz);
            }
            catch { }
        }

        private static bool Has(string s, string key)
        { return !string.IsNullOrEmpty(s) && s.ToLower().IndexOf(key) >= 0; }

        private static bool IsAttack(string s)
        { return Has(s, "atk") || Has(s, "attack") || Has(s, "rising") || Has(s, "execute") || Has(s, "skill") || Has(s, "combo"); }

        // 攻击段位:Atk1/Atk2 → 0/1;重击·上挑·处决 → 2
        private static float AttackKind(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            string t = s.ToLower();
            if (Hit(t, "hv") || Hit(t, "rising") || Hit(t, "execute") || Hit(t, "spatk") || Hit(t, "skill")) return 2f;
            if (Hit(t, "2")) return 1f;
            return 0f;
        }
        private static bool Hit(string s, string k) { return s.IndexOf(k) >= 0; }

        // 当前动作名:骨架 state 优先,取不到再反射读游戏控制器
        private string ReadAnimName()
        {
            try
            {
                for (int i = 0; i < _skels.Count; i++)
                {
                    var sa = _skels[i] != null ? _skels[i].Comp as SkeletonAnimation : null;
                    if (sa == null || sa.state == null) continue;
                    var te = sa.state.GetCurrent(0);
                    if (te != null && te.Animation != null && !string.IsNullOrEmpty(te.Animation.Name)) return te.Animation.Name;
                }
            }
            catch { }
            try
            {
                if (_mcCtrl == null) FindAnimController();
                if (_mcCtrl != null && _fCtrlAnim != null) return (_fCtrlAnim.GetValue(_mcCtrl) as string) ?? "";
            }
            catch { }
            return "";
        }

        // 找到游戏自己的 MultiSpineAnimationController(不写游戏类型,靠类型名找)
        private void FindAnimController()
        {
            try
            {
                var p = R.Player;
                if (p == null || p.transform == null) return;
                Component[] all = p.transform.gameObject.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    if (all[i].GetType().Name == "MultiSpineAnimationController")
                    {
                        _mcCtrl = all[i];
                        if (_fCtrlAnim == null) _fCtrlAnim = all[i].GetType().GetField("currentAnim");
                        Logger.LogInfo("[mc] 找到动画控制器: " + all[i].GetType().Name + " (currentAnim 字段 " + (_fCtrlAnim != null ? "在" : "没找到") + ")");
                        return;
                    }
                }
            }
            catch { }
        }

        // ================= 素材 / shader =================
        private Texture2D LoadMcSkin(string path, ref Texture2D cache, string what)
        {
            if (cache != null) return cache;
            try
            {
                if (!File.Exists(path)) { Logger.LogWarning("[mc] 找不到" + what + ": " + path); return null; }
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool ok = false;
                try { ok = t.LoadImage(bytes, false); } catch (Exception e) { Warn("MC/load", e); }
                if (!ok || t.width <= 4) { Logger.LogWarning("[mc] " + what + "解码失败: " + path); try { Destroy(t); } catch { } return null; }
                t.name = "icey_mc_" + what;
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Point;     // 像素风
                FlipVertical(t);                     // 与配色替换同一个结论
                cache = t;
                Logger.LogInfo("[mc] " + what + "载入 " + path + " -> " + t.width + "x" + t.height);
                return t;
            }
            catch (Exception e) { Warn("MC/skin", e); return null; }
        }

        private Shader PickMcShader()
        {
            // 真 3D 盒子要"不透明 + 写深度"的 shader,否则背面会穿到正面。按顺序试。
            string[] opaque = { "Unlit/Texture", "Unlit/Color", "Mobile/Diffuse", "Legacy Shaders/Diffuse",
                                "Illumin-Diffuse", "Alpha-VertexLit" };
            string[] any = { "Sprites/Default", "Unlit/Transparent", "Unlit/Transparent Colored",
                             "Particles/Alpha Blended", "Spine/Skeleton_PS4", "Spine/Skeleton" };
            for (int i = 0; i < opaque.Length; i++)
            {
                Shader s = Find(opaque[i]);
                if (s != null) { Logger.LogInfo("[mc] shader 命中(不透明): " + opaque[i]); return s; }
            }
            for (int i = 0; i < any.Length; i++)
            {
                Shader s = Find(any[i]);
                if (s != null) { Logger.LogInfo("[mc] shader 命中(透明,可能出现背面穿透): " + any[i]); return s; }
            }
            try
            {
                MeshRenderer mr = FirstSkeletonRenderer();
                if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.shader != null)
                {
                    Logger.LogInfo("[mc] shader 兜底用主角材质: " + mr.sharedMaterial.shader.name);
                    return mr.sharedMaterial.shader;
                }
            }
            catch { }
            Logger.LogWarning("[mc] 一个可用 shader 都没找到");
            return null;
        }

        private static Shader Find(string name)
        {
            try { return Shader.Find(name); } catch { return null; }
        }

        // ================= 藏剑光/特效 =================
        // 教训:只扫主角子物体是不够的 —— 刀光这类特效是**场景里另外的实例**(不是主角的子物体)。
        // 现在:全场扫渲染器,按"离主角多近 + 名字/贴图关键词"筛;另外给一个"按大小列清单"的诊断,
        // 关键词没命中的也能靠大小一眼认出来。
        public float _vfxRadius = 20f;

        public void DumpSceneRenderers()
        {
            try
            {
                var p = R.Player;
                Vector3 pc = (p != null && p.transform != null) ? p.transform.position : Vector3.zero;
                UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(typeof(Renderer));
                List<Renderer> near = new List<Renderer>();
                for (int i = 0; i < all.Length; i++)
                {
                    Renderer r = all[i] as Renderer;
                    if (r == null) continue;
                    if (IsSkeletonRenderer(r)) continue;
                    Vector3 c = r.bounds.center;
                    if (Vector3.Distance(c, pc) > 40f) continue;
                    near.Add(r);
                }
                near.Sort(delegate (Renderer a, Renderer b) { return b.bounds.size.magnitude.CompareTo(a.bounds.size.magnitude); });
                Logger.LogInfo("[vfx] ---- 主角 40 米内的渲染器(按大小排,共 " + near.Count + ",只列前 40)----");
                for (int i = 0; i < near.Count && i < 40; i++)
                {
                    Renderer r = near[i];
                    string tex = "?", mat = "?";
                    try { if (r.sharedMaterial != null) { mat = r.sharedMaterial.name; if (r.sharedMaterial.mainTexture != null) tex = r.sharedMaterial.mainTexture.name; } } catch { }
                    Logger.LogInfo("[vfx] " + (i + 1) + ". " + Path0(r.gameObject) + "  大小 " + r.bounds.size.x.ToString("F1") + "x" + r.bounds.size.y.ToString("F1") +
                                   "  距 " + Vector3.Distance(r.bounds.center, pc).ToString("F1") + "  材质 " + mat + "  贴图 " + tex + "  启用 " + r.enabled);
                }
                Logger.LogInfo("[vfx] ---- 清单结束 ----");
                Toast("场景渲染器清单已写进日志(搜 [vfx])");
            }
            catch (Exception e) { Warn("MC/dump", e); }
        }

        private static string Path0(GameObject go)
        {
            try
            {
                string s = go.name;
                Transform t = go.transform.parent;
                int n = 0;
                while (t != null && n < 4) { s = t.name + "/" + s; t = t.parent; n++; }
                return s;
            }
            catch { return "?"; }
        }

        private void HideVfx()
        {
            try
            {
                var p = R.Player;
                if (p == null || p.transform == null) return;
                Vector3 pc = p.transform.position;
                UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(typeof(Renderer));
                int n = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    Renderer r = all[i] as Renderer;
                    if (r == null || !r.enabled) continue;
                    if (IsOwnModel(r)) continue;                             // 自己的方块人 / 剑
                    if (IsSkeletonRenderer(r)) continue;                     // 骨架由"藏原角色"那套管
                    if (_vfxOff.Contains(r)) continue;
                    if (Vector3.Distance(r.bounds.center, pc) > _vfxRadius) continue;
                    if (!LooksLikeVfx(r, UnderPlayer(r, pc))) continue;
                    _vfxOff.Add(r);
                    r.enabled = false;
                    n++;
                    Logger.LogInfo("[vfx] 藏掉: " + Path0(r.gameObject) + " 贴图 " + SafeTex(r));
                }
                if (n > 0) Logger.LogInfo("[mc] 本轮藏掉特效渲染器 " + n + " 个(累计 " + _vfxOff.Count + ")");
            }
            catch (Exception e) { Warn("MC/vfx", e); }        }

        private static string SafeTex(Renderer r)
        {
            try { if (r.sharedMaterial != null && r.sharedMaterial.mainTexture != null) return r.sharedMaterial.mainTexture.name; } catch { }
            return "?";
        }

        // 自动记录"此刻主角 10 米内最大的几个渲染器",集合变了才写一行 —— 这样主人只要正常玩,
        // 日志里就会留下刀光的真身(不用他专门去点按钮)。
        private float _mcNextAuto;
        private string _mcLastDump = "";

        private void AutoDumpNearby()
        {
            try
            {
                var p = R.Player;
                if (p == null || p.transform == null) return;
                Vector3 pc = p.transform.position;
                UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(typeof(Renderer));
                List<Renderer> near = new List<Renderer>();
                for (int i = 0; i < all.Length; i++)
                {
                    Renderer r = all[i] as Renderer;
                    if (r == null || !r.enabled) continue;
                    if (IsOwnModel(r)) continue;                            // 我们自己的方块人,跳过
                    if (IsSkeletonRenderer(r)) continue;
                    if (Vector3.Distance(r.bounds.center, pc) > 10f) continue;
                    near.Add(r);
                }
                near.Sort(delegate (Renderer a, Renderer b) { return b.bounds.size.magnitude.CompareTo(a.bounds.size.magnitude); });
                string sig = "";
                for (int i = 0; i < near.Count && i < 8; i++) sig += Path0(near[i].gameObject) + ";" + SafeTex(near[i]) + "|";
                if (sig == _mcLastDump || sig.Length == 0) return;
                _mcLastDump = sig;
                for (int i = 0; i < near.Count && i < 8; i++)
                {
                    Renderer r = near[i];
                    Logger.LogInfo("[vfx] 附近大物件 " + (i + 1) + ": " + Path0(r.gameObject) +
                                   "  大小 " + r.bounds.size.x.ToString("F1") + "x" + r.bounds.size.y.ToString("F1") +
                                   "  距 " + Vector3.Distance(r.bounds.center, pc).ToString("F1") + "  贴图 " + SafeTex(r) +
                                   "  组件 " + Comps0(r));
                }
            }
            catch { }
        }

        // ================= 去剑光(独立开关;不需要开方块人也能用)=================
        // 教训:"去剑光"原来和「MC 攻击方式」绑在一个开关里,而那个开关默认关 → 补丁从来没装,
        // 日志里 `[mcatk]` 一行都没有。现在拆成独立开关,默认开。
        public void SetNoSlash(bool v)
        {
            _mcNoSlash = v;
            McNoAtkEffectPatch.Enabled = v;     // 顺手也把"生成特效"的方法拦掉(命中就是赚到)
            if (v) { InstallMcPatches(); TickNoSlash(); Toast("去剑光:开(持续清理新生成的特效)"); }
            else { RestoreVfx(); Toast("去剑光:关(特效恢复)"); }
            Save();
        }

        private float _mcNextNoSlash;

        // 只做"周期性清理",和方块人开不开无关
        private void TickNoSlash()
        {
            try
            {
                float now = Time.unscaledTime;
                if (now >= _mcNextNoSlash && _mcNoSlash) { _mcNextNoSlash = now + 1f; HideVfx(); }   // 1 秒一次(已搁置:默认不开就不跑)
            }
            catch { }
        }

        private void RestoreVfx()
        {
            try
            {
                for (int i = 0; i < _vfxOff.Count; i++) if (_vfxOff[i] != null) _vfxOff[i].enabled = true;
                Logger.LogInfo("[mc] 还原特效渲染器 " + _vfxOff.Count + " 个");
                _vfxOff.Clear();
            }
            catch { }
        }

        private bool IsSkeletonRenderer(Renderer r)
        {
            try
            {
                for (int i = 0; i < _skels.Count; i++) if (_skels[i] != null && _skels[i].Mr == r) return true;
            }
            catch { }
            return false;
        }

        // underPlayer = 这个渲染器挂在主角层级下(通过"离主角很近 + 名字里没有场景前缀"近似判断)
        private static bool LooksLikeVfx(Renderer r, bool underPlayer)
        {
            try
            {
                string s = BuildName(r);
                for (int i = 0; i < VfxKeys.Length; i++) if (s.IndexOf(VfxKeys[i]) >= 0)
                {
                    // 通用词必须"挂在主角身上"才敢藏(否则会误伤场景装饰,踩过一次)
                    bool strong = false;
                    for (int k = 0; k < VfxStrong.Length; k++) if (s.IndexOf(VfxStrong[k]) >= 0) { strong = true; break; }
                    return strong || underPlayer;
                }
            }
            catch { }
            return false;
        }

        // 近似判断"是不是挂在主角下面":直接看层级(父子链里有没有主角的 GameObject)
        private bool UnderPlayer(Renderer r, Vector3 pc)
        {
            try
            {
                if (IsOwnModel(r)) return false;      // 别去动我们自己的方块人
                var p = R.Player;
                if (p == null || p.transform == null) return false;
                Transform pt = p.transform;
                Transform t = r.transform;
                int guard = 0;
                while (t != null && guard++ < 8) { if (t == pt) return true; t = t.parent; }
            }
            catch { }
            return false;
        }

        // 是不是我们自己建的方块人(名字里带 IceyMcModel)
        private bool IsOwnModel(Renderer r)
        {
            try
            {
                Transform t = r.transform;
                int guard = 0;
                while (t != null && guard++ < 6) { if (t.name == "IceyMcModel") return true; t = t.parent; }
            }
            catch { }
            return false;
        }

        private static string BuildName(Renderer r)
        {
            string s = "";
            try
            {
                Material m = r.sharedMaterial;
                if (m != null)
                {
                    s = (m.name ?? "") + "|" + (m.shader != null ? m.shader.name : "");
                    if (m.mainTexture != null) s += "|" + m.mainTexture.name;
                }
                s += "|" + r.gameObject.name + "|" + Path0(r.gameObject);
                // ★ 组件类型名才是关键判据(LineInScene / FullScreenColorMaskController 这类)
                try
                {
                    Component[] cs = r.gameObject.GetComponents<Component>();
                    for (int i = 0; i < cs.Length; i++) if (cs[i] != null) s += "|" + cs[i].GetType().Name;
                }
                catch { }
            }
            catch { }
            return s.ToLower();
        }

        // 给诊断用:把这个渲染器身上所有组件名列出来
        private static string Comps0(Renderer r)
        {
            string s = "";
            try
            {
                Component[] cs = r.gameObject.GetComponents<Component>();
                for (int i = 0; i < cs.Length; i++) { if (cs[i] == null) continue; s += cs[i].GetType().Name; if (i < cs.Length - 1) s += ","; }
            }
            catch { }
            return s;
        }
    }
}
