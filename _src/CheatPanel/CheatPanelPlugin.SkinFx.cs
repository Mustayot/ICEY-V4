// ICEY Cheat Panel - 动态配色(人物颜色随时间变幻)。partial class 第十个文件。
//
// 【为什么不是改材质颜色】第一版我改的是材质的 `_Color`,日志证明**接管成功**(7 个材质,原始色白色),
//   但画面**纹丝不动** → 这个自定义 shader(`Spine/Skeleton IllumPlayer AddNoLight`)**不看 `_Color`**。
//
// 【正确的通道:顶点色】Spine 的着色是"贴图 × 顶点色"。这游戏打中敌人时身上闪白,靠的就是顶点色。
//   所以动态配色要走顶点色:Spine 每帧会重建网格(顶点色也被重置成骨架里的值),
//   我们就在**它重建完之后立刻**把顶点色乘上我们的色调 —— 用 Harmony 在 `SkeletonRenderer.LateUpdate()` 上挂 postfix,
//   这样**必然在重建之后、渲染之前**,不依赖脚本执行顺序。
//
// 【性能】网格重建每帧都在,所以我们不能每帧都写:
//   只在"色调相对上次写的变了超过 1%"时才写一次(渐变速度 0.35 时约等于每秒十几次,肉眼完全看不出跳)——
//   顺带把 `colors32` 数组的分配也压下去。
//
// 【还原】顶点色每帧都会从骨架重新生成 → 关掉开关(Enabled=false)后**下一帧就自动恢复原样**,不需要记账写回。

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace IceyCheatPanel
{
    // 顶点色染色补丁:挂在 Spine 骨架每帧重建网格之后
    internal static class SpineTintPatch
    {
        internal static bool Enabled;
        internal static Color Tint = Color.white;
        internal static int Applied;

        private static readonly Dictionary<int, Color> _last = new Dictionary<int, Color>();
        private static bool _installed;

        internal static void Install()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                Type t = AccessTools.TypeByName("SkeletonRenderer");
                if (t == null) { McLog.Lg.LogWarning("[fx] 没找到 SkeletonRenderer,顶点色染色不可用"); return; }
                MethodInfo m = AccessTools.Method(t, "LateUpdate");
                if (m == null) { McLog.Lg.LogWarning("[fx] 没找到 SkeletonRenderer.LateUpdate"); return; }
                MethodInfo post = AccessTools.Method(typeof(SpineTintPatch), "Postfix");
                new Harmony("icey.cheatpanel.fx").Patch(m, null, new HarmonyMethod(post), null);
                McLog.Lg.LogInfo("[fx] 顶点色染色补丁已挂: SkeletonRenderer.LateUpdate");
            }
            catch (Exception e) { McLog.Lg.LogWarning("[fx] 补丁安装失败: " + e.Message); }
        }

        internal static void Postfix(object __instance)
        {
            if (!Enabled) return;
            try
            {
                Component c = __instance as Component;
                if (c == null) return;
                MeshFilter mf = c.GetComponent<MeshFilter>();
                if (mf == null) return;
                Mesh mesh = mf.sharedMesh;
                if (mesh == null) return;
                int id = mesh.GetInstanceID();
                Color prev;
                if (_last.TryGetValue(id, out prev) &&
                    Mathf.Abs(prev.r - Tint.r) < 0.01f && Mathf.Abs(prev.g - Tint.g) < 0.01f && Mathf.Abs(prev.b - Tint.b) < 0.01f)
                    return;                                   // 色调几乎没变 → 这一帧不写(网格里已经是上次的染色)
                Color32[] cols = mesh.colors32;
                if (cols == null || cols.Length == 0) return;
                Color32 t = Tint;
                for (int i = 0; i < cols.Length; i++)
                    cols[i] = new Color32((byte)(cols[i].r * t.r), (byte)(cols[i].g * t.g), (byte)(cols[i].b * t.b), cols[i].a);
                mesh.colors32 = cols;
                _last[id] = Tint;
                Applied++;
            }
            catch { }
        }

        internal static void Reset()
        {
            _last.Clear();
        }
    }

    public partial class CheatPanelPlugin
    {
        public bool _fxOn;                 // 动态配色开关
        public bool _fxRainbow = true;     // true = 彩虹循环;false = 呼吸明暗
        public float _fxSpeed = 0.35f;     // 变幻速度
        public float _fxSat = 0.75f;       // 饱和度
        public float _fxMin = 0.45f;       // 最暗亮度
        public string _fxStatus = "未启用";

        public void SetSkinFx(bool v)
        {
            _fxOn = v;
            SpineTintPatch.Enabled = v;
            if (v)
            {
                SpineTintPatch.Install();
                SpineTintPatch.Reset();
                Toast("动态配色:开(" + (_fxRainbow ? "彩虹循环" : "呼吸明暗") + ")");
            }
            else
            {
                SpineTintPatch.Tint = Color.white;
                SpineTintPatch.Reset();
                Toast("动态配色:关(下一帧自动恢复原色)");
            }
            Save();
        }

        // 每帧调(放在 Update() 的 _open 提前返回之前)
        public void UpdateSkinFx()
        {
            if (!_fxOn) return;
            try
            {
                SpineTintPatch.Enabled = true;
                SpineTintPatch.Tint = FxColor(Time.unscaledTime);
                _fxStatus = "运行中:" + (_fxRainbow ? "彩虹" : "呼吸") + " 速度 " + _fxSpeed.ToString("F2") +
                            " 已染色 " + SpineTintPatch.Applied + " 次";
            }
            catch (Exception e) { Warn("Fx/update", e); }
        }

        // 时间 → 染色(RGB 乘数;白色 = 不变)
        private Color FxColor(float now)
        {
            float speed = Mathf.Max(0.01f, _fxSpeed);
            float phase = now * speed;
            if (_fxRainbow)
            {
                float hue = Mathf.Repeat(phase, 1f);                       // 彩虹:色相绕环
                float v = Mathf.Lerp(Mathf.Clamp(_fxMin, 0.4f, 1f), 1f, 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f));
                return Color.HSVToRGB(hue, Mathf.Clamp01(_fxSat), Mathf.Clamp01(v));
            }
            // 呼吸:同色明暗脉动(色相也慢慢漂一点)
            float h2 = Mathf.Repeat(phase * 0.15f, 1f);
            float b = Mathf.Lerp(0.55f, 1f, 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f));
            return Color.HSVToRGB(h2, Mathf.Clamp01(_fxSat) * 0.55f, Mathf.Clamp01(b));
        }
    }
}
