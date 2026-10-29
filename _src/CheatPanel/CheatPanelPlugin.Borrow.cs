// ICEY Cheat Panel - 任务 C:把主角换成别的角色(第一版:运行时"借"别人的骨架)。partial class 第七个文件。
//
// 为什么走运行时而**不是**改 assets:
//   交接文档 13.6 原来的路子是"把敌人的 .skel/.atlas 换进 assets" —— 那要**重写 assets 结构**
//   (TextAsset 长度一变,后面所有对象的偏移都得重算),现有工具链只做过 resS 原地覆盖,风险很大。
//   而 `SkeletonRenderer.skeletonDataAsset` 是 **public 字段** → 直接在运行时把主角 6 个槽位的
//   skeletonDataAsset 换成敌人的那一份就行,原样记下来还能一键还原,一步都不碰游戏文件。
//
// 已知要处理的:
//   * 换了数据以后必须重新初始化(这个 Spine 版本没有 `Initialize`,但 `Awake()` 是 public 且
//     就是"从 skeletonDataAsset 建 Skeleton/AnimationState"的那一步)→ 调用顺序 Reset() → Awake()。
//   * **动画名不通用**:主角 16 个动作,敌人只有一部分(Jumper 5/16、Rider 4/16…)→ 缺的会报错并保持旧姿势。
//     所以这一版先只做"换数据 + 列出动画重叠",下一版再加"缺的动画兜底播 Idle"的补丁。
//   * 6 个槽位分别换、分别记,还原时逐个写回。

using System;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;

namespace IceyCheatPanel
{
    public partial class CheatPanelPlugin
    {
        public bool _borrowOn;
        public int _borrowIdx;
        public string _borrowStatus = "未扫描";

        private sealed class BorrowRef
        {
            public Component Comp;                  // SkeletonAnimation
            public UnityEngine.Object OrigAsset;    // 原来的 skeletonDataAsset
        }

        private readonly List<BorrowRef> _borrowSlots = new List<BorrowRef>();
        private readonly List<Component> _borrowSources = new List<Component>();
        private string[] _borrowNames = new string[] { "(未扫描)" };
        private UnityEngine.Object _borrowAsset;

        // 主角的动作名(用来算"这套骨架支持哪些")—— 列表不全没关系,只是给日志看重叠
        private static readonly string[] PlayerAnimNames = {
            "Idle", "Run", "RunSlow", "Roll", "Jump", "Jump2", "Fall1", "Fall2", "Ready",
            "Atk1", "Atk2", "Atk3", "AtkHv1", "UpRising", "HitGround", "Execute", "Defense",
            "Flash1", "CallSkill", "RunJump", "RollJump", "EndAtk"
        };

        // ================= 扫描"别人的骨架" =================
        public void ScanBorrowSources()
        {
            try
            {
                List<string> names = new List<string>();
                _borrowSources.Clear();
                UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(typeof(SkeletonAnimation));
                Transform pt = null;
                try { var p = R.Player; if (p != null && p.transform != null) pt = p.transform; } catch { }
                for (int i = 0; i < all.Length; i++)
                {
                    var sa = all[i] as SkeletonAnimation;
                    if (sa == null) continue;
                    try
                    {
                        if (pt != null && sa.transform != null && sa.transform.IsChildOf(pt)) continue;   // 主角自己的不算
                        if (sa.skeletonDataAsset == null) continue;
                        _borrowSources.Add(sa);
                        names.Add((_borrowSources.Count - 1) + ". " + sa.gameObject.name + " [" + sa.skeletonDataAsset.name + "]");
                    }
                    catch { }
                }
                if (names.Count == 0) names.Add("(场景里没找到别的骨架)");
                _borrowNames = names.ToArray();
                if (_borrowIdx >= _borrowNames.Length) _borrowIdx = 0;
                _borrowStatus = "找到 " + _borrowSources.Count + " 个可借骨架";
                Logger.LogInfo("[borrow] " + _borrowStatus);
                for (int i = 0; i < _borrowNames.Length && i < 40; i++) Logger.LogInfo("[borrow]   " + _borrowNames[i]);
                if (_borrowSources.Count > 0) LogAnimOverlap(_borrowSources[Mathf.Clamp(_borrowIdx, 0, _borrowSources.Count - 1)]);
            }
            catch (Exception e) { Warn("Borrow/scan", e); }
        }

        // 这套骨架支持主角哪些动作(缺的要靠"兜底播 Idle")
        private void LogAnimOverlap(Component sa)
        {
            try
            {
                var anim = sa as SkeletonAnimation;
                if (anim == null || anim.skeletonDataAsset == null) return;
                var data = anim.skeletonDataAsset.GetSkeletonData(false);
                if (data == null) { Logger.LogInfo("[borrow] GetSkeletonData 返回空"); return; }
                int ok = 0;
                string yes = "", no = "";
                for (int i = 0; i < PlayerAnimNames.Length; i++)
                {
                    bool has = false;
                    try { has = data.FindAnimation(PlayerAnimNames[i]) != null; } catch { }
                    if (has) { ok++; yes += PlayerAnimNames[i] + " "; }
                    else no += PlayerAnimNames[i] + " ";
                }
                Logger.LogInfo("[borrow] " + anim.gameObject.name + " 动作重叠 " + ok + "/" + PlayerAnimNames.Length);
                Logger.LogInfo("[borrow]   有: " + yes);
                Logger.LogInfo("[borrow]   缺: " + no);
                _borrowStatus = "可借 " + _borrowSources.Count + " 个;当前这套支持 " + ok + "/" + PlayerAnimNames.Length;
            }
            catch (Exception e) { Warn("Borrow/overlap", e); }
        }

        // ================= 套用 / 还原 =================
        public void ApplyBorrowedSkeleton()
        {
            try
            {
                if (_borrowSources.Count == 0) { ScanBorrowSources(); }
                if (_borrowSources.Count == 0) { Toast("没有可借的骨架"); return; }
                int idx = Mathf.Clamp(_borrowIdx, 0, _borrowSources.Count - 1);
                var src = _borrowSources[idx] as SkeletonAnimation;
                if (src == null || src.skeletonDataAsset == null) { Toast("这套骨架取不到数据"); return; }
                _borrowAsset = src.skeletonDataAsset;

                if (_borrowSlots.Count == 0) CollectBorrowSlots();
                if (_borrowSlots.Count == 0) { Toast("没找到主角的骨架槽位"); return; }

                int n = 0;
                for (int i = 0; i < _borrowSlots.Count; i++)
                {
                    var slot = _borrowSlots[i].Comp as SkeletonAnimation;
                    if (slot == null) continue;
                    try
                    {
                        slot.skeletonDataAsset = src.skeletonDataAsset;   // public 字段,直接换
                        ReinitSkeleton(slot);
                        n++;
                    }
                    catch (Exception e) { Warn("Borrow/apply slot", e); }
                }
                _borrowOn = true;
                _borrowStatus = "已套用 " + src.gameObject.name + "(" + n + " 个槽位)";
                Logger.LogInfo("[borrow] " + _borrowStatus + "  asset=" + src.skeletonDataAsset.name);
                LogAnimOverlap(src);
                Toast(_borrowStatus);
                Save();
            }
            catch (Exception e) { Warn("Borrow/apply", e); Toast("套用失败: " + e.Message); }
        }

        public void RevertBorrowedSkeleton()
        {
            try
            {
                int n = 0, miss = 0;
                for (int i = 0; i < _borrowSlots.Count; i++)
                {
                    var slot = _borrowSlots[i].Comp as SkeletonAnimation;
                    if (slot == null) { miss++; continue; }
                    try
                    {
                        if (_borrowSlots[i].OrigAsset != null) slot.skeletonDataAsset = (SkeletonDataAsset)_borrowSlots[i].OrigAsset;
                        ReinitSkeleton(slot);
                        n++;
                    }
                    catch (Exception e) { Warn("Borrow/revert slot", e); }
                }
                _borrowOn = false;
                _borrowStatus = "已还原 " + n + " 个槽位" + (miss > 0 ? ",丢了 " + miss + " 个" : "");
                Toast(_borrowStatus);
                Logger.LogInfo("[borrow] " + _borrowStatus);
                Save();
            }
            catch (Exception e) { Warn("Borrow/revert", e); }
        }

        // 把主角 6 个骨架槽位登记下来(第一次套用前调)
        private void CollectBorrowSlots()
        {
            try
            {
                _borrowSlots.Clear();
                if (_skels.Count == 0) ScanSkeletons();
                for (int i = 0; i < _skels.Count; i++)
                {
                    var sa = _skels[i] != null ? _skels[i].Comp as SkeletonAnimation : null;
                    if (sa == null) continue;
                    BorrowRef b = new BorrowRef();
                    b.Comp = sa;
                    b.OrigAsset = sa.skeletonDataAsset;
                    _borrowSlots.Add(b);
                }
                Logger.LogInfo("[borrow] 主角槽位登记: " + _borrowSlots.Count + " 个");
            }
            catch (Exception e) { Warn("Borrow/collect", e); }
        }

        // 换完数据必须重建:Skeleton/AnimationState 都是从 skeletonDataAsset 来的。
        // 这个 Spine 版本没有 Initialize(),但 Awake() 是 public 且正是干这个的。
        private void ReinitSkeleton(SkeletonAnimation sa)
        {
            try { sa.Reset(); } catch (Exception e) { Warn("Borrow/reset", e); }
            try { sa.Awake(); } catch (Exception e) { Warn("Borrow/awake", e); }
        }

        public void SetBorrowIdx(int idx)
        {
            _borrowIdx = idx;
            if (_borrowSources.Count > 0) LogAnimOverlap(_borrowSources[Mathf.Clamp(idx, 0, _borrowSources.Count - 1)]);
            Save();
        }
    }
}
