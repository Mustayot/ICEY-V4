// ICEY Cheat Panel - 外观层(第 11 个 partial 文件)。
//
// 这里只有"外观",没有功能逻辑:
//   * 两套皮肤:最初那套(浅色半透明玻璃,`_uiDark == false`,默认)/ 深色渐变卡片风(`_uiDark == true`);
//   * 深色皮肤用到的调色板、每张卡的主题色相、圆角/渐变绘制辅助;
//   * 自动列流布局 + 鼠标滚轮滚动(只在深色皮肤下启用);
//   * 开发者模式(`_devMode`)的显示过滤:方块人/换角色两张卡 + 一批调试行,非开发者模式完全不出现。
//
// 皮肤选择:Ui.cs 里的绘制代码一律读 EnsureSkin() 算出来的 _cXxx 字段;
// 只有结构性差异(纯色遮罩 vs 竖向渐变、有没有渐变标题条/装饰线/页脚大字)才用 if (_uiDark) 分支。
//
// IMGUI 渐变土办法:先用 start 色铺满(圆角)形状,再用一张"透明→不透明"的辅助纹理染 end 色叠上去,
// 叠加方向由 texCoords 决定;圆角处叠不进去,所以上圆角用 start 兜底、右侧/下侧圆角显式补 end 色。
//
// 三条保守取舍(看不到画面时故意选稳的):
//   * 不用 GUI.BeginGroup/BeginClip 做滚动裁剪(会平移坐标、可能连鼠标命中一起改),
//     改成"画之前把矩形和滚动视口求交",只依赖已经在用的 DrawTexture/DrawTextureWithTexCoords/GUI.Label。
//   * 圆角小方块的纹理一被裁就会拉成椭圆,所以落在视口边界上的圆角整块跳过,不拉伸。
//   * 滚轮方向在不同 Unity 版本上不一致 → 默认"滚轮向上 = 内容向上滚",反了在开发者模式里点一下
//     "滚轮反向"(_scrollInvert)即可,不用重新编译。
//
// 性能:每帧只做几次 float/颜色比较(EnsureSkin 里 _uiDark 没变就直接 return),
//       没有反射、没有 FindObjectsOfType、没有每帧分配数组。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace IceyCheatPanel
{
    public partial class CheatPanelPlugin
    {
        // ===================== 1. 深色调色板(只有 _uiDark=true 那一套用) =====================
        private static readonly Color DkBgTop = RGBA(7, 9, 16, 0.85f);        // 背景渐变:顶(近黑)
        private static readonly Color DkBgBot = RGBA(15, 24, 50, 0.89f);      // 背景渐变:底(深蓝)
        private static readonly Color DkCardBody = RGBA(20, 23, 35, 0.80f);   // 卡片体上缘
        private static readonly Color DkCardBody2 = RGBA(12, 14, 24, 0.86f);  // 卡片体下缘
        private static readonly Color DkCardEdge = RGBA(255, 255, 255, 0.05f); // 卡片淡描边
        private static readonly Color DkCardShadow = RGBA(0, 0, 0, 0.45f);
        private static readonly Color DkRowLine = RGBA(255, 255, 255, 0.055f);// 行分隔细线
        private static readonly Color DkHover = RGBA(255, 255, 255, 0.075f);  // 行 hover 提亮
        private static readonly Color DkText = RGBA(226, 233, 245, 1f);       // 主文字
        private static readonly Color DkTextDim = RGBA(150, 160, 182, 1f);    // 次要文字
        private static readonly Color DkGroup = RGBA(146, 160, 190, 0.80f);   // 分组标题
        private static readonly Color DkOutline = RGBA(0, 0, 0, 0.62f);       // 文字描边
        private static readonly Color DkPanel = RGBA(14, 16, 26, 0.86f);      // 小面板(HUD/Toast)
        private static readonly Color DkPanel2 = RGBA(26, 31, 50, 0.90f);
        private static readonly Color DkSearch = RGBA(10, 12, 20, 0.88f);
        private static readonly Color DkTrack = RGBA(46, 51, 66, 0.92f);      // 滑块轨道(未填充)
        private static readonly Color DkSwOff = RGBA(58, 64, 80, 0.95f);      // 开关关态轨道
        private static readonly Color DkSwThumbOff = RGBA(178, 186, 202, 1f); // 开关关态圆钮
        private static readonly Color DkThumbOn = RGBA(255, 255, 255, 0.98f); // 开关/滑块开态圆钮
        private static readonly Color DkBtn = RGBA(38, 43, 58, 0.92f);        // 圆角按钮
        private static readonly Color DkBtnHi = RGBA(58, 65, 86, 0.95f);
        private static readonly Color DkBtn2 = RGBA(24, 27, 38, 0.95f);
        private static readonly Color DkModal = RGBA(16, 19, 30, 0.97f);
        private static readonly Color DkModal2 = RGBA(30, 36, 58, 0.97f);
        private static readonly Color DkModalEdge = RGBA(255, 255, 255, 0.14f);
        private static readonly Color DkOk1 = RGBA(140, 105, 250, 0.95f);
        private static readonly Color DkOk2 = RGBA(232, 80, 160, 0.95f);
        private static readonly Color DkOkEdge = RGBA(170, 120, 255, 0.55f);
        private static readonly Color DkNoEdge = RGBA(255, 255, 255, 0.12f);
        private static readonly Color DkBarBg = RGBA(11, 13, 21, 0.84f);      // 顶部胶囊条
        private static readonly Color DkBarHi = RGBA(255, 255, 255, 0.10f);
        private static readonly Color DkDev1 = RGBA(255, 168, 60, 0.95f);     // 开发者按钮(激活态)
        private static readonly Color DkDev2 = RGBA(255, 92, 92, 0.95f);
        private static readonly Color DkInv1 = RGBA(120, 200, 255, 0.95f);    // 滚轮反向按钮(激活态)
        private static readonly Color DkInv2 = RGBA(92, 130, 255, 0.95f);

        // 每张卡片的主题色相(参照图里的 紫/粉/蓝/红/青…),10 张卡均匀错开
        private static readonly float[] DkHue = {
            0.98f,  // 0 战斗   玫红
            0.50f,  // 1 玩家   青
            0.76f,  // 2 敌人   紫
            0.10f,  // 3 资源   橙金
            0.58f,  // 4 世界   蓝
            0.34f,  // 5 存档   绿
            0.90f,  // 6 配色   粉
            0.45f,  // 7 文本   青蓝
            0.83f,  // 8 换角色 品红
            0.17f,  // 9 方块人  黄绿
            0.62f   // 10 水印界面 靛蓝
        };
        private static Color[] _themeA, _themeB;   // A=标题条左端/开关亮色,B=标题条右端(偏深)

        private static void EnsureTheme()
        {
            if (_themeA != null) return;
            _themeA = new Color[DkHue.Length];
            _themeB = new Color[DkHue.Length];
            for (int i = 0; i < DkHue.Length; i++)
            {
                float h = DkHue[i];
                _themeA[i] = Color.HSVToRGB(h, 0.66f, 0.94f);
                _themeB[i] = Color.HSVToRGB(Mathf.Repeat(h + 0.07f, 1f), 0.86f, 0.62f);
            }
        }

        private Color CardA(int c)
        {
            EnsureTheme();
            return _themeA[((c % DkHue.Length) + DkHue.Length) % DkHue.Length];
        }

        private Color CardB(int c)
        {
            EnsureTheme();
            return _themeB[((c % DkHue.Length) + DkHue.Length) % DkHue.Length];
        }

        // 亮色版(深色卡片上要够亮)
        private static Color Lighten(Color c, float t)
        {
            Color w = Color.Lerp(c, Color.white, t);
            w.a = c.a;
            return w;
        }

        // ===================== 2. 皮肤选择层(_uiDark:false = 最初那套) =====================
        // Ui.cs 的绘制只读这里的 _cXxx / _trackH / _skHue / _skBtnCenter;
        // 切换主题时重算一次(不是每帧重算,单帧只做两个 bool 比较)。
        private bool _skReady, _skDark;
        private Color _cBg1, _cBg2;              // 背景(深色=渐变两端;最初=纯色遮罩)
        private Color _cCard1, _cCard2, _cHead, _cCardEdge, _cShadow;
        private Color _cHover, _cRowLine;
        private Color _cText, _cTextDim, _cGroup, _cOutline;
        private Color _cTrack, _cThumb; private float _trackH;
        private Color _cSwOff, _cSwOn, _cSwThumbOff, _cSwThumbOn;
        private Color _cBtn, _cBtn2; private float _btnRad;
        private Color _cField, _cBarBg, _cBarHi, _cTab, _cPanel1, _cPanel2;
        private Color _cPlaceholder;
        private bool _skHue;                     // 每卡一个色相?
        private bool _skBtnCenter;               // 动作行画成居中圆角按钮?

        private void EnsureSkin()
        {
            if (_skReady && _skDark == _uiDark) return;
            bool dark = _uiDark;
            if (dark)
            {
                _cBg1 = DkBgTop; _cBg2 = DkBgBot;
                _cCard1 = DkCardBody; _cCard2 = DkCardBody2; _cHead = DkCardBody; _cCardEdge = DkCardEdge;
                _cShadow = DkCardShadow; _cHover = DkHover; _cRowLine = DkRowLine;
                _cText = DkText; _cTextDim = DkTextDim; _cGroup = DkGroup; _cOutline = DkOutline;
                _cTrack = DkTrack; _trackH = 6f * S; _cThumb = DkThumbOn;
                _cSwOff = DkSwOff; _cSwOn = DkSwOff; _cSwThumbOff = DkSwThumbOff; _cSwThumbOn = DkThumbOn;
                _cBtn = DkBtn; _cBtn2 = DkBtn2; _btnRad = 7f * S; _skBtnCenter = true;
                _cField = DkSearch; _cBarBg = DkBarBg; _cBarHi = DkBarHi; _cTab = DkBarHi;
                _cPanel1 = DkPanel; _cPanel2 = DkPanel2;
                _cPlaceholder = RGBA(150, 160, 182, 0.60f);
                _skHue = true;
            }
            else
            {
                // 最初那套:浅色半透明玻璃 —— 颜色就是 CheatPanelPlugin.cs 里原有那份调色板
                _cBg1 = ColOverlay; _cBg2 = ColOverlay;
                _cCard1 = ColBody; _cCard2 = ColBody; _cHead = ColHeader; _cCardEdge = new Color(0f, 0f, 0f, 0f);
                _cShadow = ColShadow; _cHover = ColHover; _cRowLine = new Color(0f, 0f, 0f, 0f);
                _cText = ColText; _cTextDim = ColTextDim; _cGroup = ColGroup; _cOutline = ColOutline;
                _cTrack = ColSlideTrack; _trackH = 3f * S; _cThumb = ColAccent;
                _cSwOff = ColSwTrackOff; _cSwOn = ColSwTrackOn; _cSwThumbOff = ColSwThumbOff; _cSwThumbOn = ColAccent;
                _cBtn = ColSlideTrack; _cBtn2 = ColSlideTrack; _btnRad = 4f * S; _skBtnCenter = false;
                _cField = ColSearch; _cBarBg = ColTabsBg; _cBarHi = ColTabHover; _cTab = ColTabActive;
                _cPanel1 = ColHover; _cPanel2 = ColHover;
                _cPlaceholder = RGBA(74, 44, 58, 0.38f);
                _skHue = false;
            }
            bool wasDark = _skReady && _skDark;
            _skReady = true; _skDark = dark;
            ApplyStyleColors();
            // 换主题时各自回到自己的默认摆位(两套布局范式不同,直接复用会串味)
            if (!dark && wasDark) RestoreLightLayout();
            else if (dark && !wasDark) _flowOn = true;     // 切到深色:回到自动列流(卡片墙)
        }

        // 样式里的文字颜色(TextField 用的是 normal/focused,必须跟着主题走)
        private void ApplyStyleColors()
        {
            Paint(_stTitle, _cText); Paint(_stTitleB, Color.white);
            Paint(_stMod, _cTextDim); Paint(_stSmall, _cText); Paint(_stCenter, _cText);
            Paint(_stTab, _cTextDim); Paint(_stSearch, _cText); Paint(_stGroup, _cGroup);
            Paint(_stIcon, _cTextDim); Paint(_stWrap, _cTextDim); Paint(_stBig, _cText);
        }

        private static void Paint(GUIStyle st, Color c)
        {
            if (st == null) return;
            st.normal.textColor = c;
            st.hover.textColor = c;
            st.active.textColor = c;
            st.focused.textColor = c;
        }

        // 最初主题的卡片摆位(3 列 x N 行);只动坐标,不动 _cardOpen
        private void RestoreLightLayout()
        {
            for (int c = 0; c < CardCount; c++)
            {
                int col = c % 3, row = c / 3;
                _cardX[c] = (20f + col * (CardW + 12f)) * S;
                _cardY[c] = (92f + row * 44f) * S;
            }
            _scrollY = 0f;
        }

        // ---- 强调色(深色=每卡色相;最初=统一的粉) ----
        private Color AccentOf(int c) { return _skHue ? Lighten(CardA(c), 0.42f) : ColAccent; }   // 开关打开时的功能名
        private Color AccentSoft(int c) { return _skHue ? CardA(c) : ColAccent; }                  // 滑块填充/主按钮
        private Color AccentEnd(int c) { return _skHue ? CardB(c) : ColAccent; }                   // 渐变末端

        private Color ChevronCol(int c, bool expanded)
        {
            if (!_skHue) return expanded ? ColAccent : ColTextDim;
            return expanded ? Lighten(CardA(c), 0.20f) : DkTextDim;
        }

        private Color ResetIconCol(int c)
        {
            // 最初那套原本是 Mul(ColTextDim, a * 1.15f),这里保留那个亮度
            return _skHue ? Lighten(CardA(c), 0.25f) : Mul(ColTextDim, 1.15f);
        }

        // ===================== 3. 开发者模式过滤 =====================
        // 非开发者模式:两张实验卡片 + 一批调试行完全不出现(不是灰掉);布局也当它们不存在。
        // 用"包含"匹配,免得以后改名字就漏掉。
        public bool _scrollInvert;          // ★ 滚轮反向(开发者模式里可切;默认 false = 滚轮向上→内容向上)
        private static readonly string[] DevOnlyRowKeys = {
            "去剑光", "藏特效半径", "列出场景渲染器", "骨点叠加",
            "重新扫描骨架", "导出骨架信息", "强制恢复原角色"
        };

        private static bool IsDevOnlyRow(Mod m)
        {
            if (m == null || string.IsNullOrEmpty(m.Name)) return false;
            for (int i = 0; i < DevOnlyRowKeys.Length; i++)
                if (m.Name.IndexOf(DevOnlyRowKeys[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private bool RowShown(Mod m) { return _devMode || !IsDevOnlyRow(m); }

        private bool CardShown(int c)
        {
            if (_devMode) return true;
            string n = CatName[c];
            return !(n == "换角色" || n == "方块人");
        }

        // ===================== 4. 滚动视口与裁剪 =====================
        private const float DkViewTopY = 92f * S;    // 内容视口上边界(工具条+搜索框之下)
        private const float DkFooterH = 34f * S;     // 底部大标题占的高度(只有深色皮肤有)
        private const float DkScrollStep = 46f * S;  // 一格滚轮的像素

        private Rect _view;            // 内容视口(屏幕坐标)
        private float _scrollY;        // 当前纵向滚动量
        private float _contentH;       // 内容总高度(用于算滚动上限)
        private bool _clipOn;          // true 时所有绘制/命中都裁到 _view(只在深色皮肤画卡片时打开)
        private bool _flowOn = true;   // 自动列流布局;用户一旦拖动卡片就关闭,改用原坐标体系

        private void RefreshView()
        {
            float bot = Screen.height - (_uiDark ? DkFooterH : 0f);
            _view = new Rect(0f, DkViewTopY, Screen.width, Mathf.Max(80f, bot - DkViewTopY));
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            float x0 = Mathf.Max(a.x, b.x), x1 = Mathf.Min(a.xMax, b.xMax);
            float y0 = Mathf.Max(a.y, b.y), y1 = Mathf.Min(a.yMax, b.yMax);
            if (x1 <= x0 || y1 <= y0) return new Rect(0f, 0f, 0f, 0f);
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        // 命中/绘制前统一裁一次:鼠标在工具条上时不会误触卡片
        private Rect ViewClip(Rect r)
        {
            return _clipOn ? Intersect(r, _view) : r;
        }

        private bool FullyInside(Rect r)
        {
            return !_clipOn || (r.x >= _view.x && r.y >= _view.y && r.xMax <= _view.xMax && r.yMax <= _view.yMax);
        }

        // ===================== 5. 绘制原语(全部带裁剪) =====================
        // 渐变辅助纹理:_texFadeV2 顶透明→底不透明,_texFadeH2 左透明→右不透明
        // (Theme.cs 里的 _texFade 是"顶不透明→底透明",正好用于铺底色的反方向叠加,背景渐变复用它。)
        private Texture2D _texFadeV2, _texFadeH2;

        private void EnsureFade2()
        {
            if (_texFadeV2 == null)
            {
                _texFadeV2 = new Texture2D(1, 64, TextureFormat.RGBA32, false);
                for (int y = 0; y < 64; y++)
                    _texFadeV2.SetPixel(0, y, new Color(1f, 1f, 1f, 1f - (float)y / 63f));
                _texFadeV2.wrapMode = TextureWrapMode.Clamp;
                _texFadeV2.filterMode = FilterMode.Bilinear;
                _texFadeV2.Apply(false, false);
            }
            if (_texFadeH2 == null)
            {
                _texFadeH2 = new Texture2D(64, 1, TextureFormat.RGBA32, false);
                for (int x = 0; x < 64; x++)
                    _texFadeH2.SetPixel(x, 0, new Color(1f, 1f, 1f, (float)x / 63f));
                _texFadeH2.wrapMode = TextureWrapMode.Clamp;
                _texFadeH2.filterMode = FilterMode.Bilinear;
                _texFadeH2.Apply(false, false);
            }
        }

        private void Fill(Rect r, Color c)
        {
            if (r.width <= 0f || r.height <= 0f || c.a <= 0.001f) return;
            Rect q = ViewClip(r);
            if (q.width <= 0f || q.height <= 0f) return;
            GUI.color = c;
            GUI.DrawTexture(q, _texWhite);
            GUI.color = Color.white;
        }

        private void RQuad(Rect r, int q, Color c)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            // 圆角是一小块圆形贴图,被裁一点就会拉成椭圆 → 只在完整可见时才画
            if (!FullyInside(r)) return;
            GUI.color = c;
            float u = (q == 1 || q == 3) ? 0.5f : 0f;
            float v = (q >= 2) ? 0f : 0.5f;
            GUI.DrawTextureWithTexCoords(r, _texCircle, new Rect(u, v, 0.5f, 0.5f));
            GUI.color = Color.white;
        }

        private void RCircle(Rect r, Color c)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            if (!FullyInside(r)) return;
            GUI.color = c;
            GUI.DrawTexture(r, _texCircle);
            GUI.color = Color.white;
        }

        // 圆角矩形(shape: 0=四角全圆 1=只圆上面两角)
        private void RRectEx(Rect r, float rad, Color c, int shape)
        {
            if (r.width <= 0f || r.height <= 0f || c.a <= 0.001f) return;
            rad = Mathf.Min(rad, Mathf.Min(r.width, r.height) * 0.5f);
            if (rad <= 0.5f) { Fill(r, c); return; }
            if (shape == 1)
            {
                // 上半部:圆角带(左右留出角块) + 中间竖条
                Fill(new Rect(r.x + rad, r.y, r.width - 2f * rad, rad), c);
                RQuad(new Rect(r.x, r.y, rad, rad), 0, c);
                RQuad(new Rect(r.x + r.width - rad, r.y, rad, rad), 1, c);
                // 下半部:整宽方角
                Fill(new Rect(r.x, r.y + rad, r.width, r.height - rad), c);
            }
            else
            {
                Fill(new Rect(r.x + rad, r.y, r.width - 2f * rad, r.height), c);
                Fill(new Rect(r.x, r.y + rad, rad, r.height - 2f * rad), c);
                Fill(new Rect(r.x + r.width - rad, r.y + rad, rad, r.height - 2f * rad), c);
                RQuad(new Rect(r.x, r.y, rad, rad), 0, c);
                RQuad(new Rect(r.x + r.width - rad, r.y, rad, rad), 1, c);
                RQuad(new Rect(r.x, r.y + r.height - rad, rad, rad), 2, c);
                RQuad(new Rect(r.x + r.width - rad, r.y + r.height - rad, rad, rad), 3, c);
            }
            GUI.color = Color.white;
        }

        private void RRect(Rect r, float rad, Color c) { RRectEx(r, rad, c, 0); }
        private void RRectTop(Rect r, float rad, Color c) { RRectEx(r, rad, c, 1); }

        // 把 end 色按"透明→不透明"叠到 sub 上(gTop/gBot 是整个渐变块的屏幕 y 范围)
        private void FadeV(Rect sub, float gTop, float gBot, Color c)
        {
            Rect q = ViewClip(sub);
            if (q.width <= 0f || q.height <= 0f) return;
            float H = Mathf.Max(1f, gBot - gTop);
            float v0 = Mathf.Clamp01(1f - (q.yMax - gTop) / H);   // 底边
            float v1 = Mathf.Clamp01(1f - (q.y - gTop) / H);      // 顶边
            if (v1 - v0 <= 0.002f) return;
            EnsureFade2();
            GUI.color = c;
            GUI.DrawTextureWithTexCoords(q, _texFadeV2, new Rect(0f, v0, 1f, v1 - v0));
            GUI.color = Color.white;
        }

        private void FadeH(Rect sub, float gL, float gR, Color c)
        {
            Rect q = ViewClip(sub);
            if (q.width <= 0f || q.height <= 0f) return;
            float W = Mathf.Max(1f, gR - gL);
            float u0 = Mathf.Clamp01((q.x - gL) / W);
            float u1 = Mathf.Clamp01((q.xMax - gL) / W);
            if (u1 - u0 <= 0.002f) return;
            EnsureFade2();
            GUI.color = c;
            GUI.DrawTextureWithTexCoords(q, _texFadeH2, new Rect(u0, 0f, u1 - u0, 1f));
            GUI.color = Color.white;
        }

        // 圆角渐变块:start → end(horiz=false 竖向,true 横向;shape: 0=全圆角 1=只圆上角)
        private void RGradEx(Rect r, float rad, Color start, Color end, float alpha, bool horiz, int shape)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            Color cs = Mul(start, alpha), ce = Mul(end, alpha);
            float rr = Mathf.Min(rad, Mathf.Min(r.width, r.height) * 0.5f);
            RRectEx(r, rr, cs, shape);                       // 底:start 铺满形状(圆角随之正确)
            if (rr <= 0.5f)
            {
                if (horiz) FadeH(r, r.x, r.xMax, ce); else FadeV(r, r.y, r.yMax, ce);
                return;
            }
            if (horiz)
            {
                FadeH(new Rect(r.x + rr, r.y, r.width - 2f * rr, r.height), r.x, r.xMax, ce);
                RQuad(new Rect(r.x + r.width - rr, r.y, rr, rr), 1, ce);            // 右上角补 end
                if (shape == 0) RQuad(new Rect(r.x + r.width - rr, r.y + r.height - rr, rr, rr), 3, ce);
            }
            else
            {
                float y1 = (shape == 1) ? r.yMax : r.yMax - rr;
                FadeV(new Rect(r.x, r.y + rr, r.width, y1 - (r.y + rr)), r.y, r.yMax, ce);
                FadeV(new Rect(r.x + rr, r.y, r.width - 2f * rr, rr), r.y, r.yMax, ce);
                if (shape == 0)
                {
                    RQuad(new Rect(r.x, r.y + r.height - rr, rr, rr), 2, ce);        // 左下角补 end
                    RQuad(new Rect(r.x + r.width - rr, r.y + r.height - rr, rr, rr), 3, ce);
                }
            }
        }

        private void RGrad(Rect r, float rad, Color start, Color end, float alpha, bool horiz)
        { RGradEx(r, rad, start, end, alpha, horiz, 0); }

        private void RGradTop(Rect r, float rad, Color start, Color end, float alpha, bool horiz)
        { RGradEx(r, rad, start, end, alpha, horiz, 1); }

        // 全屏背景:_uiDark=true → 近黑(上)→深蓝(下)竖向渐变;
        //           _uiDark=false → 最初那套纯色半透明遮罩(alpha ≈0.22)
        private void DrawPanelBg(float a)
        {
            Rect r = new Rect(0f, 0f, Screen.width, Screen.height);
            Fill(r, Mul(_cBg2, a));
            if (!_uiDark) return;
            Color t = Mul(_cBg1, a);
            EnsureWm();                                   // _texFade 懒建(Theme.cs 里的 1×64)
            GUI.color = t;
            GUI.DrawTextureWithTexCoords(r, _texFade, new Rect(0f, 0f, 1f, 1f));
            GUI.color = Color.white;
        }

        // ===================== 6. 文字 =====================
        // 带描边 + 裁切的文字(深色皮肤下矩形被滚动裁掉一半时,文字不会溢出到工具条上)
        private void RText(Rect r, string s, GUIStyle st, Color col, bool outline, float a)
        {
            if (string.IsNullOrEmpty(s) || st == null) return;
            Rect q = ViewClip(r);
            if (q.width <= 0f || q.height <= 0f) return;
            st.clipping = TextClipping.Clip;
            Color c = Mul(col, a);
            if (outline)
            {
                Color o = Mul(_cOutline, a);
                st.normal.textColor = o;
                GUI.Label(new Rect(q.x - 1f, q.y, q.width, q.height), s, st);
                GUI.Label(new Rect(q.x + 1f, q.y, q.width, q.height), s, st);
                GUI.Label(new Rect(q.x, q.y - 1f, q.width, q.height), s, st);
                GUI.Label(new Rect(q.x, q.y + 1f, q.width, q.height), s, st);
            }
            st.normal.textColor = c;
            GUI.Label(q, s, st);
        }

        // 逐字渐变文字(IMGUI 一次只能一种颜色 → 拆字;Theme.cs 的 GradLabel 固定用水印字号,这里要能换字号)
        // align: 0=左 1=中 2=右
        private void GradText(Rect r, string s, GUIStyle st, Color ca, Color cb, float alpha, int align, bool outline)
        {
            if (string.IsNullOrEmpty(s) || st == null) return;
            Rect q = ViewClip(r);
            if (q.width <= 0f || q.height <= 0f) return;
            float total = 0f;
            float[] ws = new float[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                ws[i] = st.CalcSize(new GUIContent(s[i].ToString())).x;
                total += ws[i];
            }
            float x = q.x;
            if (align == 1) x = q.x + (q.width - total) * 0.5f;
            else if (align == 2) x = q.xMax - total;
            st.clipping = TextClipping.Clip;
            st.alignment = TextAnchor.MiddleLeft;
            for (int i = 0; i < s.Length; i++)
            {
                float t = s.Length <= 1 ? 0f : (float)i / (s.Length - 1);
                Color c = Color.Lerp(ca, cb, t);
                if (outline)
                {
                    st.normal.textColor = Mul(_cOutline, alpha);
                    GUI.Label(new Rect(x + 1f, q.y + 1f, ws[i] + 2f, q.height), s[i].ToString(), st);
                }
                c.a *= alpha;
                st.normal.textColor = c;
                GUI.Label(new Rect(x, q.y, ws[i] + 2f, q.height), s[i].ToString(), st);
                x += ws[i];
            }
        }

        // 底部一行巨大的渐变标题字(只有深色皮肤才有)
        private void DrawFooterTitle(float a)
        {
            if (!_uiDark) return;
            Rect r = new Rect(0f, Screen.height - DkFooterH, Screen.width, DkFooterH);
            RGradEx(new Rect(Screen.width * 0.20f, r.y + 2f * S, Screen.width * 0.60f, 1.5f), 0.75f,
                RGBA(150, 120, 255, 0.55f), RGBA(255, 90, 165, 0.40f), a, true, 0);
            if (_stBig == null)
            {
                _stBig = NewStyle(24f * S, TextAnchor.MiddleCenter, DkText);
                _stBig.fontStyle = FontStyle.Bold;
            }
            GradText(new Rect(0f, r.y + 3f * S, Screen.width, DkFooterH - 3f * S), "ICEY 作弊面板", _stBig,
                RGBA(178, 150, 255, 1f), RGBA(255, 128, 190, 1f), a * 0.85f, 1, true);
        }

        // ===================== 7. 自动列流布局(只有深色皮肤用) =====================
        // 列数按屏幕宽度自动算(2560 宽 → 6 列;最少 3 列),卡片在列内自上而下堆叠,超高就整页滚。
        // 卡片高度按"目标展开态"算(不用动画中的高度),这样展开动画期间列不会跳。
        // 被开发者模式藏起来的卡片当作不存在(否则会留空位)。
        private void FlowLayout()
        {
            float gap = 12f * S;
            float cw = CardW * S;
            float availW = Screen.width - 40f * S;
            int cols = Mathf.FloorToInt((availW + gap + 0.5f) / (cw + gap));   // +0.5px 容差,避免浮点误差少一列
            cols = Mathf.Clamp(cols, 3, CardCount);
            float used = cols * cw + (cols - 1) * gap;
            float x0 = Mathf.Max(20f * S, (Screen.width - used) * 0.5f);

            float[] colY = new float[cols];
            for (int i = 0; i < cols; i++) colY[i] = DkViewTopY;
            float maxY = DkViewTopY;
            int n = 0;
            for (int c = 0; c < CardCount; c++)
            {
                if (!CardShown(c)) continue;      // 藏起来的卡片不排版,也不改它的坐标(免得存盘存了个哨兵值)
                int col = n % cols;
                _cardX[c] = x0 + col * (cw + gap);
                _cardY[c] = colY[col];
                colY[col] += HeadH * S + BodyHFor(c, _cardOpen[c]) + gap * 0.75f;
                if (colY[col] > maxY) maxY = colY[col];
                n++;
            }
            _contentH = maxY - DkViewTopY + 8f * S;
            _scrollY = Mathf.Clamp(_scrollY, 0f, Mathf.Max(0f, _contentH - _view.height));
        }

        // 展开态下正文(含分组标题)的高度 —— 被藏起来的行不算
        private float BodyHFor(int c, bool expanded)
        {
            if (!expanded) return 0f;
            float h = 0f;
            string last = null;
            for (int i = 0; i < Mods[c].Count; i++)
            {
                Mod m = Mods[c][i];
                if (!RowShown(m)) continue;
                if (m.Group.Length > 0 && m.Group != last) { h += GrpH * S; last = m.Group; }
                h += RowHFor(m);
            }
            return h;
        }

        // 用户拖动过卡片之后用回原坐标体系(手动摆位),但仍然允许滚动
        private float ManualContentH()
        {
            float max = 0f;
            for (int c = 0; c < CardCount; c++)
            {
                if (!CardShown(c)) continue;
                float bottom = _cardY[c] + HeadH * S + BodyHFor(c, _cardOpen[c]) - DkViewTopY;
                if (bottom > max) max = bottom;
            }
            return max + 8f * S;
        }

        private void ScrollInput()
        {
            float max = Mathf.Max(0f, _contentH - _view.height);
            if (max <= 0f) { _scrollY = 0f; return; }
            Event e = Event.current;
            if (e != null && e.type == EventType.ScrollWheel && _dragCard < 0 && _view.Contains(e.mousePosition))
            {
                // 假设 Event.delta.y > 0 = 滚轮向上 → 内容向上滚(直觉方向);
                // 反了就在开发者模式里点一下"滚轮反向"(_scrollInvert),不用重新编译。
                float dir = _scrollInvert ? 1f : -1f;
                _scrollY += e.delta.y * DkScrollStep * dir;
                e.Use();
            }
            _scrollY = Mathf.Clamp(_scrollY, 0f, max);
        }

        // 卡片被拖到"整张都在视口外"(完全看不见)时拉回来,避免拖丢了找不回来。
        // 只在这种情况下动坐标 —— 正常拖动、吸附、Shift 自由摆放的逻辑一行没改。
        private void KeepCardReachable(int c, float totalH, float w)
        {
            Rect r = new Rect(_cardX[c], _cardY[c] - _scrollY, w, Mathf.Max(totalH, HeadH * S));
            if (Intersect(r, _view).height > 0.5f) return;
            _cardY[c] = _scrollY + DkViewTopY + 6f * S;
            _cardX[c] = Mathf.Clamp(_cardX[c], 10f * S, Mathf.Max(10f * S, Screen.width - w - 10f * S));
        }

        // ===================== 8. 滚动指示条 / 开发按钮 =====================
        // 内容超出视口时才有的滚动提示(两套皮肤都有,样式各自不同):
        //   深色 = 右侧带轨道的圆角细条;
        //   最初那套 = 贴右边缘的 3px 极细淡色条(无圆角/无阴影/不用强调色)——
        //   纯粹是"可发现性":能滚的时候要看得出来,不然主人会以为选项丢了。
        private void DrawScrollBar(float a)
        {
            float max = _contentH - _view.height;
            if (max <= 0.5f || _view.height <= 1f) return;
            float frac = Mathf.Clamp01(_view.height / Mathf.Max(1f, _contentH));
            float prog = Mathf.Clamp01(_scrollY / max);

            if (_uiDark)
            {
                float w = 3f * S;
                Rect track = new Rect(Screen.width - 8f * S, _view.y + 6f * S, w, _view.height - 12f * S);
                RRect(track, w * 0.5f, Mul(RGBA(255, 255, 255, 0.10f), a));
                float th = Mathf.Max(24f * S, track.height * frac);
                float ty = track.y + (track.height - th) * prog;
                RRect(new Rect(track.x, ty, w, th), w * 0.5f, Mul(RGBA(190, 205, 245, 0.45f), a));
            }
            else
            {
                float w = 3f;                                     // 宽 3px
                float x = Screen.width - 3f - w;                  // 离视口右边缘 3px
                float th = Mathf.Max(18f, _view.height * frac);
                float ty = _view.y + (_view.height - th) * prog;
                Fill(new Rect(x, ty, w, th), Mul(ColTextDim, 0.30f * a));   // 低调淡色,不加圆角/阴影
            }
        }

        // 顶部胶囊条左边的「开发」按钮(主人要的"正上方开一个")。
        // 开着 = 明显暖色渐变 + 白点;关着 = 跟工具条同色的低调按钮。
        // 开发者模式还多一个"滚轮反向"按钮(滚轮方向不用重新编译就能试)。
        private void DrawDevButton(Rect bar, float a)
        {
            float bw = 64f * S, bh = bar.height, gap = 10f * S;
            Rect d = new Rect(Mathf.Max(8f * S, bar.x - gap - bw), bar.y, bw, bh);
            if (_devMode)
            {
                RGrad(d, bh * 0.5f, Mul(DkDev1, a), Mul(DkDev2, a), 1f, true);
                RCircle(new Rect(d.x + 8f * S, d.center.y - 3f * S, 6f * S, 6f * S), Mul(Color.white, a));
            }
            else
            {
                RRect(d, bh * 0.5f, Mul(_cBarBg, a));
            }
            RText(new Rect(d.x + 6f * S, d.y, d.width - 10f * S, d.height), "开发", _stTab,
                _devMode ? Color.white : _cTextDim, _devMode, a);
            if (GUI.Button(d, GUIContent.none, GUIStyle.none)) { _devMode = !_devMode; Save(); }

            if (!_devMode) return;
            float iw = 76f * S;
            Rect inv = new Rect(Mathf.Max(8f * S, d.x - gap - iw), bar.y, iw, bh);
            if (_scrollInvert) RGrad(inv, bh * 0.5f, Mul(DkInv1, a), Mul(DkInv2, a), 1f, true);
            else RRect(inv, bh * 0.5f, Mul(_cBarBg, a));
            RText(inv, "滚轮反向", _stTab, _scrollInvert ? Color.white : _cTextDim, true, a);
            if (GUI.Button(inv, GUIContent.none, GUIStyle.none)) _scrollInvert = !_scrollInvert;
        }
    }
}
