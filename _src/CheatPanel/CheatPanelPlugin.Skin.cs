// ICEY Cheat Panel - 运行时配色热切换(任务 A②)。partial class 的第三个文件。
//
// 五件事必须说清楚,否则下一个改这块的人一定会踩:
//
// 1) 为什么改"材质对象"而不是 renderer.material
//    SkeletonRenderer.UpdateMesh() 每帧都会把 meshRenderer.sharedMaterials 重新赋值成
//    "当前可见附件用到的那几个图集页材质"(IL 见 SkeletonRenderer::LateUpdate @ 113163-113184)。
//    那些材质是**图集素材的 Material 对象**(不是每帧 new 的实例)。所以:
//      * renderer.material(实例材质)会被每帧丢弃 —— 必须改材质对象本身的属性;
//      * 凡是用同一张图集材质的渲染器都会被一起改到(不止主角)—— 设计取舍,不是 bug。
//
// 2) 贴图可能不在材质上,而在 **per-renderer 的 MaterialPropertyBlock** 里
//    特效/光效层常用 property block 覆盖贴图(这样不用给每个实例建材质)。只读 mat.GetTexture
//    会完全看不到那层 —— 实机日志就有这个现象:扫描只命中 6 张基础图集,颜色是青色的
//    PlayerFlash 光效层一处都没匹配到。所以扫描与写入都要同时支持"材质属性"和"property block"两条路,
//    SkinEntry.ViaBlock 记录这处贴图是从哪来的,还原也必须写回同一个地方。
//
// 3) 为什么用"贴图名"匹配而不是按骨架槽位匹配
//    6 个槽位是 private 字段(normalSkeleton / atkSkeleton / ...,类型 SkeletonAnimation),
//    而且槽位 GameObject 会按需激活/创建。改成遍历渲染器 → 每处贴图来源,
//    用贴图 **名字** 去撞目标名单(NewPlayerNormal / NewPlayerAtkHV / ..._Illum / PlayerFlash)。
//    代价:换上去的贴图 **必须 named 成同一个名字**,否则下一次扫描就认不出这处(见 GetSkinTexture)。
//    另一条:场景扫描用 `FindObjectsOfType(typeof(Renderer))`,只看 MeshRenderer/SpriteRenderer
//    会漏掉粒子/拖尾/线渲染器(光效很可能就是那些)。
//
// 4) 扫描只负责"发现",发现过就永久记住(_skinKnown)+ 每帧低成本续写(KeepKnownWrites)
//    Spine 的 sharedMaterials 是按**当前可见附件**重建的:自发光页(_Illum)、闪光层
//    只在对应动画帧才出现在渲染器上。靠"几秒扫一次"去逮闪光那 0.3 秒是不可靠的。所以:
//      * _skinKnown 是**累积**的(来源被销毁才剔除),不再每轮 Clear;
//      * 每帧对已知来源做一次"该挂的贴图是不是我们的,不是就写回去";
//      * 于是只要出现过一次被抓到,以后每一帧都是对的。
//
// 5) 还原的唯一权威是 **写入台账 _skinWrites**(三轮审查抓出来的,别退化)
//    扫描看不到"非激活 + 不在玩家层级"的渲染器(FindObjectsOfType 不返回未激活对象),所以**不能拿
//    扫描结果当"我们写过哪些"的清单** —— 漏一处就会把还在用的贴图 Destroy 掉 → 指向已销毁贴图
//    → 角色糊块,而且死引用会被扫描跳过,再也修不回来。规则:
//      * 每次真的把我们的贴图写进去,就记一条 _skinWrites(按 来源|属性 去重);
//      * 原贴图按 **(来源 instanceID)|(属性名)** 精确记在 _skinOrig 里(按名字的 _skinOrigFallback 只兜底);
//      * Destroy 之前拿**台账**逐条复核:读不出当前贴图、写不回原图、或原图查不到而当前还挂着我们的贴图
//        → 一律 bad++,**整批不销毁**并请求重扫;
//      * 因此不会出现"贴图被销毁但还在用"。
//    全流程 try/catch 吞异常;状态行与日志如实报"未还原/未命中/尺寸不符"。

using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace IceyCheatPanel
{
    public partial class CheatPanelPlugin
    {
        // 与 _tools\atlas_tool.py 的 TARGETS 保持一致(11 张主角贴图)
        public static readonly string[] SkinTargets = {
            "NewPlayerNormal", "NewPlayerAtk", "NewPlayerAtkHV", "NewPlayerSpAtk",
            "NewPlayerUnderAtk", "NewPlayerAtkUpRising",
            "NewPlayerAtk_Illum", "NewPlayerNormal_Illum", "NewPlayerSPAtk_Illum",
            "NewPlayerAtkUpRising_Illum", "PlayerFlash"
        };

        // 只知道这几个常用的贴图属性名:Unity 5.4 **没有** Material.GetTexturePropertyNames(),
        // 也没有 Shader.GetPropertyCount/GetPropertyName(都核对过 UnityEngine.il,0 命中),
        // 所以只能硬列候选属性名;先 HasProperty 再 GetTexture(打错名会刷日志错误)。
        private static readonly string[] SkinTexProps = {
            "_MainTex", "_Illum", "_IllumTex", "_IllumTexture", "_EmissionMap",
            "_BumpMap", "_DetailTex", "_AlphaTex", "_MaskTex", "_Tex", "_SecondTex"
        };

        public bool _skinOn;
        public int _skinIdx;
        public string _skinStatus = "未扫描";
        public string[] SkinPresets = new string[] { "原色" };
        // 运行时贴图**必须**上下翻一次(实机 + .atlas 区域坐标双重验证过的结论):
        // 导出的 PNG 是"第一行在上",而游戏原贴图的数据按 Unity 约定是"第一行在 V=0(下)",
        // 两者相反。LoadImage 按 PNG 约定摆像素 → 不翻的话所有 UV 都会采样到上下镜像的区域,
        // 实机现象就是"只剩几块残缺的贴图,身体整片不见"。
        // 证据:用 NewPlayerNormal.atlas 的 120 个区域坐标去量导出的 PNG ——
        //   不翻:只有 24 个区域有内容、78 个空白;翻过来:**109 个有内容、0 个空白**
        //   (NewPlayerAtk 55/113、NewPlayerSpAtk 61/119,三张一致)
        // 静态那条路(atlas_tool apply 写 resS)不受影响:它按原来的行序编码回去,方向天然是对的。
        public bool _skinFlip = true;
        // 虚拟预设(不对应磁盘目录):把主角身上的贴图全换成 1×1 全透明 → "null 形态 / 把东西全删了"。
        // 放在预设列表**最后**,这样已有的 skini 配置索引含义不变。
        public const string NullPresetName = "隐形(null)";

        // 一处"贴图来源":可能是材质的某个属性,也可能是某个渲染器的 property block 覆盖
        private sealed class SkinEntry
        {
            public Material Mat;        // ViaBlock=false 时用
            public Renderer Rnd;        // ViaBlock=true 时用
            public string Prop;
            public string Key;
            public bool ViaBlock;
            public string EK;           // 创建时定下的唯一键,免得之后对象被销毁读不出 instanceID
        }

        // "我们把贴图写进了哪里" —— 还原权威,和扫描结果无关(见文件头第 5 条)
        private sealed class SkinWrite
        {
            public Material Mat;
            public Renderer Rnd;
            public string Prop;
            public string Key;
            public bool ViaBlock;
            public string EK;
        }

        // **累积**的已知目标来源(EK → entry);来源被销毁才剔除。扫描只负责往里加,不清空。
        private readonly Dictionary<string, SkinEntry> _skinKnown = new Dictionary<string, SkinEntry>();
        private readonly List<SkinWrite> _skinWrites = new List<SkinWrite>();
        private readonly Dictionary<string, Texture2D> _skinTex = new Dictionary<string, Texture2D>();
        // 当前预设下"每个贴图名该挂我们的哪张贴图"(只放**我们的**贴图;原色/缺图时不放)→ 供每帧续写
        private readonly Dictionary<string, Texture2D> _skinWant = new Dictionary<string, Texture2D>();
        private readonly HashSet<string> _skinNoFile = new HashSet<string>();
        private readonly HashSet<string> _skinSizeWarn = new HashSet<string>();
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();   // 复用,避免每帧分配

        // "(来源 instanceID)|(属性名)" → 这处的原贴图。**还原的唯一权威来源。**
        private readonly Dictionary<string, Texture> _skinOrig = new Dictionary<string, Texture>();
        // 贴图名 → 原贴图。只当"精确记录失效时"的兜底,不是权威(同名可能是两个不同对象)。
        private readonly Dictionary<string, Texture> _skinOrigFallback = new Dictionary<string, Texture>();
        // 我们造出来的贴图的 instanceID。**故意永不清理**:贴图被 Destroy 之后还要靠它认出
        // "这处挂的是我们的贴图",否则会把我们的贴图误记成原贴图 → 永久还原不回去。
        private readonly HashSet<int> _skinOurs = new HashSet<int>();

        private bool _skinScanned, _skinDirty = true;
        private float _skinNextScan;
        private int _skinLastPlayerTid = -1;
        private int _skinRenderers, _skinMats, _skinHits;
        private int _skinFutile, _skinErrLogged, _skinStuck;
        private int _skinDumpedForTid = -99999;      // 玩家实例变一次就自动扒一次玩家层级的渲染器
        private string _skinLogSig = "";
        private string _skinWantPreset = "\u0001";        // 哨兵:与任何真实预设名都不同 → 首次必定重建

        public string SkinRoot { get { return Path.Combine(Paths.ConfigPath, "player_skin"); } }

        private string CurrentSkinPreset
        {
            get
            {
                if (_skinIdx <= 0 || SkinPresets == null || _skinIdx >= SkinPresets.Length) return null;
                return SkinPresets[_skinIdx];
            }
        }

        // 预设 = BepInEx\config\player_skin\ 下的子目录名(以下划线开头的不算,留给临时目录)
        public void RefreshSkinPresets()
        {
            try
            {
                List<string> l = new List<string>();
                l.Add("原色");
                string dir = SkinRoot;
                if (Directory.Exists(dir))
                {
                    string[] subs = Directory.GetDirectories(dir);
                    Array.Sort(subs, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < subs.Length; i++)
                    {
                        string n = Path.GetFileName(subs[i]);
                        if (!string.IsNullOrEmpty(n) && n[0] != '_') l.Add(n);
                    }
                }
                l.Add(NullPresetName);     // 虚拟预设(无目录):全透明"隐形"形态
                SkinPresets = l.ToArray();
                if (_skinIdx < 0 || _skinIdx >= SkinPresets.Length) _skinIdx = 0;
                Logger.LogInfo("[skin] PNG 目录: " + SkinRoot + "  (预设 " + (SkinPresets.Length - 2) + " 套 + 原色 + 隐形)");
            }
            catch (Exception e) { Warn("Skin/presets", e); }
        }

        // ================= 来源读写(材质 / property block 两条路) =================
        private static string SourceKey(Renderer r, Material m, string prop, bool viaBlock)
        {
            if (viaBlock) return "B" + r.GetInstanceID().ToString() + "|" + prop;
            return "M" + m.GetInstanceID().ToString() + "|" + prop;
        }

        private Texture GetSourceTexture(Renderer r, Material m, string prop, bool viaBlock)
        {
            if (viaBlock)
            {
                _mpb.Clear();
                r.GetPropertyBlock(_mpb);
                return _mpb.GetTexture(prop);
            }
            return m.GetTexture(prop);
        }

        private void SetSourceTexture(Renderer r, Material m, string prop, bool viaBlock, Texture t)
        {
            if (viaBlock)
            {
                _mpb.Clear();
                r.GetPropertyBlock(_mpb);
                _mpb.SetTexture(prop, t);
                r.SetPropertyBlock(_mpb);
                return;
            }
            m.SetTexture(prop, t);
        }

        private static bool EntryAlive(SkinEntry e)
        {
            if (e == null) return false;
            if (e.ViaBlock) return e.Rnd != null;
            return e.Mat != null;
        }

        private Texture ReadEntry(SkinEntry e)
        {
            return GetSourceTexture(e.Rnd, e.Mat, e.Prop, e.ViaBlock);
        }

        private void WriteEntry(SkinEntry e, Texture t)
        {
            SetSourceTexture(e.Rnd, e.Mat, e.Prop, e.ViaBlock, t);
        }

        // ================= 原贴图的记账 =================
        private Texture FallbackFor(string key)
        {
            Texture o;
            if (_skinOrigFallback.TryGetValue(key, out o) && o != null) return o;
            if (_skinOrigFallback.ContainsKey(key)) _skinOrigFallback.Remove(key);
            return null;
        }

        // 这处的原贴图。拿不到就返回 null(调用方必须当作"不能写、也不能销毁我们的贴图")
        private Texture OrigFor(SkinEntry e)
        {
            if (!EntryAlive(e)) return null;
            Texture o;
            if (_skinOrig.TryGetValue(e.EK, out o))
            {
                if (o != null) return o;
                _skinOrig.Remove(e.EK);           // 假 null = 对象被销毁了,剔除死引用
                Logger.LogWarning("[skin] " + e.Key + " 的原贴图记录已失效,已剔除并尝试兜底");
            }
            o = FallbackFor(e.Key);
            if (o != null) { _skinOrig[e.EK] = o; return o; }
            return null;
        }

        // ================= 扫描(只负责发现) =================
        private static void CollectRenderers(GameObject go, List<Renderer> outList)
        {
            if (go == null) return;
            try
            {
                Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);   // true = 含未激活的子物体
                if (rs == null) return;
                for (int i = 0; i < rs.Length; i++) if (rs[i] != null) outList.Add(rs[i]);
            }
            catch { }
        }

        // 登记一处贴图来源(材质属性或 property block)
        private void ConsiderSource(Renderer r, Material m, string prop, bool viaBlock, Texture tex,
                                    string ek, HashSet<string> seenP, List<string> other,
                                    List<string> unknownOrig, ref int discovered)
        {
            if (tex == null) return;
            string key = tex.name;
            if (Array.IndexOf(SkinTargets, key) < 0)
            {
                if (other.Count < 12 && !other.Contains(key)) other.Add(key);
                return;
            }
            if (!seenP.Add(ek)) return;

            SkinEntry e = new SkinEntry();
            e.Mat = m; e.Rnd = r; e.Prop = prop; e.Key = key; e.ViaBlock = viaBlock; e.EK = ek;

            // 登记原贴图:只认"不是我们造的"贴图
            Texture known;
            if (!_skinOrig.TryGetValue(ek, out known) || known == null)
            {
                if (!IsOurs(tex))
                {
                    _skinOrig[ek] = tex;
                    // 兜底只记"第一个"健康的同名原图(后写覆盖会在同名多对象时还原成别人那份)
                    Texture fb;
                    if (!_skinOrigFallback.TryGetValue(key, out fb) || fb == null)
                        _skinOrigFallback[key] = tex;
                }
                else if (OrigFor(e) == null && !unknownOrig.Contains(key))
                {
                    // 挂着我们的贴图却查不到原图:保留 entry(继续尝试还原),但不写、且禁止销毁我们的贴图
                    unknownOrig.Add(key);
                }
            }

            if (!_skinKnown.ContainsKey(ek)) discovered++;
            _skinKnown[ek] = e;
        }

        public void ScanSkinTargets()
        {
            int prevKnown = _skinKnown.Count;
            int discovered = 0;
            _skinRenderers = 0; _skinMats = 0;
            List<string> other = new List<string>();
            HashSet<int> seenR = new HashSet<int>();
            HashSet<string> seenP = new HashSet<string>();
            List<string> unknownOrig = new List<string>();
            try
            {
                List<Renderer> rs = new List<Renderer>();
                // R.Player 是 GameWorld.PlayerManager —— **不是** UnityEngine.Object(没有 GetInstanceID)。
                // 所以一律从它的 transform 拿 GameObject。这是带 includeInactive 的主力扫描。
                try { var p = R.Player; if (p != null && p.transform != null) CollectRenderers(p.transform.gameObject, rs); }
                catch (Exception e) { Warn("Skin/player", e); }

                // 玩家层级之外兜底。用 Renderer 基类:只看 MeshRenderer/SpriteRenderer 会漏掉
                // 粒子/拖尾/线渲染器 —— 光效层很可能就是那些。
                try
                {
                    UnityEngine.Object[] a = UnityEngine.Object.FindObjectsOfType(typeof(Renderer));
                    for (int i = 0; i < a.Length; i++) if (a[i] != null) rs.Add((Renderer)a[i]);
                }
                catch (Exception e) { Warn("Skin/find", e); }

                for (int i = 0; i < rs.Count; i++)
                {
                    Renderer r = rs[i];
                    if (r == null) continue;
                    if (!seenR.Add(r.GetInstanceID())) continue;
                    _skinRenderers++;

                    Material[] mats = null;
                    try { mats = r.sharedMaterials; } catch { }
                    if (mats != null)
                    {
                        for (int m = 0; m < mats.Length; m++)
                        {
                            Material mat = mats[m];
                            if (mat == null) continue;
                            _skinMats++;
                            // 一个材质整段包 try:材质/贴图随时可能被销毁,读属性会抛 MissingReferenceException,
                            // 不能让一处异常把整轮扫描废掉(否则 _skinScanned 一直 false → 每帧重扫)
                            try
                            {
                                for (int t = 0; t < SkinTexProps.Length; t++)
                                {
                                    string prop = SkinTexProps[t];
                                    if (!mat.HasProperty(prop)) continue;
                                    Texture tex = mat.GetTexture(prop);
                                    if (tex == null) continue;
                                    ConsiderSource(r, mat, prop, false, tex,
                                                   SourceKey(r, mat, prop, false), seenP, other, unknownOrig, ref discovered);
                                }
                            }
                            catch (Exception ex) { Warn("Skin/mat " + SafeName(mat), ex); }
                        }
                    }

                    // per-renderer property block(特效常用;材质上完全看不到)
                    try
                    {
                        _mpb.Clear();
                        r.GetPropertyBlock(_mpb);
                        for (int t = 0; t < SkinTexProps.Length; t++)
                        {
                            string prop = SkinTexProps[t];
                            Texture tex = _mpb.GetTexture(prop);
                            if (tex == null) continue;
                            ConsiderSource(r, null, prop, true, tex,
                                           SourceKey(r, null, prop, true), seenP, other, unknownOrig, ref discovered);
                        }
                    }
                    catch (Exception ex) { Warn("Skin/block " + SafeName2(r), ex); }
                }

                // 顺手剔除已销毁的来源(累积集合必须能收敛)
                if (_skinKnown.Count > 0)
                {
                    List<string> dead = null;
                    foreach (KeyValuePair<string, SkinEntry> kv in _skinKnown)
                        if (!EntryAlive(kv.Value)) { if (dead == null) dead = new List<string>(); dead.Add(kv.Key); }
                    if (dead != null) for (int i = 0; i < dead.Count; i++) _skinKnown.Remove(dead[i]);
                }
                _skinHits = _skinKnown.Count;
            }
            catch (Exception e) { Warn("Skin/scan", e); }
            finally
            {
                // 一定要落 true:否则异常路径会让 UpdateSkin 每帧重扫
                _skinScanned = true;
                string sig = _skinRenderers + "/" + _skinMats + "/" + _skinHits + "/" + discovered + "/" + unknownOrig.Count;
                if (sig != _skinLogSig || _skinHits == 0 || _skinHits != prevKnown)
                {
                    _skinLogSig = sig;
                    Logger.LogInfo("[skin] 扫描: 渲染器 " + _skinRenderers + " / 材质 " + _skinMats +
                                   " / 已知目标来源 " + _skinHits + " 处(本轮新发现 " + discovered + ")");
                    foreach (KeyValuePair<string, SkinEntry> kv in _skinKnown)
                    {
                        SkinEntry e = kv.Value;
                        Logger.LogInfo("[skin]   " + e.Key + "  <-  " +
                                       (e.ViaBlock ? ("[block] " + SafeName2(e.Rnd)) : SafeName(e.Mat)) + " ." + e.Prop);
                    }
                    if (other.Count > 0)
                        Logger.LogInfo("[skin] 没对上的贴图名(前 12 个): " + string.Join(", ", other.ToArray()));
                    if (unknownOrig.Count > 0)
                        Logger.LogWarning("[skin] 这些贴图查不到原图,已跳过写入(也不会销毁我们的贴图): " +
                                          string.Join(", ", unknownOrig.ToArray()));
                }
            }
        }

        private static string SafeName(Material m)
        {
            try { return m != null ? m.name : "(已销毁材质)"; } catch { return "(材质名读不出)"; }
        }

        private static string SafeName2(Renderer r)
        {
            try { return r != null ? (r.gameObject.name + "/" + r.GetType().Name) : "(已销毁渲染器)"; }
            catch { return "(渲染器名读不出)"; }
        }

        // ================= 贴图装载 =================
        private bool IsOurs(Texture t)
        {
            if (t == null) return false;
            try
            {
                if (_skinOurs.Contains(t.GetInstanceID())) return true;
                foreach (KeyValuePair<string, Texture2D> kv in _skinTex)
                    if (object.ReferenceEquals(kv.Value, t)) return true;
            }
            catch { }
            return false;
        }

        // 运行时必须做的"上下翻"(见 _skinFlip 的注释)。用 raw 数据翻,16MB 一份,比 GetPixels 省 4 倍。
        private void FlipVertical(Texture2D t)
        {
            int w = t.width, h = t.height;
            if (t.format != TextureFormat.ARGB32 && t.format != TextureFormat.RGBA32)
            {
                Logger.LogWarning("[skin] 贴图格式 " + t.format + " 不是 32 位,跳过上下翻(方向可能不对)");
                return;
            }
            int stride = w * 4;
            byte[] src = t.GetRawTextureData();
            if (src == null || src.Length < stride * h) { Logger.LogWarning("[skin] raw 数据读不到,跳过上下翻"); return; }
            byte[] dst = new byte[stride * h];
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(src, y * stride, dst, (h - 1 - y) * stride, stride);
            t.LoadRawTextureData(dst);
            t.Apply(false, false);
        }

        private Texture2D GetSkinTexture(string preset, string key)
        {
            string ck = preset + "/" + key;
            Texture2D cached;
            if (_skinTex.TryGetValue(ck, out cached)) return cached;
            if (_skinNoFile.Contains(ck)) return null;
            try
            {
                string p = Path.Combine(Path.Combine(SkinRoot, preset), key + ".png");
                if (!File.Exists(p)) { _skinNoFile.Add(ck); return null; }

                byte[] bytes = File.ReadAllBytes(p);
                // mipChain = false:游戏自己的 6 张基础图集槽位**本来就没有 mip 链**
                // (NewPlayerNormal 槽位 4194304 = 2048×2048×1,正好基础层),所以不生成 mip 才是"和原版一样",
                // 还能省 1/3 显存。要改的话这里换成 true 即可。
                Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool ok = false;
                try
                {
                    // 第二参 markNonReadable 传 **false**:保持可读。
                    // 传 true 省一半内存,但 Unity 5.4 上这条路出过"上传后内容不对"的怪事,
                    // 而我们能靠"可读"来做下面的采样自检(把采样值写进日志,和 PNG 原值比对)。
                    ok = t.LoadImage(bytes, false);
                }
                catch (Exception e) { Warn("Skin/load " + ck, e); }

                if (!ok || t.width <= 8)
                {
                    Logger.LogWarning("[skin] 解码失败,跳过: " + p);
                    try { Destroy(t); } catch { }
                    _skinNoFile.Add(ck);                     // 只有"解码失败"才记黑名单
                    return null;
                }
                // 名字必须与原贴图一致!否则下次扫描认不出这处,关不掉(见文件头注释)
                t.name = key;
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Bilinear;
                if (_skinFlip) FlipVertical(t);
                _skinTex[ck] = t;
                _skinOurs.Add(t.GetInstanceID());
                Logger.LogInfo("[skin] 载入 " + p + " -> " + t.width + "x" + t.height + " fmt=" + t.format +
                               " mip=" + t.mipmapCount + (_skinFlip ? " 已上下翻" : " 未翻") +
                               " 采样:" + SamplePixels(t));
                return t;
            }
            catch (Exception e)
            {
                // 瞬时 IO 失败不要进黑名单,下次还有机会(否则要用户点"重新读取 PNG"才能恢复)
                Warn("Skin/get " + ck, e);
                return null;
            }
        }

        // 采样自检:Unity 的 GetPixel 原点在**左下**,PNG 原点在左上(比对时 y 要翻)
        private static string SamplePixels(Texture2D t)
        {
            string s = "";
            try
            {
                int w = t.width, h = t.height;
                int[] xs = { 8, w / 4, w / 2, w - 9 };
                int[] ys = { 8, h / 3, h / 2, h - 9 };
                for (int i = 0; i < xs.Length; i++)
                {
                    int x = Mathf.Clamp(xs[i], 0, w - 1), y = Mathf.Clamp(ys[i], 0, h - 1);
                    Color c = t.GetPixel(x, y);
                    s += " (" + x + "," + y + ")=" + Mathf.RoundToInt(c.r * 255f) + "," + Mathf.RoundToInt(c.g * 255f) +
                         "," + Mathf.RoundToInt(c.b * 255f) + "," + Mathf.RoundToInt(c.a * 255f);
                }
            }
            catch (Exception e) { s = " 采样失败:" + e.Message; }
            return s;
        }

        private Texture2D GetNullTexture(string key)
        {
            string ck = "null/" + key;
            Texture2D cached;
            if (_skinTex.TryGetValue(ck, out cached)) return cached;
            try
            {
                Texture2D t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                t.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                t.Apply();
                // 名字必须和目标贴图一致,否则下一轮扫描认不出这处(见文件头不变式)
                t.name = key;
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Bilinear;
                _skinTex[ck] = t;
                _skinOurs.Add(t.GetInstanceID());
                Logger.LogInfo("[skin] 隐形贴图(key=" + key + ")");
                return t;
            }
            catch (Exception e) { Warn("Skin/null", e); return null; }
        }

        private void RememberWrite(SkinEntry e)
        {
            for (int i = 0; i < _skinWrites.Count; i++)
                if (_skinWrites[i].EK == e.EK) return;
            SkinWrite w = new SkinWrite();
            w.Mat = e.Mat; w.Rnd = e.Rnd; w.Prop = e.Prop; w.Key = e.Key; w.ViaBlock = e.ViaBlock; w.EK = e.EK;
            _skinWrites.Add(w);
        }

        private Texture ReadWriteSource(SkinWrite w)
        {
            return GetSourceTexture(w.Rnd, w.Mat, w.Prop, w.ViaBlock);
        }

        private void WriteWriteSource(SkinWrite w, Texture t)
        {
            SetSourceTexture(w.Rnd, w.Mat, w.Prop, w.ViaBlock, t);
        }

        private static bool WriteAlive(SkinWrite w)
        {
            if (w == null) return false;
            if (w.ViaBlock) return w.Rnd != null;
            return w.Mat != null;
        }

        private void FreeSkinTextures()
        {
            try
            {
                // ---- 以"写入台账"为准逐条还原(扫描结果可能已经不含这些来源,不能靠它) ----
                int bad = 0;
                for (int i = _skinWrites.Count - 1; i >= 0; i--)
                {
                    SkinWrite w = _skinWrites[i];
                    if (!WriteAlive(w)) { _skinWrites.RemoveAt(i); continue; }   // 来源本身没了,不用管

                    Texture orig;
                    if (!_skinOrig.TryGetValue(w.EK, out orig) || orig == null) orig = FallbackFor(w.Key);

                    Texture cur = null; bool got = false;
                    try { cur = ReadWriteSource(w); got = true; }
                    catch (Exception ex) { Warn("Skin/free-get", ex); }

                    if (!got) { bad++; continue; }        // 读不出来就不敢销毁
                    if (orig == null)
                    {
                        if (IsOurs(cur)) bad++;           // 还挂着我们的贴图又不知道原图 → 不能销毁
                        else _skinWrites.RemoveAt(i);     // 当前不是我们的贴图,没我们的事
                        continue;
                    }
                    try
                    {
                        if (cur != orig) WriteWriteSource(w, orig);
                        _skinWrites.RemoveAt(i);
                    }
                    catch (Exception ex) { Warn("Skin/free-restore", ex); bad++; }
                }

                if (bad > 0)
                {
                    // 宁可多占点显存,也不能让贴图被销毁了还在用(那是糊块,而且扫描跳过死引用后修不回来)
                    _skinStuck = bad;
                    _skinStatus = _skinOn
                        ? _skinStatus + "(" + bad + " 处没还原,看日志)"
                        : "已关闭,但 " + bad + " 处没还原(看日志 [skin])";
                    Logger.LogWarning("[skin] 有 " + bad + " 处确认不了原贴图 → **不销毁**我们的贴图,已请求重新扫描");
                    _skinScanned = false;
                    _skinNextScan = 0f;
                    _skinDirty = _skinOn;      // 关着的时候别把 dirty 挂着,否则每帧都会白走一遍 UpdateSkin
                    return;
                }
                if (_skinStuck > 0)
                {
                    // 之前卡住过、这一轮解开了 → 把状态文字改回真实(否则会一直挂着"没还原")
                    _skinStuck = 0;
                    if (!_skinOn) _skinStatus = "已关闭(已还原 " + _skinHits + " 处)";
                }

                List<string> keys = new List<string>(_skinTex.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    Texture2D t = _skinTex[keys[i]];
                    _skinTex.Remove(keys[i]);
                    if (t != null) { try { Destroy(t); } catch { } }
                }
                _skinNoFile.Clear();
            }
            catch (Exception e) { Warn("Skin/free", e); }
        }

        // ================= 写入 / 还原 =================
        // restoreOnly=true 一定写回原贴图;否则按当前预设写(预设=原色 时等于还原)
        private void PushSkinTextures(bool restoreOnly)
        {
            string preset = (restoreOnly || !_skinOn) ? null : CurrentSkinPreset;
            bool isNull = preset == NullPresetName;
            _skinWant.Clear();
            _skinWantPreset = preset == null ? "" : preset;

            HashSet<string> miss = new HashSet<string>();
            HashSet<string> keys = new HashSet<string>();
            HashSet<string> sizeBad = new HashSet<string>();
            int unresolved = 0;
            try
            {
                foreach (KeyValuePair<string, SkinEntry> kv in _skinKnown)
                {
                    SkinEntry e = kv.Value;
                    if (!EntryAlive(e)) continue;
                    Texture orig = OrigFor(e);
                    if (orig == null) { unresolved++; continue; }   // 原图查不到 → 不写(FreeSkinTextures 也会因此拒绝销毁)

                    Texture want = orig;
                    if (isNull)
                    {
                        Texture2D t = GetNullTexture(e.Key);
                        if (t != null) { want = t; keys.Add(e.Key); _skinWant[e.Key] = t; }
                        else { miss.Add(e.Key); }
                    }
                    else if (preset != null)
                    {
                        Texture2D t = GetSkinTexture(preset, e.Key);
                        if (t == null) { miss.Add(e.Key); }
                        else if (t.width != orig.width || t.height != orig.height)
                        {
                            // 尺寸不符 = UV 必然错位,宁可不换(只告警一次)
                            sizeBad.Add(e.Key);
                            if (_skinSizeWarn.Add(e.Key))
                                Logger.LogWarning("[skin] 尺寸不符,跳过: " + e.Key + ".png 是 " + t.width + "x" + t.height +
                                                  ",原贴图是 " + orig.width + "x" + orig.height + " —— UV 会错位");
                        }
                        else { want = t; keys.Add(e.Key); _skinWant[e.Key] = t; }
                    }

                    try
                    {
                        if (ReadEntry(e) != want) WriteEntry(e, want);
                        if (want != orig) RememberWrite(e);
                    }
                    catch (Exception ex) { Warn("Skin/set", ex); }
                }

                if (_skinHits == 0)
                    _skinStatus = "没找到可换材质,点『重新扫描』看日志";
                else if (_skinOn && preset != null)
                {
                    string s = isNull
                        ? "隐形: 已抹掉 " + keys.Count + " 种 / " + _skinHits + " 处"
                        : "预设 " + preset + ": 换 " + keys.Count + " 种 / " + _skinHits + " 处";
                    if (miss.Count > 0) s += ",缺 " + miss.Count + " 种图";
                    if (sizeBad.Count > 0) s += ",尺寸不符 " + sizeBad.Count + " 种";
                    if (unresolved > 0) s += ",未确认 " + unresolved + " 处";
                    _skinStatus = s;
                }
                else if (_skinOn)      // 开着但选了「原色」
                    _skinStatus = "原色(" + _skinHits + " 处)" +
                                  (_skinStuck > 0 ? "(" + _skinStuck + " 处没还原,看日志)" : "");
                else                   // 关着
                    _skinStatus = _skinStuck > 0
                        ? "已关闭,但 " + _skinStuck + " 处没还原(看日志 [skin])"
                        : "已关闭(已还原 " + _skinHits + " 处)";

                if (miss.Count > 0)
                    Logger.LogWarning("[skin] 预设 " + preset + " 缺少这些 PNG: " + string.Join(", ", ToArray(miss)));
            }
            catch (Exception e)
            {
                Warn("Skin/push", e);
                _skinStatus = "出错(看日志 [skin])";
            }
        }

        // 每帧的低成本续写:只对**已知来源**做一次"该挂的是不是我们的贴图,不是就写回去"。
        // 无字符串拼接、无集合分配;~几十次 GetTexture/SetTexture 在 Unity 里可忽略。
        private void KeepKnownWrites()
        {
            if (!_skinOn || _skinWant.Count == 0) return;
            string cur = CurrentSkinPreset == null ? "" : CurrentSkinPreset;
            if (cur != _skinWantPreset || _skinWantPreset.Length == 0) return;   // 预设变了 → 等下一轮 Push 重建
            try
            {
                foreach (KeyValuePair<string, SkinEntry> kv in _skinKnown)
                {
                    SkinEntry e = kv.Value;
                    if (!EntryAlive(e)) continue;
                    Texture2D want;
                    if (!_skinWant.TryGetValue(e.Key, out want) || want == null) continue;
                    try { if (ReadEntry(e) != want) WriteEntry(e, want); }
                    catch { }
                }
            }
            catch (Exception e)
            {
                if (++_skinErrLogged <= 5) Logger.LogWarning("[skin] KeepKnownWrites: " + e);
            }
        }

        public void SetSkinPreset(int idx)
        {
            if (SkinPresets == null || idx < 0 || idx >= SkinPresets.Length) return;
            if (idx == _skinIdx) return;
            // 顺序纪律:先把材质写回原贴图,再销毁我们自己的贴图(反过来 = 还在用就销毁)
            PushSkinTextures(true);
            FreeSkinTextures();
            _skinIdx = idx;
            _skinDirty = _skinOn;
            Toast("配色预设: " + SkinPresets[idx] + (_skinOn ? "" : "(未开启)"));
            Save();
        }

        // 面板里「配色预设 → 应用」:顺手把配色开关打开,否则点了没反应会让人以为坏了
        public void ApplySkinNow()
        {
            _skinOn = true;
            _skinScanned = false;
            _skinNextScan = 0f;
            _skinDirty = true;
            Toast("应用配色: " + (CurrentSkinPreset == null ? "原色" : CurrentSkinPreset));
            Save();
        }

        // 上下翻开关:改了要整批重新载入贴图(缓存的贴图方向是旧的)
        public void SetSkinFlip(bool v)
        {
            _skinFlip = v;
            PushSkinTextures(true);
            FreeSkinTextures();
            _skinSizeWarn.Clear();
            _skinNoFile.Clear();
            _skinDirty = _skinOn;
            Toast("PNG 上下翻转: " + (v ? "开(默认,方向正确)" : "关"));
            Save();
        }

        public void ReloadSkinPngs()
        {
            PushSkinTextures(true);
            FreeSkinTextures();
            _skinSizeWarn.Clear();
            _skinNoFile.Clear();              // 「重新读取 PNG」必须真的把黑名单清掉,否则后补的 PNG 永远读不进来
            _skinDirty = _skinOn;
            Toast(_skinOn ? "PNG 缓存已清空,将重新读取" : "PNG 缓存已清空(配色未开启)");
        }

        public void RescanSkin()
        {
            PushSkinTextures(true);      // 扫描前先还原,免得把我们的贴图当成"原贴图"
            _skinScanned = false;
            _skinNextScan = 0f;
            _skinDirty = _skinOn;
            Toast(_skinOn ? "正在重新扫描材质…" : "配色没开,打开开关后会自动扫描");
        }

        // ================= 每帧驱动 =================
        // 注意:必须在 Update() 的 `if (_open) return;` **之前**调用 —— 面板开着的时候也要能换色看效果
        public void UpdateSkin()
        {
            try
            {
                if (!_skinOn && !_skinDirty) return;

                float now = Time.unscaledTime;
                int pid = -1;
                try { var p = R.Player; if (p != null && p.transform != null) pid = p.transform.GetInstanceID(); } catch { }

                bool need = !_skinScanned || pid != _skinLastPlayerTid || now >= _skinNextScan;
                if (_skinOn && need)
                {
                    int before = _skinHits;
                    _skinLastPlayerTid = pid;
                    ScanSkinTargets();
                    if (_skinHits == 0)
                    {
                        // 玩家还没生成 / 骨架还没建:退避重试,别每秒扫全场景
                        _skinFutile++;
                        _skinNextScan = now + Mathf.Min(12f, 1f + _skinFutile * 2f);
                    }
                    else
                    {
                        _skinFutile = 0;
                        // 已知来源还在长 → 扫勤一点;稳定了就把间隔拉开。
                        // 真正的实时性靠 KeepKnownWrites,不靠这个间隔。
                        _skinNextScan = now + (_skinHits != before ? 1.5f : 6f);
                    }
                    _skinDirty = true;

                    // 玩家层级的渲染器清单:自动扒一次(不用主人点按钮),用来找那层"白光+染色"的光效
                    if (pid != -1 && pid != _skinDumpedForTid)
                    {
                        _skinDumpedForTid = pid;
                        LogPlayerRenderers(150);
                    }
                }

                if (_skinDirty && _skinOn)
                {
                    _skinDirty = false;
                    PushSkinTextures(false);
                }
                // 自发光/闪光那层只在某些帧出现在渲染器上(或每帧被 property block 重设)→ 每帧续写一次
                KeepKnownWrites();
            }
            catch (Exception e)
            {
                if (++_skinErrLogged <= 5) Logger.LogWarning("[skin] UpdateSkin: " + e);
            }
        }

        // ================= 诊断(查"为什么某处没跟着变色") =================
        // 用途:实机日志里只看到 6 张基础图集命中,闪光/自发光/特效那层根本没被匹配到 ——
        // 这个按钮把**已加载的全部 Material**(含未激活对象上的、资产里的)、**已加载的 Texture2D**、
        // 以及玩家层级下每个渲染器的材质 + per-renderer property block 都打进日志(搜 [skin])。
        private static readonly string[] SkinDiagKeys =
            { "player", "flash", "illum", "effect", "test", "charging", "afterimage", "glow", "qte", "skeleton", "spine" };

        private static bool DiagHit(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string low = s.ToLowerInvariant();
            for (int i = 0; i < SkinDiagKeys.Length; i++)
                if (low.IndexOf(SkinDiagKeys[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string TexInfo(Texture t)
        {
            if (t == null) return "null";
            try { return t.name + "(" + t.width + "x" + t.height + ")"; }
            catch { return "(读不出)"; }
        }

        // 材质上我们知道的那些属性里能读到的贴图
        private static string DescribeTextures(Material m)
        {
            string s = "";
            for (int i = 0; i < SkinTexProps.Length; i++)
            {
                try
                {
                    if (!m.HasProperty(SkinTexProps[i])) continue;
                    Texture t = m.GetTexture(SkinTexProps[i]);
                    if (t == null) continue;
                    s += SkinTexProps[i] + "=" + TexInfo(t) + " ";
                }
                catch { }
            }
            return s.Trim();
        }

        // 渲染器上的 per-renderer 覆盖(特效常用,材质本身看不到)
        private string DescribeBlock(Renderer r)
        {
            string s = "";
            try
            {
                _mpb.Clear();
                r.GetPropertyBlock(_mpb);
                for (int i = 0; i < SkinTexProps.Length; i++)
                {
                    Texture t = _mpb.GetTexture(SkinTexProps[i]);
                    if (t == null) continue;
                    s += SkinTexProps[i] + "=" + TexInfo(t) + " ";
                }
            }
            catch { }
            return s.Trim();
        }

        private static string MatColor(Material m)
        {
            try
            {
                Color c = m.color;
                return c.r.ToString("F2") + "," + c.g.ToString("F2") + "," + c.b.ToString("F2") + "," + c.a.ToString("F2");
            }
            catch { return "?"; }
        }

        private static string ShaderOf(Material m)
        {
            try { return m.shader != null ? m.shader.name : "?"; }
            catch { return "?"; }
        }

        // 把玩家层级下每个渲染器的"材质 + shader + color + 贴图 + property block"打进日志。
        // 目的:找出那层"白色贴图 + 染色"(实测日志里没对上的名字有 simpleFlash / white_point32_Sprite /
        // thinRing / falloffLightbeam / distortion3 这类)—— 那种光效的颜色来自 color/顶点色,
        // **换贴图换不到它**,必须先看清它是谁、挂在哪个渲染器上、颜色从哪来。
        public void LogPlayerRenderers(int cap)
        {
            try
            {
                var p = R.Player;
                if (p == null || p.transform == null) { Logger.LogInfo("[skin] 玩家还没生成,渲染器清单跳过"); return; }
                Renderer[] rs = p.transform.gameObject.GetComponentsInChildren<Renderer>(true);
                Logger.LogInfo("[skin] ---- 玩家层级渲染器清单(共 " + rs.Length + " 个,最多打 " + cap + " 行)----");
                int n = 0;
                for (int i = 0; i < rs.Length && n < cap; i++)
                {
                    Renderer r = rs[i];
                    if (r == null) continue;
                    n++;
                    string line = "RND " + r.GetType().Name + " " + r.gameObject.name +
                                  " active=" + r.gameObject.activeInHierarchy;
                    try
                    {
                        Material[] ms = r.sharedMaterials;
                        for (int k = 0; k < ms.Length; k++)
                            if (ms[k] != null)
                                line += " | mat=" + SafeName(ms[k]) + " sh=" + ShaderOf(ms[k]) +
                                        " c=" + MatColor(ms[k]) + " {" + DescribeTextures(ms[k]) + "}";
                    }
                    catch { }
                    string b = DescribeBlock(r);
                    if (b.Length > 0) line += " | BLOCK{" + b + "}";
                    Logger.LogInfo("[skin] " + line);
                }
                Logger.LogInfo("[skin] ---- 清单结束(打了 " + n + " 行)----");
            }
            catch (Exception e) { Warn("Skin/logrenderers", e); }
        }

        public void ExportSkinDiagnostics()
        {
            try
            {
                Logger.LogInfo("[skin] ===== 诊断开始(把带 [skin] 的行发给我) =====");
                int n = 0;

                // 1) 已加载的全部 Material —— 关键词过滤,重点看名字/贴图名带 player/flash/illum/effect 的
                UnityEngine.Object[] mats = Resources.FindObjectsOfTypeAll(typeof(Material));
                Logger.LogInfo("[skin] 已加载 Material: " + mats.Length + " 个");
                for (int i = 0; i < mats.Length && n < 150; i++)
                {
                    Material m = mats[i] as Material;
                    if (m == null) continue;
                    string nm = SafeName(m);
                    string tex = DescribeTextures(m);
                    if (!DiagHit(nm) && !DiagHit(tex)) continue;
                    n++;
                    Logger.LogInfo("[skin] MAT " + nm + " | shader=" + ShaderOf(m) + " | color=" + MatColor(m) + " | " + tex);
                }
                Logger.LogInfo("[skin] 上面打印了 " + n + " 个 Material(上限 150)");

                // 2) 已加载的 Texture2D
                UnityEngine.Object[] texs = Resources.FindObjectsOfTypeAll(typeof(Texture2D));
                List<string> tl = new List<string>();
                for (int i = 0; i < texs.Length; i++)
                {
                    Texture2D t = texs[i] as Texture2D;
                    if (t == null) continue;
                    if (!DiagHit(t.name)) continue;
                    if (tl.Count < 80) tl.Add(t.name + "(" + t.width + "x" + t.height + "," + t.format + ")");
                }
                Logger.LogInfo("[skin] 已加载 Texture2D: " + texs.Length + " 个;名字带关键词的 " + tl.Count + " 个:");
                for (int i = 0; i < tl.Count; i++) Logger.LogInfo("[skin]   TEX " + tl[i]);

                // 3) 玩家层级下每个渲染器(材质 + property block)—— 与自动扒的那份同一套代码
                LogPlayerRenderers(200);
                Logger.LogInfo("[skin] ===== 诊断结束 =====");
                Toast("材质诊断已写进日志 [skin]");
            }
            catch (Exception e) { Warn("Skin/diag", e); Toast("诊断出错: " + e.Message); }
        }

        private static string[] ToArray(HashSet<string> s)
        {
            string[] a = new string[s.Count];
            s.CopyTo(a);
            return a;
        }
    }
}
