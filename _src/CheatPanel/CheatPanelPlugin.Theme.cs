// ICEY Cheat Panel - 右上角"已开启功能"渐变水印 + 渐变绘制小工具。partial class 第九个文件。
//
// 主人要的效果(参照图):一堆功能名竖着排在屏幕右上角,每个名字自带**渐变颜色**,开启的功能才出现。
//
// IMGUI 里做渐变的三个土办法(都用上了):
//   1) **色块渐变**:先铺 A 色整块,再叠一张"上不透明→下透明"的 1×64 白纹理染 B 色 → 看起来就是 A→B。
//   2) **文字渐变**:IMGUI 一次只能给一种颜色 → 逐字画,每个字按它在字符串里的位置取色(短标签够用)。
//   3) 颜色缓慢流动:`_wmPhase` 随时间推移整体色相,看着"活"。

using System;
using UnityEngine;

namespace IceyCheatPanel
{
    public partial class CheatPanelPlugin
    {
        public bool _wmOn = true;          // 右上角水印开关
        public float _wmScale = 1f;        // 水印大小
        public bool _wmAll;                // 连"未开启"的也列出来(灰着)
        public bool _wmEn;                 // ★ 水印用英文
        public static readonly string[] WmLangNames = { "中文", "English" };

        private GUIStyle _stWm;
        private Texture2D _texFade;        // 1×64:上不透明 → 下透明
        private float _wmPhase;

        private void EnsureWm()
        {
            if (_stWm == null)
            {
                _stWm = new GUIStyle(GUI.skin.label);
                _stWm.alignment = TextAnchor.MiddleRight;
                _stWm.fontSize = Mathf.RoundToInt(15f * _wmScale);
                _stWm.fontStyle = FontStyle.Bold;
                _stWm.clipping = TextClipping.Clip;
            }
            if (_texFade == null)
            {
                _texFade = new Texture2D(1, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++)
                {
                    float t = 1f - (float)y / 63f;             // 顶 1 → 底 0
                    _texFade.SetPixel(0, y, new Color(1f, 1f, 1f, t));
                }
                _texFade.wrapMode = TextureWrapMode.Clamp;
                _texFade.filterMode = FilterMode.Bilinear;
                _texFade.Apply(false, false);
            }
        }

        // A→B 的竖向渐变块(两块叠出来,零 shader 成本)
        private void GradV(Rect r, Color a, Color b, float alpha)
        {
            Color ca = a; ca.a *= alpha;
            GUI.color = ca;
            GUI.DrawTexture(r, _texWhite);
            EnsureWm();
            Color cb = b; cb.a *= alpha;
            GUI.color = cb;
            _stWm.alignment = _stWm.alignment;      // 不动
            GUI.DrawTexture(r, _texFade);           // 叠一层:上 B 下透明
            GUI.color = Color.white;
        }

        // 逐字渐变文字(IMGUI 一次只能一种颜色 → 拆字)
        private void GradLabel(Rect r, string s, Color a, Color b, float alpha, bool rightAlign)
        {
            if (string.IsNullOrEmpty(s)) return;
            EnsureWm();
            float total = 0f;
            float[] ws = new float[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                ws[i] = _stWm.CalcSize(new GUIContent(s[i].ToString())).x;
                total += ws[i];
            }
            float x = rightAlign ? (r.x + r.width - total) : r.x;
            for (int i = 0; i < s.Length; i++)
            {
                float t = s.Length <= 1 ? 0f : (float)i / (s.Length - 1);
                Color c = Color.Lerp(a, b, t);
                // 先画 1px 暗描边,再画本体 → 深色背景下也能看清
                GUI.color = new Color(0f, 0f, 0f, 0.75f * alpha);
                GUI.Label(new Rect(x + 1f, r.y + 1f, ws[i] + 2f, r.height), s[i].ToString(), _stWm);
                c.a *= alpha;
                GUI.color = c;
                GUI.Label(new Rect(x, r.y, ws[i] + 2f, r.height), s[i].ToString(), _stWm);
                x += ws[i];
            }
            GUI.color = Color.white;
        }

        // ================= 右上角水印 =================
        private void DrawWatermark()
        {
            if (!_wmOn) return;
            try
            {
                EnsureWm();
                _wmPhase += Time.unscaledDeltaTime * 0.08f;
                if (_wmPhase > 1f) _wmPhase -= 1f;

                float lineH = 19f * _wmScale;
                float w = 260f * _wmScale;
                float x = Screen.width - w - 26f;
                float y = 26f;

                int shown = 0;
                for (int i = 0; i < WmList.Length; i++)
                {
                    bool on; string txt;
                    if (!WmGet(i, out on, out txt)) continue;
                    if (!on && !_wmAll) continue;
                    float hue = Mathf.Repeat(_wmPhase + i * 0.11f, 1f);
                    Color ca = Color.HSVToRGB(hue, 0.85f, 1f);
                    Color cb = Color.HSVToRGB(Mathf.Repeat(hue + 0.13f, 1f), 0.85f, 1f);
                    float al = on ? 0.95f : 0.35f;

                    // 右侧竖条:一眼看出有没有开
                    GradV(new Rect(Screen.width - 16f, y + 3f, 4f * _wmScale, lineH - 6f), ca, cb, al);
                    // 功能名(右对齐,逐字渐变)
                    GradLabel(new Rect(x, y, w, lineH), txt, ca, cb, al, true);
                    y += lineH;
                    shown++;
                    if (shown >= 22) break;
                }
            }
            catch { }
        }

        // 水印清单:名字 + 是否开启(数据从插件自己的状态字段来)
        private static readonly string[] WmList = {
            "无敌", "无限能量", "一击必杀", "无限闪现", "无限连击", "连击显示", "DPS 显示",
            "攻击力倍率", "移速倍率", "跳跃倍率", "体型倍率", "重力倍率", "时间流速",
            "敌人不攻击", "冻结敌人", "穿墙", "敌人 AI 关闭", "碰撞盒", "自由相机", "画面去色",
            "配色替换", "MC 方块人", "MC 攻击方式", "文本替换", "借敌人骨架"
        };

        private bool WmGet(int i, out bool on, out string txt)
        {
            on = false; txt = _wmEn ? WmListEn[i] : WmList[i];
            float f = 1f;
            switch (i)
            {
                case 0: on = _godMode; break;
                case 1: on = _infiniteEnergy; break;
                case 2: on = _oneHitKill; break;
                case 3: on = _infFlash; break;
                case 4: on = _infCombo; break;
                case 5: on = _comboHud; break;
                case 6: on = _dpsOn; break;
                case 7: f = _atkMult; on = f != 1f; txt = (_wmEn ? "ATK ×" : "攻击力 ×") + f.ToString("F1"); break;
                case 8: f = _moveMult; on = f != 1f; txt = (_wmEn ? "SPEED ×" : "移速 ×") + f.ToString("F2"); break;
                case 9: f = _jumpMult; on = f != 1f; txt = (_wmEn ? "JUMP ×" : "跳跃 ×") + f.ToString("F2"); break;
                case 10: f = _scaleMult; on = f != 1f; txt = (_wmEn ? "SCALE ×" : "体型 ×") + f.ToString("F2"); break;
                case 11: f = _gravMult; on = f != 1f; txt = (_wmEn ? "GRAVITY ×" : "重力 ×") + f.ToString("F2"); break;
                case 12: on = _timeScaleOn; txt = (_wmEn ? "TIME ×" : "时间流速 ×") + _timeScale.ToString("F2"); break;
                case 13: on = _noEnemyAtk; break;
                case 14: on = _freezeEnemy; break;
                case 15: on = _passWall; break;
                case 16: on = _aiOff; break;
                case 17: on = _boundsOn; break;
                case 18: on = _camFree; break;
                case 19: on = _glassOn; break;
                case 20: on = _skinOn; break;
                case 21: on = _blockyOn; break;
                case 22: on = _mcAtkMode; break;
                case 23: on = _textOn; break;
                case 24: on = _borrowOn; break;
                default: return false;
            }
            return true;
        }

        // 英文清单(和 WmList 一一对应,顺序不能乱)
        private static readonly string[] WmListEn = {
            "GOD MODE", "INF ENERGY", "ONE-HIT KILL", "INF DASH", "INF COMBO", "COMBO HUD", "DPS METER",
            "ATK MULT", "SPEED MULT", "JUMP MULT", "SCALE MULT", "GRAVITY MULT", "TIME SCALE",
            "NO ENEMY ATK", "FREEZE ENEMY", "NO CLIP", "ENEMY AI OFF", "HITBOX", "FREE CAM", "GRAYSCALE",
            "SKIN RECOLOR", "MC BLOCKY", "MC ATTACK", "TEXT OVERRIDE", "BORROW SKELETON"
        };
    }
}
