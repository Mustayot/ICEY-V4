// ICEY Cheat Panel - MC 攻击模式补丁(任务:"把攻击方式改成 MC 那种")。partial class 第八个文件。
//
// 思路(比"事后藏特效渲染器"干净得多):
//   1) **从源头掐掉剑光**:游戏每段连击都会调 `PlayATK1Effect` / `PlayATK2Effect` / `PlayATK3Effect`
//      来放刀光 → 在这三个方法上挂 **Prefix 直接 return false**,方法体不执行 = 刀光根本不生成。
//      这样就不用去猜特效叫什么名字、也不用满场景关渲染器。
//   2) **攻击间隔改成 MC 那种**:MC 的剑是"按一下挥一次 + 冷却(≈0.625 秒)",而 ICEY 是快速三连段。
//      在 `EnterAtkSta`(进入攻击状态)上挂 Prefix:冷却没到就 return false → 这次攻击不成立,
//      于是画面上的方块人就是"一次挥砍 + 一段间隔",和 MC 手感一致。
//
// 安全:方法找不到就只写日志、不崩;开关关掉立刻恢复原状(补丁本身还在,只是 return true 放行)。

using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace IceyCheatPanel
{
    // 补丁类里不能直接用 `Logger`(那是插件实例的属性;静态上下文里会被解析成 UnityEngine.Logger ✗)
    // → 自己建一个日志源,写的是同一个 BepInEx 日志文件。
    internal static class McLog
    {
        internal static readonly ManualLogSource Lg = BepInEx.Logging.Logger.CreateLogSource("ICEY Cheat Panel");
    }

    // 掐掉剑光特效(只在 MC 攻击模式开着时生效)
    internal static class McNoAtkEffectPatch
    {
        internal static bool Enabled;
        // Prefix 返回 false = 跳过原方法体 → 刀光根本不生成。
        // ⚠ Harmony 的 Patch(original, prefix, postfix, transpiler, finalizer):prefix 必须传**第二个**参数。
        internal static bool Skip() { return !Enabled; }
    }

    // 攻击冷却:冷却没到就拦掉这次攻击
    internal static class McAtkIntervalPatch
    {
        internal static bool Enabled;
        internal static float Interval = 0.625f;
        private static float _lastAtk = -99f;
        private static float _lastLog;

        internal static bool Prefix()
        {
            if (!Enabled) return true;                 // 关着 → 放行(原版手感)
            float now = Time.time;
            if (now - _lastAtk < Interval)
            {
                if (now - _lastLog > 1f) { _lastLog = now; McLog.Lg.LogInfo("[mcatk] 冷却中,拦掉一次攻击(间隔 " + Interval.ToString("F2") + "s)"); }
                return false;                          // 拦掉:这次不进攻击状态
            }
            _lastAtk = now;
            return true;
        }
    }

    public partial class CheatPanelPlugin
    {
        public bool _mcAtkMode;                                  // MC 攻击方式(冷却 + 无剑光)
        public float _mcAtkInterval = 0.625f;                    // MC 剑冷却 ≈ 0.625 秒
        private bool _mcPatchTried, _mcAtkPatched;

        private static bool HasMethod(Type t, string name)
        {
            try { return AccessTools.Method(t, name) != null; } catch { return false; }
        }
        private static bool HasAnyMethod(Type t, string[] names)
        {
            for (int i = 0; i < names.Length; i++) if (HasMethod(t, names[i])) return true;
            return false;
        }

        // ★ 便宜的一次性查找:每种类型只反射一次 GetMethods(DeclaredOnly),然后比对名字。
        // 之前用 AccessTools.Method 每个名字查一次 → 几万次反射,进游戏主线程卡死(主人遇到的"未响应")。
        private static bool HasAnyMethodCheap(Type t, string[] names, out string found)
        {
            found = null;
            try
            {
                MethodInfo[] ms = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                for (int i = 0; i < ms.Length; i++)
                {
                    string n = ms[i].Name;
                    for (int k = 0; k < names.Length; k++)
                        if (n == names[k]) { found = n; return true; }
                }
            }
            catch { }
            return false;
        }

        // 懒安装(等游戏程序集加载好);找到几个就挂几个,找不到只写日志
        private void InstallMcPatches()
        {
            if (_mcPatchTried) return;
            // ★ 刚进游戏那几秒绝对不做反射扫描(会和场景加载抢主线程 → 看起来像"未响应")
            try { if (Time.unscaledTime < 6f) return; } catch { return; }
            _mcPatchTried = true;
            try
            {
                Harmony h = new Harmony("icey.cheatpanel.mcatk");
                // 大写 ATK / 小写 Atk 两种拼写都要试(游戏里两种都有,分属不同的类)
                string[] noEffect = { "PlayATK1Effect", "PlayATK2Effect", "PlayATK3Effect", "PlayAtk1Effect", "PlayAtk2Effect", "PlayAtk3Effect" };
                MethodInfo skip = AccessTools.Method(typeof(McNoAtkEffectPatch), "Skip");
                MethodInfo pre = AccessTools.Method(typeof(McAtkIntervalPatch), "Prefix");
                int nEffect = 0;

                // ★ 只扫游戏自己的两个程序集(攻击相关的方法一定在这里),不碰 UnityEngine/BCL 那几百个
                System.Collections.Generic.List<Type> hosts = new System.Collections.Generic.List<Type>();
                try
                {
                    System.Reflection.Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                    for (int a = 0; a < asms.Length; a++)
                    {
                        string an = "";
                        try { an = asms[a].GetName().Name; } catch { }
                        if (an != "Assembly-CSharp" && an != "Assembly-CSharp-firstpass") continue;
                        Type[] ts = null;
                        try { ts = asms[a].GetTypes(); } catch { continue; }
                        for (int t = 0; t < ts.Length; t++)
                        {
                            string found;
                            try
                            {
                                if (HasAnyMethodCheap(ts[t], noEffect, out found) || HasAnyMethodCheap(ts[t], new string[] { "EnterAtkSta" }, out found))
                                    hosts.Add(ts[t]);
                            }
                            catch { }
                        }
                    }
                    McLog.Lg.LogInfo("[mcatk] 扫描完成:候选类 " + hosts.Count + " 个");
                }
                catch (Exception e) { McLog.Lg.LogWarning("[mcatk] 扫类型失败: " + e.Message); }

                for (int t = 0; t < hosts.Count; t++)
                {
                    for (int i = 0; i < noEffect.Length; i++)
                    {
                        MethodInfo m = AccessTools.Method(hosts[t], noEffect[i]);
                        if (m == null) continue;
                        h.Patch(m, new HarmonyMethod(skip), null, null);   // prefix 在第 2 个参数(5 参重载已过时)
                        nEffect++;
                        McLog.Lg.LogInfo("[mcatk] 拦截 " + hosts[t].Name + "." + noEffect[i]);
                    }
                    MethodInfo enter = AccessTools.Method(hosts[t], "EnterAtkSta");
                    if (enter != null && !_mcAtkPatched)
                    {
                        h.Patch(enter, new HarmonyMethod(pre), null, null);
                        _mcAtkPatched = true;
                        McLog.Lg.LogInfo("[mcatk] 攻击间隔补丁已挂: " + hosts[t].Name + ".EnterAtkSta");
                    }
                }
                McLog.Lg.LogInfo("[mcatk] 候选类 " + hosts.Count + " 个;剑光拦截挂上 " + nEffect + " 个;攻击间隔 " +
                                 (_mcAtkPatched ? "已挂" : "没找到入口"));
            }
            catch (Exception e) { McLog.Lg.LogWarning("[mcatk] 补丁安装失败: " + e.Message); }
        }

        public void SetMcAtkMode(bool v)
        {
            _mcAtkMode = v;
            McNoAtkEffectPatch.Enabled = v;
            McAtkIntervalPatch.Enabled = v;
            McAtkIntervalPatch.Interval = Mathf.Max(0.05f, _mcAtkInterval);
            if (v) InstallMcPatches();
            Toast(v ? ("MC 攻击方式:开(间隔 " + _mcAtkInterval.ToString("F2") + "s)") : "MC 攻击方式:关(原版手感)");
            Save();
        }
    }
}
