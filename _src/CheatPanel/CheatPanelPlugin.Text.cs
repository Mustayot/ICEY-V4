// ICEY Cheat Panel - 任务 B:运行时文本替换(不碰 assets)。partial class 的第六个文件。
//
// 方案(交接文档 11.6 的结论,已核对 Assembly-CSharp-firstpass.il):
//   * 补一个 Harmony **postfix** 在 `I2.Loc.TermData::GetTranslation(int32)` 上 —— 这个点是"术语翻译"的最底层,
//     `LanguageSource::TryGetTermTranslation`(L122894/122930 处调用它)和 Localize 控件的多参重载都会走到,
//     所以**不会漏**(文档特别警告:只 patch 单参 GetTermTranslation 会漏)。
//   * 术语名就在 TermData 自己的 public 字段 `Term` 上 → 补丁里直接拿它查替换表,不需要碰 Sources、不需要重扫。
//   * 好处:场景重建 / 语言切换 / Sources 一开始是空表 这些坑**天然免疫**(补丁是每次翻译时现查)。
//   * 换文本表:`BepInEx\config\text_override.txt`,一行一条 `术语<TAB>原文<TAB>新文本`;
//     第三列**留空 = 不替换**(这条很关键,否则导出模板会直接把界面清空)。
//   * 全量导出:`BepInEx\config\text_all.txt`(启动后自动导一次 + 面板按钮随时重导)。
//   * 已经画在屏幕上的文字不会自己变 → 改完点「刷新界面文本」= `LocalizationManager.LocalizeAll(**true**)`
//     (文档坑 #3:传 false 有 early-return,不刷新)。
//
// 注意:补丁类是 static 的,拿不到插件实例 → 替换表放在**静态字段**上,插件改的时候整体换引用。

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using I2.Loc;
using UnityEngine;

namespace IceyCheatPanel
{
    // 术语 → 新文本。手动 TargetMethod(),**特性里绝不写游戏类型**(元数据里出现游戏类型会让 BepInEx 丢掉整个 dll)
    internal static class TermTranslationPatch
    {
        internal static Dictionary<string, string> Overrides = new Dictionary<string, string>();
        internal static bool Enabled;
        internal static int Hits;

        private static FieldInfo _termField;

        internal static MethodBase TargetMethod()
        {
            try
            {
                Type t = AccessTools.TypeByName("I2.Loc.TermData");
                if (t == null) return null;
                _termField = AccessTools.Field(t, "Term");
                MethodBase m = AccessTools.Method(t, "GetTranslation", new Type[] { typeof(int) });
                if (m == null) return null;
                return m;
            }
            catch { return null; }
        }

        // 补丁体里只用 object / BCL 类型(签名不许出现游戏类型)
        internal static void Postfix(object __instance, ref string __result)
        {
            try
            {
                if (!Enabled || __instance == null || Overrides.Count == 0) return;
                if (_termField == null) return;
                string term = _termField.GetValue(__instance) as string;
                if (string.IsNullOrEmpty(term)) return;
                string rep;
                if (Overrides.TryGetValue(term, out rep) && !string.IsNullOrEmpty(rep))
                {
                    __result = rep;
                    Hits++;
                }
            }
            catch { }
        }
    }

    public partial class CheatPanelPlugin
    {
        public bool _textOn;                       // 持久化:texton=1
        public string _textStatus = "未加载";

        private int _textRules, _textDumped, _textDistinct;
        private bool _textPatched, _textPatchFailed, _textAutoDumped;
        private float _textNextTry;
        private string _textLangHow = "还没定位";
        private int _textAutoDumps, _textLastCount;
        private StringBuilder _textSb = new StringBuilder(1 << 20);

        public string TextOverridePath { get { return Path.Combine(Paths.ConfigPath, "text_override.txt"); } }
        public string TextAllPath { get { return Path.Combine(Paths.ConfigPath, "text_all.txt"); } }

        // ================= 开关 / 读表 =================
        public void SetTextOn(bool v)
        {
            _textOn = v;
            TermTranslationPatch.Enabled = v;
            if (v)
            {
                ReloadTextOverrides();
                RefreshLocalizedText();
                Toast("文本替换已开(" + _textRules + " 条规则)");
            }
            else
            {
                RefreshLocalizedText();
                Toast("文本替换已关");
            }
            Save();
        }

        public void ReloadTextOverrides()
        {
            try
            {
                Dictionary<string, string> map = new Dictionary<string, string>();
                string p = TextOverridePath;
                int bad = 0;
                if (File.Exists(p))
                {
                    string[] lines = File.ReadAllLines(p, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string ln = lines[i];
                        if (ln == null) continue;
                        ln = ln.TrimEnd('\r');
                        if (ln.Length == 0 || ln[0] == '#') continue;      // # 开头是注释
                        string[] parts = ln.Split('\t');
                        if (parts.Length < 2) { bad++; continue; }
                        string term = parts[0].Trim();
                        string nw = (parts.Length >= 3 ? parts[2] : parts[1]);
                        if (term.Length == 0 || nw.Length == 0) continue;  // 第三列留空 = 不替换
                        map[term] = nw;
                    }
                }
                else Logger.LogWarning("[text] 还没有替换表: " + p);

                TermTranslationPatch.Overrides = map;      // 整体换引用,补丁侧立刻生效
                _textRules = map.Count;
                if (bad > 0) Logger.LogWarning("[text] 替换表里有 " + bad + " 行格式不对(至少要有 术语<TAB>…)");
                Logger.LogInfo("[text] 替换表载入: " + _textRules + " 条规则" + (File.Exists(p) ? "" : "(文件不存在)"));
                UpdateTextStatus();
            }
            catch (Exception e) { Warn("Text/reload", e); }
        }

        private void UpdateTextStatus()
        {
            _textStatus = "规则 " + _textRules + " 条 / 命中 " + TermTranslationPatch.Hits +
                          " 次 / 已导出 " + _textDumped + " 条 / 语言列 " + _textLangHow +
                          (_textPatchFailed ? " / 补丁失败(看日志)" : "");
        }

        // ================= 全量导出 / 模板 =================
        public void DumpAllTerms()
        {
            try
            {
                int n = 0, distinct = 0;
                string cur = "";
                _textSb.Length = 0;
                try { cur = LocalizationManager.CurrentLanguage; } catch { }
                _textSb.AppendLine("# ICEY 全量术语导出 —— 一行一条:术语<TAB>当前语言原文<TAB>新文本(留空=不替换)");
                _textSb.AppendLine("# 当前语言: " + cur + "    导出时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _textSb.AppendLine("# 语言列定位: " + _textLangHow);
                _textSb.AppendLine("# 注意:'原文'这一列是**当前语言**的文字;换语言的存档要在游戏里切语言后再导一次");
                _textSb.AppendLine("# 要改文本:把需要的行拷进 text_override.txt,在第三列填新文本,然后在面板点「重新读取替换表」+「刷新界面文本」");
                HashSet<string> seen = new HashSet<string>();

                var sources = LocalizationManager.Sources;
                if (sources != null)
                {
                    for (int i = 0; i < sources.Count; i++)
                    {
                        var src = sources[i];
                        if (src == null) continue;
                        string how;
                        int idx = ResolveLanguageIndex(src, cur, out how);
                        if (i == 0) _textLangHow = cur + " → 第 " + idx + " 列(" + how + ")";
                        var terms = src.mTerms;
                        if (terms == null) continue;
                        for (int t = 0; t < terms.Count; t++)
                        {
                            var td = terms[t];
                            if (td == null) continue;
                            string term = Clean(td.Term);
                            if (term.Length == 0) continue;
                            string origin = "";
                            try
                            {
                                if (td.Languages != null && idx >= 0 && idx < td.Languages.Length) origin = td.Languages[idx];
                            }
                            catch { }
                            origin = Clean(origin);
                            _textSb.Append(term).Append('\t').Append(origin).Append('\t').Append('\n');
                            n++;
                            if (seen.Add(term)) distinct++;
                        }
                    }
                }
                File.WriteAllText(TextAllPath, _textSb.ToString(), new UTF8Encoding(true));   // 带 BOM:Excel 打开不乱码
                _textDumped = n;
                if (_textDistinct != distinct) _textDistinct = distinct;
                Logger.LogInfo("[text] 全量术语导出: " + n + " 条(去重 " + distinct + ")/ 来源 " +
                               (sources != null ? sources.Count : 0) + " 个 -> " + TextAllPath);
                UpdateTextStatus();
                Toast("已导出 " + n + " 条术语(见 text_all.txt)");
            }
            catch (Exception e) { Warn("Text/dump", e); Toast("导出失败: " + e.Message); }
        }

        // 从全量导出生成替换表模板(**已存在就不动**,免得覆盖主人填过的东西)
        public void MakeOverrideTemplate()
        {
            try
            {
                string p = TextOverridePath;
                if (File.Exists(p)) { Toast("替换表已存在,不覆盖: " + Path.GetFileName(p)); return; }
                if (!File.Exists(TextAllPath)) DumpAllTerms();
                if (!File.Exists(TextAllPath)) { Toast("先导出全量术语失败,模板没生成"); return; }
                string[] lines = File.ReadAllLines(TextAllPath, Encoding.UTF8);
                StringBuilder sb = new StringBuilder(1 << 20);
                sb.AppendLine("# 文本替换表 —— 一行一条:术语<TAB>原文<TAB>新文本");
                sb.AppendLine("# 第三列留空 = 不替换;不需要的行可以直接删掉;改完在面板点「重新读取替换表」再点「刷新界面文本」");
                int n = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i];
                    if (ln == null) continue;
                    ln = ln.TrimEnd('\r');
                    if (ln.Length == 0 || ln[0] == '#') continue;
                    sb.AppendLine(ln);
                    n++;
                }
                File.WriteAllText(p, sb.ToString(), new UTF8Encoding(true));
                Logger.LogInfo("[text] 替换表模板已生成: " + n + " 行 -> " + p);
                Toast("替换表模板已生成(" + n + " 行),第三列填新文本");
                ReloadTextOverrides();
            }
            catch (Exception e) { Warn("Text/template", e); Toast("生成模板失败: " + e.Message); }
        }

        // 定位"当前语言"是哪一列。
        // 坑:`LocalizationManager.CurrentLanguage` 是**显示名**(如 `Chinese (Simplified)`),
        // 而 `GetLanguageIndexFromCode` 要的是 ISO 代码 → 直接拿显示名去查会失败,静默退回第 0 列(通常是英文)。
        // 正确做法:先按 `LanguageData.Name` 比,再按 `Code` 比,最后才退老办法。
        private static int ResolveLanguageIndex(object srcObj, string cur, out string how)
        {
            how = "回退第 0 列";
            try
            {
                var src = srcObj as LanguageSource;
                if (src == null) return 0;
                var langs = src.mLanguages;
                if (langs != null && !string.IsNullOrEmpty(cur))
                {
                    for (int i = 0; i < langs.Count; i++)
                    {
                        var ld = langs[i];
                        if (ld == null) continue;
                        if (string.Equals(ld.Name, cur, StringComparison.OrdinalIgnoreCase)) { how = "按语言名匹配"; return i; }
                    }
                    for (int i = 0; i < langs.Count; i++)
                    {
                        var ld = langs[i];
                        if (ld == null) continue;
                        if (!string.IsNullOrEmpty(ld.Code) && string.Equals(ld.Code, cur, StringComparison.OrdinalIgnoreCase)) { how = "按语言代码匹配"; return i; }
                    }
                }
                int k = src.GetLanguageIndexFromCode(cur);        // 要求传 ISO 代码
                if (k >= 0) { how = "GetLanguageIndexFromCode"; return k; }
            }
            catch { }
            return 0;
        }

        // ================= 打开文件 / 文件夹(主人说"不知道在哪换",那就一键打开) =================
        public void OpenTextFile(string which)
        {
            try
            {
                string p = (which == "all") ? TextAllPath : TextOverridePath;
                if (!File.Exists(p)) { Toast("文件还没有: " + Path.GetFileName(p) + "(先在面板点导出/生成模板)"); return; }
                System.Diagnostics.Process.Start("notepad.exe", "\"" + p + "\"");
                Toast("已用记事本打开 " + Path.GetFileName(p));
            }
            catch (Exception e) { Warn("Text/open", e); Toast("打开失败(手动去这个路径): " + TextOverridePath); }
        }

        public void OpenConfigFolder()
        {
            try
            {
                string p = File.Exists(TextOverridePath) ? TextOverridePath : Paths.ConfigPath;
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + p + "\"");
                Toast("已打开配置文件夹");
            }
            catch (Exception e) { Warn("Text/folder", e); Toast("打开失败: " + Paths.ConfigPath); }
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        // ================= 刷新已显示的文本 =================
        // 文档坑 #3:LocalizeAll(false) 里 `LastLocalizedLanguage == CurrentLanguage` 会提前 return → 必须传 true
        public void RefreshLocalizedText()
        {
            try
            {
                LocalizationManager.LocalizeAll(true);
                Toast("界面文本已刷新");
            }
            catch (Exception e) { Warn("Text/localizeall", e); Toast("刷新失败: " + e.Message); }
        }

        // ================= 每帧(懒安装补丁 + 自动导出一次) =================
        public void UpdateText()
        {
            try
            {
                float now = Time.unscaledTime;
                if (now < _textNextTry) return;
                _textNextTry = now + 2f;

                if (!_textPatched && !_textPatchFailed) InstallTextPatch();

                if (!_textAutoDumped)
                {
                    int n = CountTerms();
                    // 术语总数一变就再导一次(语言源/资源是**分批**加载的,一次就够的想法会漏)
                    if (n > 0 && n != _textLastCount && _textAutoDumps < 5)
                    {
                        _textLastCount = n;
                        _textAutoDumps++;
                        DumpAllTerms();
                        _textAutoDumped = true;
                    }
                }
                UpdateTextStatus();
            }
            catch { }
        }

        private int CountTerms()
        {
            try
            {
                var sources = LocalizationManager.Sources;
                if (sources == null) return 0;
                int n = 0;
                for (int i = 0; i < sources.Count; i++)
                {
                    var s = sources[i];
                    if (s == null) continue;
                    var terms = s.mTerms;
                    if (terms != null) n += terms.Count;
                }
                return n;
            }
            catch { return 0; }
        }

        private void InstallTextPatch()
        {
            try
            {
                MethodBase mb = TermTranslationPatch.TargetMethod();
                if (mb == null) { _textPatchFailed = true; Logger.LogWarning("[text] 没找到 I2.Loc.TermData.GetTranslation(int),文本替换不可用"); return; }
                MethodInfo post = AccessTools.Method(typeof(TermTranslationPatch), "Postfix");
                new Harmony("icey.cheatpanel.text").Patch(mb, null, new HarmonyMethod(post), null);
                _textPatched = true;
                TermTranslationPatch.Enabled = _textOn;
                Logger.LogInfo("[text] 补丁已装: " + mb.DeclaringType.Name + "." + mb.Name + "  (文本替换=" + (_textOn ? "开" : "关") + ")");
            }
            catch (Exception e)
            {
                _textPatchFailed = true;
                Logger.LogWarning("[text] 补丁安装失败: " + e.Message);
            }
        }
    }
}
