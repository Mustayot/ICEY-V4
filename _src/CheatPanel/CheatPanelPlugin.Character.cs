// ICEY Cheat Panel - 角色替换地基(任务 A② 的第二步)。partial class 的第四个文件。
//
// 目标(分三步走,每步都能单独验证,别跳):
//   step1 隐藏原角色:只关 6 个骨架的 **MeshRenderer**,不动动画 —— 动画照跑,骨点/后续外挂模型才跟得上;
//   step2 读骨头:拿 SkeletonRenderer.skeleton 的 Bones(名字 + worldX/Y),先在屏幕上把骨点画出来验证坐标对不对;
//   step3 挂模型(下一步):把方块/立绘当子物体挂到骨架 GameObject 下,每帧跟着骨头走。
//
// 已知 API(已核对 Assembly-CSharp.il,都是 public):
//   Spine.Skeleton              : Bones(get) 是 List<Bone>、FindBone(string)
//   Spine.Bone                  : WorldX / WorldY(get)、X(get)、Data(get) → BoneData.Name(get)
//   SkeletonRenderer            : public 字段 skeleton(Spine.Skeleton)、private 字段 meshRenderer
// 坐标:骨头的 worldX/worldY 是**骨架局部空间**,和 SkeletonRenderer 生成网格用的坐标同一套
//       → 换算世界坐标用 `skeletonAnimation.transform.TransformPoint(x, y, 0)` 就行。
//
// 规矩:所有 Unity 调用都吞异常;开关默认关;藏起来的东西必须能原样恢复。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace IceyCheatPanel
{
    public partial class CheatPanelPlugin
    {
        public bool _hideOriginal;      // 藏掉原骨架的渲染(动画继续跑)
        public bool _boneHud;           // 屏幕上画骨点
        public string _charStatus = "未扫描角色";

        private sealed class SkelRef
        {
            public Component Comp;      // SkeletonAnimation
            public MeshRenderer Mr;
            public int MrId;            // 渲染器 instanceID —— "原始可见状态"按它记账
        }

        private readonly List<SkelRef> _skels = new List<SkelRef>();
        // 渲染器 instanceID -> **第一次**看到它时的 enabled。
        // 只记一次!以前每 3 秒重扫时会被覆盖成"已隐藏",于是"恢复"等于继续隐藏 —— 主人踩到的那个 bug。
        private readonly Dictionary<int, bool> _mrWasEnabled = new Dictionary<int, bool>();
        private readonly List<string> _boneLines = new List<string>();   // 给诊断日志用
        private float _charNextScan;
        private int _charHits;
        private bool _charDumped;

        // 每帧/定时调用(放在 Update() 的 _open 提前返回之前)
        public void UpdateCharacter()
        {
            try
            {
                if (!_mcPatchTried) InstallMcPatches();    // 补丁懒安装(去剑光 / MC 攻击共用)
                float now = Time.unscaledTime;
                if (now >= _charNextScan) { _charNextScan = now + 3f; ScanSkeletons(); }
                ApplyHideOriginal();
            }
            catch { }
        }

        // 找到玩家身上所有骨架(含未激活),缓存它们的 MeshRenderer
        public void ScanSkeletons()
        {
            try
            {
                _skels.Clear();
                var p = R.Player;
                if (p == null || p.transform == null) { _charStatus = "还没生成玩家"; _charHits = 0; return; }
                var arr = p.transform.gameObject.GetComponentsInChildren<SkeletonAnimation>(true);
                if (arr != null)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (arr[i] == null) continue;
                        SkelRef s = new SkelRef();
                        s.Comp = arr[i];
                        s.Mr = arr[i].GetComponent<MeshRenderer>();
                        if (s.Mr != null)
                        {
                            s.MrId = s.Mr.GetInstanceID();
                            bool was;
                            if (!_mrWasEnabled.TryGetValue(s.MrId, out was)) _mrWasEnabled[s.MrId] = s.Mr.enabled;
                        }
                        _skels.Add(s);
                    }
                }
                _charHits = _skels.Count;
                _charStatus = _skels.Count > 0
                    ? ("骨架 " + _skels.Count + " 个" + (_hideOriginal ? "(原角色已隐藏)" : ""))
                    : "没找到骨架(点『重新扫描骨架』)";
                if (_skels.Count > 0 && !_charDumped)
                {
                    _charDumped = true;
                    LogSkeletonInfo();
                }
            }
            catch (Exception e) { Warn("Char/scan", e); }
        }

        private void ApplyHideOriginal()
        {
            try
            {
                for (int i = 0; i < _skels.Count; i++)
                {
                    SkelRef s = _skels[i];
                    if (s == null || s.Mr == null) continue;
                    bool orig;
                    if (!_mrWasEnabled.TryGetValue(s.MrId, out orig)) orig = true;   // 没记录过就当作本来是开的
                    bool want = !_hideOriginal && orig;
                    if (s.Mr.enabled != want) s.Mr.enabled = want;
                }
            }
            catch { }
        }

        // 兜底:不管记录是什么,直接把所有骨架渲染器打开(万一状态被搞坏,不用重启游戏)
        public void ForceRestoreOriginal()
        {
            try
            {
                _hideOriginal = false;
                if (_skels.Count == 0) ScanSkeletons();
                for (int i = 0; i < _skels.Count; i++)
                {
                    SkelRef s = _skels[i];
                    if (s == null || s.Mr == null) continue;
                    s.Mr.enabled = true;
                    _mrWasEnabled[s.MrId] = true;
                }
                _charStatus = "已强制恢复原角色(" + _skels.Count + " 个骨架)";
                Toast(_charStatus);
                Save();
            }
            catch (Exception e) { Warn("Char/force", e); }
        }

        public void SetHideOriginal(bool v)
        {
            _hideOriginal = v;
            if (_skels.Count == 0) ScanSkeletons();
            ApplyHideOriginal();
            Toast(v ? "原角色已隐藏(动画还在跑)" : "原角色已恢复");
            Save();
        }

        // 骨头表:名字 + 局部/世界坐标 + 旋转 + 缩放。挂模型前先看这张表。
        public void LogSkeletonInfo()
        {
            try
            {
                _boneLines.Clear();
                Logger.LogInfo("[char] ---- 骨架信息 ----");
                for (int i = 0; i < _skels.Count; i++)
                {
                    SkelRef s = _skels[i];
                    if (s == null || s.Comp == null) continue;
                    var sa = s.Comp as SkeletonAnimation;
                    if (sa == null) continue;
                    var skel = sa.skeleton;
                    if (skel == null) { Logger.LogInfo("[char] 骨架 " + i + ": skeleton 为空"); continue; }
                    var bones = skel.Bones;
                    Logger.LogInfo("[char] 骨架 " + i + " (" + sa.gameObject.name + "): 骨头 " + (bones != null ? bones.Count : 0) + " 根");
                    if (bones == null) continue;
                    for (int b = 0; b < bones.Count; b++)
                    {
                        var bone = bones[b];
                        if (bone == null) continue;
                        string nm = "?";
                        try { nm = bone.Data != null ? bone.Data.Name : "?"; } catch { }
                        float wx = 0f, wy = 0f, rot = 0f;
                        try { wx = bone.WorldX; wy = bone.WorldY; } catch { }
                        try { rot = bone.X; } catch { }
                        string line = "  B " + b + " " + nm + " world=(" + wx.ToString("F1") + "," + wy.ToString("F1") +
                                      ") x=" + rot.ToString("F2");
                        _boneLines.Add(line);
                        if (b < 60) Logger.LogInfo("[char]" + line);
                    }
                }
                Logger.LogInfo("[char] ---- 骨架信息结束(共 " + _boneLines.Count + " 根)----");
                Toast("骨架信息已写进日志 [char]");
            }
            catch (Exception e) { Warn("Char/log", e); Toast("读骨头出错: " + e.Message); }
        }

        // 屏幕上把骨点画出来:验证"骨头 → 世界 → 屏幕"这条换算链对不对(挂模型全靠它)
        public void DrawBoneHud()
        {
            if (!_boneHud) return;
            try
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                if (_texWhite == null) return;
                int labeled = 0;
                for (int i = 0; i < _skels.Count; i++)
                {
                    SkelRef s = _skels[i];
                    if (s == null || s.Comp == null) continue;
                    var sa = s.Comp as SkeletonAnimation;
                    if (sa == null || sa.skeleton == null) continue;
                    var bones = sa.skeleton.Bones;
                    if (bones == null) continue;
                    for (int b = 0; b < bones.Count; b++)
                    {
                        var bone = bones[b];
                        if (bone == null) continue;
                        float wx = 0f, wy = 0f;
                        try { wx = bone.WorldX; wy = bone.WorldY; } catch { continue; }
                        Vector3 world = sa.transform.TransformPoint(new Vector3(wx, wy, 0f));
                        Vector3 sp = cam.WorldToScreenPoint(world);
                        if (sp.z < 0f) continue;
                        float sx = sp.x, sy = Screen.height - sp.y;
                        GUI.color = new Color(1f, 0.3f, 0.6f, 0.9f);
                        GUI.DrawTexture(new Rect(sx - 2f, sy - 2f, 4f, 4f), _texWhite);
                        if (labeled < 14 && b % 1 == 0)
                        {
                            string nm = "?";
                            try { nm = bone.Data != null ? bone.Data.Name : "?"; } catch { }
                            if (nm == "root" || nm == "body" || nm == "upbody" || nm == "legs" ||
                                nm == "leftLeg" || nm == "rightLeg" || nm == "head" || nm == "tail" ||
                                nm == "leftShank" || nm == "rightShank" || nm == "upbody" || nm == "leftArm" ||
                                nm == "rightArm" || nm == "hand" || nm == "qiangshua")
                            {
                                GUI.color = new Color(1f, 1f, 1f, 0.95f);
                                GUI.Label(new Rect(sx + 4f, sy - 8f, 150f, 18f), nm);
                                labeled++;
                            }
                        }
                    }
                }
                GUI.color = Color.white;
            }
            catch { }
        }
    }
}
