// ICEY Cheat Panel - UI layer (partial class). Core logic lives in CheatPanelPlugin.cs.
// Layout: N collapsible floating cards + capsule category bar + search + confirm modal.
// 两套外观(只换皮,不换逻辑):
//   _uiDark == false(默认)= 最初那套:纯色半透明遮罩 + 卡片用保存的原始摆位(3 列)+ 平色标题条;
//   _uiDark == true        = 深色渐变卡片风:深色渐变背景 + 自动多列 + 滚轮滚动 + 每卡主题色渐变。
//   _devMode == false(默认)时「方块人」「换角色」两张卡与一批调试行完全不出现。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace IceyCheatPanel
{
    public partial class CheatPanelPlugin
    {
        private Font _font;
        private bool _fontTried, _stylesReady;
        private GUIStyle _stTitle, _stTitleB, _stMod, _stSmall, _stCenter, _stSearch, _stTab, _stGroup, _stIcon, _stWrap, _stBig;
        private bool _hasResetGlyph;
        private Texture2D _texWhite, _texCircle;
        // 敌人受击框的"快照中心 → 实时位置"补偿表(键是 attribute 对象本身,别用游戏类型当字段类型)
        private readonly Dictionary<object, Vector3> _enemyBoxOffset = new Dictionary<object, Vector3>();

        // ================= OnGUI =================
        private void OnGUI()
        {
            if (!_stylesReady) BuildStyles();
            EnsureSkin();      // 两套皮肤(_uiDark:false = 最初那套);只在主题切换时重算配色
            _clipOn = false;   // 防御:工具条/HUD/Toast/弹窗一律不裁(只在画卡片那一段打开)

            // 右上角"已开启功能"渐变水印:面板完全关着时画在这里(此时不会有深色背景盖住它)
            if (_fade <= 0.001f) DrawWatermark();

            if (_boundsOn && !_open) DrawBounds();
            if ((_hud || _dpsOn || _comboHud) && !_open) DrawHud();
            if (_tpArmed && !_open) DrawToast("传送模式:点击目标位置");
            if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime < _toastUntil && !_open) DrawToast(_toast);

            if (_fade <= 0.001f) { _scrollY = 0f; return; }
            float a = 1f - Mathf.Pow(1f - _fade, 5f);

            RefreshView();          // 内容视口:工具条/搜索框之下 → 底部大标题之上
            DrawPanelBg(a);         // 深色渐变背景 / 最初那套纯色半透明遮罩
            DrawWatermark();        // 水印必须画在深色背景**之上**,否则被背景吞掉

            DrawBoneHud();      // 骨点调试叠加(开面板时也画,方便边看边调)

            if (_confirmOpen) { DrawConfirm(a); GUI.color = Color.white; return; }

            DrawCapsuleBar(a);
            DrawSearch(a);

            // ---- 内容区:两套皮肤都支持纵向滚动(行多了不会永久看不见)----
            // 深色皮肤额外有"自动多列布局";最初那套保留保存/默认的 3 列摆位,只加滚轮位移 + 裁剪。
            _clipOn = true;
            if (Searching)
            {
                LayoutSearchResults();         // 搜索结果仍然是"单列堆叠"(布局函数本身没动)
                if (_contentH > _view.height) ScrollInput(); else _scrollY = 0f;
            }
            else if (!_uiDark)
            {
                _contentH = ManualContentH();
                _scrollY = Mathf.Clamp(_scrollY, 0f, Mathf.Max(0f, _contentH - _view.height));
                ScrollInput();
            }
            else if (_flowOn) FlowLayout();     // 自动多列布局(拖动过卡片后改用手动坐标)
            else
            {
                _contentH = ManualContentH();
                _scrollY = Mathf.Clamp(_scrollY, 0f, Mathf.Max(0f, _contentH - _view.height));
                ScrollInput();
            }

            for (int c = 0; c < CardCount; c++) DrawCard(c, a);
            _clipOn = false;

            DrawScrollBar(a);      // 内部已判断只有深色皮肤才画
            DrawFooterTitle(a);    // 内部已判断只有深色皮肤才画
            GUI.color = Color.white;
        }

        // While searching, every matching card is force-expanded. Placing them in a single
        // stacked column (instead of their floating positions) keeps the results from piling
        // on top of each other.
        private void LayoutSearchResults()
        {
            float py = 92f * S;
            for (int c = 0; c < CardCount; c++)
            {
                if (!CardShown(c)) continue;              // 开发者模式藏起来的卡不参与布局
                float bodyH = 0f;
                string lastGroup = null;
                for (int i = 0; i < Mods[c].Count; i++)
                {
                    Mod m = Mods[c][i];
                    if (!RowShown(m)) continue;           // 藏起来的行也不占位置
                    if (!Match(m.Name)) continue;
                    if (m.Group.Length > 0 && m.Group != lastGroup) { bodyH += GrpH * S; lastGroup = m.Group; }
                    bodyH += RowHFor(m);
                }
                _cardX[c] = 20f * S;
                _cardY[c] = py;
                py += HeadH * S + bodyH + 6f * S;
            }
            _contentH = py - 92f * S + 8f * S;
        }

        // ================= card =================
        public float RowHFor(Mod m)
        {
            if (m.Kind == 3) return SelH * S;
            if (m.Kind == 4) return InpH * S;
            // kind 5 = 只读状态行:两行高 + 自动换行(状态串/路径都挺长,单行会被裁掉)
            if (m.Kind == 5) return RowH * S * 2f;
            return RowH * S;
        }

        private bool Searching { get { return !string.IsNullOrEmpty(_search) && _search.Trim().Length > 0; } }

        private void DrawCard(int c, float a)
        {
            if (!CardShown(c)) return;                 // 开发者模式关闭时:这两张卡完全不出现
            bool searching = Searching;
            float w = CardW * S, headerH = HeadH * S;
            float bodyH = 0f;
            string lastGroup = null;
            for (int i = 0; i < Mods[c].Count; i++)
            {
                Mod m = Mods[c][i];
                if (!RowShown(m)) continue;
                if (searching && !Match(m.Name)) continue;
                if (!searching && m.Group.Length > 0 && m.Group != lastGroup) { bodyH += GrpH * S; lastGroup = m.Group; }
                bodyH += RowHFor(m);
            }
            float anim = searching ? 1f : _cardAnim[c];
            float totalH = headerH + bodyH * anim;
            float x = _cardX[c], y = _cardY[c] - _scrollY;     // y 是屏幕坐标(内容坐标 - 滚动量)

            Rect card = new Rect(x, y, w, totalH);

            // 卡片:深色皮肤 = 竖向渐变 + 淡描边;最初那套 = 平色半透明玻璃
            Rect shRect = new Rect(x + 4f * S, y + (_uiDark ? 6f * S : 5f * S), w, totalH);
            if (_uiDark) Fill(shRect, Mul(_cShadow, a));
            else RRect(shRect, RadiusCard * S, Mul(_cShadow, a));      // 最初那套的投影是圆角的
            if (_uiDark)
            {
                RGrad(card, RadiusCard * S, Mul(_cCard1, a), Mul(_cCard2, a), 1f, false);
                RRect(card, RadiusCard * S, Mul(_cCardEdge, a));
            }
            else RRect(card, RadiusCard * S, Mul(_cCard1, a));

            // 标题条:深色 = 这张卡的主题色 A→B 横向渐变 + 白字;最初 = 平色标题条 + 深色字
            Rect headRect = new Rect(x, y, w, headerH);
            if (_uiDark)
            {
                RGradTop(headRect, RadiusCard * S, CardA(c), CardB(c), a, true);
                RText(new Rect(x + 14f * S, y, w - 74f * S, headerH), CatName[c], _stTitleB, Color.white, true, a);
                Fill(new Rect(x + RadiusCard * S, y + headerH - 1f, w - RadiusCard * 2f * S, 1f),
                    Mul(Lighten(CardA(c), 0.40f), a * 0.60f));
            }
            else
            {
                RRectTop(headRect, RadiusCard * S, Mul(_cHead, a));
                RText(new Rect(x + 14f * S, y, w - 74f * S, headerH), CatName[c], _stTitle, _cText, true, a);
                Fill(new Rect(x + RadiusCard * S, y + headerH - 1f, w - RadiusCard * 2f * S, 1f),
                    Mul(ColAccent, a * 0.55f));      // 最初那套的 1px 强调线
            }

            // per-card reset-all
            Rect rb = new Rect(x + w - 46f * S, y + headerH * 0.5f - 9f * S, 18f * S, 18f * S);
            DrawResetIcon(rb, a, ResetIconCol(c));
            if (ViewClip(rb).height > 0.5f && GUI.Button(rb, GUIContent.none, GUIStyle.none)) ResetCard(c);

            DrawChevron(new Rect(x + w - 24f * S, y + headerH * 0.5f - 6f * S, 12f * S, 12f * S),
                searching || _cardOpen[c], a, ChevronCol(c, searching || _cardOpen[c]));

            // 拖拽 / 折叠:按下时命中矩形裁到视口 —— 滚动时不会隔着工具条误触。
            // 拖动/松开**不**受视口限制,否则把卡片拖出视口上边就会卡住不动。
            Event e = Event.current;
            Rect head = ViewClip(new Rect(x, y, w, headerH));
            if (e.type == EventType.MouseDown && head.height > 0.5f && head.Contains(e.mousePosition) && !rb.Contains(e.mousePosition))
            {
                if (!searching) _flowOn = false;      // 用户自己摆了位置 → 改用手动坐标
                _dragCard = c;
                _dragOffset = new Vector2(e.mousePosition.x - x, e.mousePosition.y - y);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _dragCard == c)
            {
                float nx = e.mousePosition.x - _dragOffset.x, ny = e.mousePosition.y - _dragOffset.y;
                if (!e.shift)
                {
                    nx = Mathf.Round(nx / (Snapping * S)) * Snapping * S;
                    ny = Mathf.Round(ny / (Snapping * S)) * Snapping * S;
                }
                _cardX[c] = nx; _cardY[c] = ny + _scrollY; e.Use();
            }
            else if (e.type == EventType.MouseUp && _dragCard == c)
            {
                if (Mathf.Abs(e.mousePosition.x - (x + _dragOffset.x)) < 4f &&
                    Mathf.Abs(e.mousePosition.y - (y + _dragOffset.y)) < 4f)
                    _cardOpen[c] = !_cardOpen[c];
                _dragCard = -1; e.Use();
                if (!searching) KeepCardReachable(c, totalH, w);   // 两套皮肤都做"整张卡看不见就拉回"
            }

            if (anim <= 0.001f) return;
            if (ViewClip(card).height <= 0.5f) return;    // 整张卡都在视口外:不画也不处理行内控件

            float ry = y + headerH;
            lastGroup = null;
            for (int i = 0; i < Mods[c].Count; i++)
            {
                Mod m = Mods[c][i];
                if (!RowShown(m)) continue;               // 被开发者模式藏起来的行:不画、不占位、不响应
                if (searching && !Match(m.Name)) continue;
                if (!searching && m.Group.Length > 0 && m.Group != lastGroup)
                {
                    RText(new Rect(x + 14f * S, ry, w - 28f * S, GrpH * S), m.Group, _stGroup, _cGroup, false, a);
                    ry += GrpH * S * anim;
                    lastGroup = m.Group;
                }
                DrawRow(m, c, x, ry, w, a, searching && m.Name == _search.Trim());
                ry += RowHFor(m) * anim;
            }
        }

        private void ResetCard(int c)
        {
            for (int i = 0; i < Mods[c].Count; i++)
            {
                Mod m = Mods[c][i];
                if (m.Kind == 0) { if (m.Get()) m.Set(false); }
                else if (m.Kind == 1) m.SetF(m.Def);
            }
            Save();
            Toast(CatName[c] + " 已恢复默认");
        }

        // ================= row =================
        // 行外观(两套皮肤各自的样子),输入部分与原版一一对应:
        //   0=开关(整行点击 + 胶囊开关) 1=滑块(隐形真控件 + 自绘轨道)
        //   2=动作(深色=居中圆角按钮 / 最初=左对齐整行) 3=选择器 ◀▶/执行 4=数字输入框 5=只读长文本
        private void DrawRow(Mod m, int c, float x, float y, float w, float a, bool highlight)
        {
            if (!RowShown(m)) return;                    // 开发者模式藏起来的行:完全不出现
            float h = RowHFor(m);
            Rect r = new Rect(x, y, w, h);
            Rect hit = ViewClip(r);                      // 命中区裁到滚动视口
            bool visible = hit.height > 0.5f;
            bool hover = visible && hit.Contains(Event.current.mousePosition) && _dragCard < 0;
            Color tcA = AccentSoft(c), tcB = AccentEnd(c);

            if (hover) Fill(r, Mul(_cHover, a));
            // 行分隔细线(只有深色皮肤画)
            if (_uiDark && y + h - 1f > _view.y)
                Fill(new Rect(x + 12f * S, y + h - 1f, w - 24f * S, 1f), Mul(_cRowLine, a));

            bool enabled = (m.Kind == 0) && m.Get();
            Color textCol = enabled ? AccentOf(c) : _cText;

            if (m.Kind == 1)
            {
                RText(new Rect(r.x + 14f * S, r.y, 130f * S, h), m.Name, _stSmall, _cText, true, a);
                float trackX = r.x + 128f * S, trackW = w - 226f * S;
                float v = m.GetF();
                float p = (m.Max - m.Min) == 0f ? 0f : Mathf.Clamp01((v - m.Min) / (m.Max - m.Min));
                float cy = r.y + h * 0.5f, th = _trackH;

                RRect(new Rect(trackX, cy - th * 0.5f, trackW, th), th * 0.5f, Mul(_cTrack, a));
                if (p > 0.002f)
                {
                    if (_uiDark)
                        RGrad(new Rect(trackX, cy - th * 0.5f, Mathf.Max(th, trackW * p), th), th * 0.5f,
                            Mul(tcA, a), Mul(tcB, a), 1f, true);
                    else Fill(new Rect(trackX, cy - th * 0.5f, trackW * p, th), Mul(ColAccent, a));
                }
                if (_uiDark)
                {
                    RCircle(new Rect(trackX + trackW * p - 12f * S, cy - 12f * S, 24f * S, 24f * S), Mul(tcA, a * 0.28f));
                    RCircle(new Rect(trackX + trackW * p - 9f * S, cy - 9f * S, 18f * S, 18f * S), Mul(_cThumb, a));
                }
                else
                    RCircle(new Rect(trackX + trackW * p - 9f * S, cy - 9f * S, 18f * S, 18f * S), Mul(ColAccent, a));

                Color keep = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0f);      // real control, invisible
                Rect sh = ViewClip(new Rect(trackX, cy - 10f * S, trackW, 20f * S));
                if (sh.height > 0.5f)
                {
                    float nv = GUI.HorizontalSlider(sh, v, m.Min, m.Max);
                    if (Mathf.Abs(nv - v) > 0.0001f) m.SetF(nv);
                }
                GUI.color = keep;

                RText(new Rect(r.x + w - 96f * S, r.y, 52f * S, h), v.ToString(m.Fmt), _stSmall,
                    _uiDark ? Lighten(tcA, 0.35f) : ColAccent, true, a);

                Rect reset = new Rect(r.x + w - 38f * S, r.y + h * 0.5f - 9f * S, 18f * S, 18f * S);
                DrawResetIcon(reset, a, ResetIconCol(c));
                if (ViewClip(reset).height > 0.5f && GUI.Button(reset, GUIContent.none, GUIStyle.none)) { m.SetF(m.Def); Save(); }
            }
            else if (m.Kind == 3)
            {
                string[] items = ItemsFor(m);
                int idx = items.Length > 0 ? Mathf.Clamp(m.GetIdx(), 0, items.Length - 1) : 0;
                string cur = items.Length > 0 ? items[idx] : "(列表为空)";
                string action = (m.Name == "跳关") ? "跳转"
                    : ((m.Name == "选择敌人类型") ? "生成"
                    : ((m.Name == "配色预设" || m.Name == "选择骨架来源" || m.Name == "原角色显示" || m.Name == "手持物品") ? "应用" : "执行"));

                RText(new Rect(r.x + 14f * S, r.y + 2f * S, w - 96f * S, 22f * S), m.Name, _stSmall, _cText, true, a);
                RText(new Rect(r.x + w - 76f * S, r.y + 2f * S, 64f * S, 22f * S),
                    (idx + 1) + "/" + items.Length, _stSmall, _cTextDim, true, a);

                Rect prev = new Rect(r.x + 14f * S, r.y + 26f * S, 24f * S, 24f * S);
                Rect next = new Rect(r.x + 42f * S, r.y + 26f * S, 24f * S, 24f * S);
                Rect go = new Rect(r.x + w - 62f * S, r.y + 26f * S, 50f * S, 24f * S);

                if (_uiDark)
                {
                    RRect(prev, _btnRad, Mul(_cBtn, a));
                    RRect(next, _btnRad, Mul(_cBtn, a));
                    RGrad(go, _btnRad, Mul(tcA, a), Mul(tcB, a), 1f, true);
                }
                else
                {
                    RRect(prev, _btnRad, Mul(ColSlideTrack, a));
                    RRect(next, _btnRad, Mul(ColSlideTrack, a));
                    RRect(go, _btnRad, Mul(ColAccent, a));
                }
                RText(prev, "◀", _stCenter, _cText, true, a);
                RText(next, "▶", _stCenter, _cText, true, a);
                RText(go, action, _stCenter, Color.white, _uiDark, a);
                RText(new Rect(prev.xMax + 6f * S, r.y + 26f * S, go.x - prev.xMax - 12f * S, 24f * S),
                    cur, _stCenter, _uiDark ? Lighten(tcA, 0.35f) : ColAccent, true, a);

                if (items.Length > 0 && visible && GUI.Button(prev, GUIContent.none, GUIStyle.none)) m.SetIdx((idx - 1 + items.Length) % items.Length);
                if (items.Length > 0 && visible && GUI.Button(next, GUIContent.none, GUIStyle.none)) m.SetIdx((idx + 1) % items.Length);
                if (items.Length > 0 && visible && GUI.Button(go, GUIContent.none, GUIStyle.none)) m.Run();
            }
            else if (m.Kind == 4)
            {
                RText(new Rect(r.x + 14f * S, r.y + 2f * S, w - 28f * S, 22f * S), m.Name, _stSmall, _cText, true, a);
                Rect fld = new Rect(r.x + 14f * S, r.y + 26f * S, 150f * S, 24f * S);
                RRect(fld, _btnRad, Mul(_cField, a));
                Rect fldHit = ViewClip(new Rect(fld.x + 8f * S, fld.y + 2f * S, fld.width - 16f * S, fld.height - 4f * S));
                if (fldHit.height > 0.5f)
                {
                    string s = GUI.TextField(fldHit, m.GetI().ToString(), _stSmall);
                    int parsed;
                    if (int.TryParse(s, out parsed) && parsed != m.GetI()) m.SetI(parsed);
                }
                Rect go = new Rect(r.x + w - 62f * S, r.y + 26f * S, 50f * S, 24f * S);
                if (_uiDark) RGrad(go, _btnRad, Mul(tcA, a), Mul(tcB, a), 1f, true);
                else RRect(go, _btnRad, Mul(ColAccent, a));
                RText(go, "执行", _stCenter, Color.white, _uiDark, a);
                if (visible && GUI.Button(go, GUIContent.none, GUIStyle.none)) m.Run();
            }
            else if (m.Kind == 5)
            {
                string s = "";
                try { if (m.GetS != null) s = m.GetS(); } catch { }
                if (s == null) s = "";
                if (_stWrap == null) _stWrap = NewStyle(12f * S, TextAnchor.UpperLeft, _cTextDim);
                _stWrap.wordWrap = true;
                RText(new Rect(r.x + 14f * S, r.y, w - 28f * S, h), s, _stWrap, _cTextDim, true, a);
            }
            else if (m.Kind == 2 && _skBtnCenter)
            {
                // 深色皮肤:动作行 = 圆角按钮(参照图:动作是圆角按钮)
                Rect b = new Rect(r.x + 10f * S, r.y + 4f * S, w - 20f * S, h - 8f * S);
                bool bh = visible && b.Contains(Event.current.mousePosition) && _dragCard < 0;
                RGrad(b, 7f * S, Mul(bh ? DkBtnHi : DkBtn, a), Mul(DkBtn2, a), 1f, false);
                Fill(new Rect(b.x + 8f * S, b.y, b.width - 16f * S, 1f), Mul(Lighten(tcA, 0.25f), a * (bh ? 0.55f : 0.30f)));
                RText(b, m.Name, _stCenter, bh ? Color.white : DkText, false, a);
            }
            else
            {
                // 最初那套:动作行(以及所有开关行)= 左对齐的一行文字,整行可点
                RText(new Rect(r.x + 14f * S, r.y, w - 70f * S, h), m.Name, _stMod, textCol, true, a);
            }

            if (highlight)
            {
                Color hc = _uiDark ? tcA : ColAccent;
                Fill(new Rect(r.x + 3f * S, r.y, w - 6f * S, 2f * S), Mul(hc, a));
                Fill(new Rect(r.x + 3f * S, r.y + h - 2f * S, w - 6f * S, 2f * S), Mul(hc, a));
                Fill(new Rect(r.x + 3f * S, r.y, 2f * S, h), Mul(hc, a));
                Fill(new Rect(r.x + w - 5f * S, r.y, 2f * S, h), Mul(hc, a));
            }

            if (m.Kind == 0) DrawSwitch(new Rect(r.x + w - 48f * S, r.y + h * 0.5f - 9.5f * S, 34f * S, 19f * S), m.Anim, a, tcA, tcB);

            // 整行点击(命中的是裁过的矩形 → 滚动时点到工具条上不会误触)
            if (m.Kind == 0 || m.Kind == 2)
                if (visible && GUI.Button(hit, GUIContent.none, GUIStyle.none))
                { if (m.Kind == 0) m.Set(!m.Get()); else m.Run(); }
        }

        private string[] ItemsFor(Mod m)
        {
            if (m.Name == "跳关") return JumpScenes;
            if (m.Name == "选择强化项") return EnhNames;
            if (m.Name == "配色预设") { return SkinPresets; }
            if (m.Name == "选择骨架来源") { return _borrowNames; }
            if (m.Name == "原角色显示") { return OrigOverlayNames; }
            if (m.Name == "手持物品") { return SwordNames; }
            if (m.Name == "选择敌人类型") { EnsureEnemyNames(); return EnemyNames; }
            if (m.Name == "滤镜切换")
            {
                EnsureFilterTypes();
                if (FilterTypes == null || FilterTypes.Length == 0) return new string[0];
                string[] names = new string[FilterTypes.Length + 1];
                names[0] = "无滤镜";
                for (int i = 0; i < FilterTypes.Length; i++) names[i + 1] = FilterTypes[i] != null ? FilterTypes[i].Name : "?";
                return names;
            }
            return new string[0];
        }

        // 圆角胶囊开关:深色 = 关态深灰轨道 + 灰钮,开态主题色渐变 + 白钮;
        //              最初那套 = 原来的 Lerp 轨道 + 圆钮
        private void DrawSwitch(Rect track, float t, float a, Color tcA, Color tcB)
        {
            float rad = track.height * 0.5f;
            if (_uiDark)
            {
                RRect(track, rad, Mul(_cSwOff, a));
                if (t > 0.002f) RGrad(track, rad, Mul(tcA, a * t), Mul(tcB, a * t), 1f, true);
            }
            else RRect(track, rad, Mul(Color.Lerp(_cSwOff, _cSwOn, t), a));
            float d = track.width - track.height;
            RCircle(new Rect(track.x + d * t, track.y, track.height, track.height),
                Mul(Color.Lerp(_cSwThumbOff, _cSwThumbOn, t), a));
        }

        // Reset-to-default button. Drawn as a real circular-arrow glyph when the CJK font has one,
        // otherwise falls back to a glyph the default IMGUI font definitely has.
        private void DrawResetIcon(Rect r, float a, Color accent)
        {
            if (_hasResetGlyph)
            {
                RText(r, "↺", _stIcon, Mul(accent, a), false, 1f);
                return;
            }
            RText(r, "R", _stIcon, Mul(accent, a), false, 1f);
        }

        private void DrawChevron(Rect r, bool expanded, float a, Color accent)
        {
            Color c = Mul(expanded ? accent : _cTextDim, a);
            float t = 1.5f * S;
            Fill(new Rect(r.x, r.y + r.height * 0.5f - t * 0.5f, r.width, t), c);
            if (!expanded) Fill(new Rect(r.x + r.width * 0.5f - t * 0.5f, r.y, t, r.height), c);
        }

        // ================= chrome =================
        private void DrawCapsuleBar(float a)
        {
            float bw = 720f * S, bh = 30f * S;
            Rect bar = new Rect((Screen.width - bw) * 0.5f, 10f * S, bw, bh);
            RRect(bar, bh * 0.5f, Mul(_cBarBg, a));
            if (_uiDark) Fill(new Rect(bar.x + bh * 0.5f, bar.y, bar.width - bh, 1f), Mul(_cBarHi, a));

            // 只给"显示出来的卡片"排按钮(开发者模式关掉时方块人/换角色两个按钮一起消失)
            int n = 0;
            for (int i = 0; i < CardCount; i++) if (CardShown(i)) n++;
            if (n < 1) n = 1;
            float tw = bw / n;
            int k = 0;
            for (int i = 0; i < CardCount; i++)
            {
                if (!CardShown(i)) continue;
                Rect t = new Rect(bar.x + k * tw + 3f, bar.y + 3f, tw - 6f, bh - 6f);
                if (_cardOpen[i])
                {
                    if (_uiDark) RGrad(t, t.height * 0.5f, CardA(i), CardB(i), a, true);
                    else RRect(t, t.height * 0.5f, Mul(_cTab, a));
                }
                else if (t.Contains(Event.current.mousePosition)) RRect(t, t.height * 0.5f, Mul(_cBarHi, a));
                RText(t, CatName[i], _stTab, _cardOpen[i] ? (_uiDark ? Color.white : ColAccent) : _cTextDim, true, a);
                if (GUI.Button(t, GUIContent.none, GUIStyle.none)) _cardOpen[i] = !_cardOpen[i];
                k++;
            }

            DrawDevButton(bar, a);      // 顶部"开发"按钮(正上方开一个)
        }

        private void DrawSearch(float a)
        {
            float w = 560f * S, h = 32f * S;
            Rect r = new Rect((Screen.width - w) * 0.5f, 46f * S, w, h);
            RRect(r, h * 0.5f, Mul(_cField, a));
            if (_uiDark) Fill(new Rect(r.x + h * 0.5f, r.y, r.width - h, 1f), Mul(_cBarHi, a));
            string s = GUI.TextField(new Rect(r.x + 18f * S, r.y + 3f * S, w - 36f * S, h - 6f * S), _search, _stSearch);
            if (s != _search) _search = s == null ? "" : s;
            if (string.IsNullOrEmpty(_search))
                RText(new Rect(r.x + 18f * S, r.y, w, h), "搜索功能名…", _stSearch, _cPlaceholder, false, a);

            Rect col = new Rect(r.xMax + 10f * S, r.y + 3f * S, 30f * S, h - 6f * S);
            RRect(col, _uiDark ? 7f * S : 4f * S, Mul(_cBarHi, a));
            RText(col, "⇕", _stCenter, _cText, true, a);
            if (GUI.Button(col, GUIContent.none, GUIStyle.none))
            {
                bool anyClosed = false;
                for (int i = 0; i < CardCount; i++) if (!_cardOpen[i]) anyClosed = true;
                for (int i = 0; i < CardCount; i++) _cardOpen[i] = anyClosed;
            }
        }

        private void DrawConfirm(float a)
        {
            float w = 320f * S, h = 104f * S;
            float x = (Screen.width - w) * 0.5f, y = (Screen.height - h) * 0.5f;

            Rect ok = new Rect(x + 22f * S, y + h - 46f * S, (w - 56f * S) * 0.5f, 30f * S);
            Rect no = new Rect(ok.xMax + 12f * S, ok.y, ok.width, ok.height);

            if (_uiDark)
            {
                Color sh = Mul(DkCardShadow, a);
                Fill(new Rect(x + 4f * S, y + 6f * S, w, h), sh);
                RGrad(new Rect(x, y, w, h), RadiusCard * S, Mul(DkModal, a), Mul(DkModal2, a), 1f, false);
                RRect(new Rect(x, y, w, h), RadiusCard * S, Mul(DkModalEdge, a));
                Fill(new Rect(x + RadiusCard * S, y + 1f, w - RadiusCard * 2f * S, 1.5f), Mul(RGBA(196, 150, 255, 0.60f), a));
                RText(new Rect(x + 10f * S, y + 10f * S, w - 20f * S, 40f * S), _confirmMsg, _stCenter, DkText, false, 1f);

                // 确认 = 强调色渐变按钮,取消 = 深色按钮
                RGrad(new Rect(ok.x - 1.5f, ok.y - 1.5f, ok.width + 3f, ok.height + 3f), 8f * S,
                    Mul(DkOkEdge, a), Mul(RGBA(255, 110, 175, 0.55f), a), 1f, true);
                RGrad(ok, 7f * S, Mul(DkOk1, a), Mul(DkOk2, a), 1f, true);
                RRect(new Rect(no.x - 1.5f, no.y - 1.5f, no.width + 3f, no.height + 3f), 8f * S, Mul(DkNoEdge, a));
                RRect(no, 7f * S, Mul(DkBtn, a));
                RText(ok, "确认", _stCenter, Color.white, true, 1f);
                RText(no, "取消", _stCenter, DkText, false, 1f);
            }
            else
            {
                // 最初那套:浅色玻璃弹窗(颜色与原版一致)
                Color sh = ColShadow; sh.a = Mathf.Min(0.35f, ColShadow.a * 2.2f);
                Fill(new Rect(x + 4f * S, y + 5f * S, w, h), sh);
                RRect(new Rect(x, y, w, h), RadiusCard * S, ColModal);
                Fill(new Rect(x + RadiusCard * S, y, w - RadiusCard * 2f * S, 1f), Mul(ColAccent, 0.7f));
                RText(new Rect(x + 10f * S, y + 10f * S, w - 20f * S, 40f * S), _confirmMsg, _stCenter, ColText, false, 1f);

                RRect(new Rect(ok.x - 1f, ok.y - 1f, ok.width + 2f, ok.height + 2f), 6f * S, RGBA(214, 51, 132, 0.42f));
                RRect(ok, 5f * S, RGBA(255, 244, 248, 0.72f));
                RRect(new Rect(no.x - 1f, no.y - 1f, no.width + 2f, no.height + 2f), 6f * S, RGBA(150, 118, 132, 0.26f));
                RRect(no, 5f * S, RGBA(255, 252, 253, 0.60f));
                RText(ok, "确认", _stCenter, ColAccent, false, 1f);
                RText(no, "取消", _stCenter, ColTextDim, false, 1f);
            }

            if (GUI.Button(ok, GUIContent.none, GUIStyle.none))
            { _confirmOpen = false; Action act = _confirmAction; _confirmAction = null; if (act != null) act(); }
            if (GUI.Button(no, GUIContent.none, GUIStyle.none)) { _confirmOpen = false; _confirmAction = null; }
        }

        private void DrawToast(string msg)
        {
            float h = 28f * S;
            Rect r = new Rect((Screen.width - 460f) * 0.5f, Screen.height - h - 46f, 460f, h);
            if (_uiDark)
            {
                RGrad(r, 7f * S, DkModal, DkModal2, 1f, false);
                RRect(r, 7f * S, DkModalEdge);
                RText(new Rect(r.x, r.y, r.width, r.height), msg, _stCenter, DkText, true, 1f);
            }
            else
            {
                RRect(r, 6f, ColHover);
                RText(new Rect(r.x, r.y, r.width, r.height), msg, _stCenter, ColText, true, 1f);
            }
        }

        private void DrawHud()
        {
            string s = "";
            try
            {
                string mode = "?";
                try { mode = R.Mode.CurrentMode.ToString(); } catch { }
                float fps = 0f;
                try { fps = WorldTime.Fps; } catch { }
                Vector3 p = Vector3.zero;
                try { p = R.Player.transform.position; } catch { }
                s = "模式 " + mode + "   FPS " + fps.ToString("F0") + "\n坐标 "
                    + p.x.ToString("F1") + ", " + p.y.ToString("F1");
                if (_dpsOn) s += "\nDPS " + _dps.ToString("F0");
                if (_comboHud)
                {
                    try { s += "   连击 " + R.SceneData.assessmentData.CurrentComboNum; } catch { }
                }
                try
                {
                    if (R.Player != null && R.Player.Attribute != null)
                        s += "\nHP " + R.Player.Attribute.currentHP + "/" + R.Player.Attribute.maxHP;
                }
                catch { }
            }
            catch (Exception e) { s = "HUD: " + e.Message; }

            // Size the box from the real content: line count + widest measured line.
            // (A hardcoded height clipped the text once DPS / combo lines appeared.)
            if (_stSmall == null) return;
            string[] lines = s.Split('\n');
            float lineH = _stSmall.fontSize + 5f;
            float boxW = 90f;
            for (int i = 0; i < lines.Length; i++)
            {
                float tw = _stSmall.CalcSize(new GUIContent(lines[i])).x;
                if (tw > boxW) boxW = tw;
            }
            boxW += 28f;
            float boxH = lines.Length * lineH + 16f;

            Rect r = new Rect(12f, 12f, boxW, boxH);
            if (_uiDark)
            {
                RGrad(r, 8f, DkPanel, _cPanel2, 1f, false);
                RRect(r, 8f, DkModalEdge);
                RText(new Rect(r.x + 11f, r.y + 8f, r.width - 22f, r.height - 16f), s, _stSmall, DkText, true, 1f);
            }
            else
            {
                RRect(r, 8f, _cPanel1);
                RText(new Rect(r.x + 11f, r.y + 8f, r.width - 22f, r.height - 16f), s, _stSmall, ColText, true, 1f);
            }
        }

        private void DrawBounds()
        {
            try
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                // 玩家:PlayerAttribute.bounds 是**属性**,每次实时算 → 直接能用
                if (R.Player != null && R.Player.Attribute != null) DrawBox(cam, R.Player.Attribute.bounds, ColAccent);
                // 敌人:EnemyAttribute.bounds 是**字段快照**(生成时写一次,不跟随移动)→ 必须从活的东西上取
                if (R.Enemy != null)
                {
                    var list = R.Enemy.EnemyAttributes;
                    if (list != null)
                        for (int i = 0; i < list.Count; i++)
                            if (list[i] != null) DrawBox(cam, LiveEnemyBounds(list[i]), RGBA(214, 51, 132, 0.5f));
                }
            }
            catch (Exception e) { Warn("Bounds", e); }
        }

        // 敌人受击框:优先取**活的**包围盒(碰撞体 → 渲染器),都没有才退回快照 + 实时位置补偿。
        // 参数用 object 是故意的:方法签名里不许出现游戏类型(否则 BepInEx 会丢掉整个 dll)。
        private Bounds LiveEnemyBounds(object enemyAttr)
        {
            Bounds b = new Bounds();
            try
            {
                if (_enemyBoxOffset.Count > 300) _enemyBoxOffset.Clear();
                var en = enemyAttr as EnemyAttribute;
                if (en == null) return b;
                b = en.bounds;                                  // 快照:至少尺寸是对的
                var tc = en.timeController;                     // 挂在敌人身上的游戏组件 → 能拿到 transform
                if (tc != null)
                {
                    Collider2D col = tc.GetComponentInParent<Collider2D>();
                    if (col != null && col.enabled) return col.bounds;
                    Renderer rr = tc.GetComponentInParent<Renderer>();
                    if (rr != null) return rr.bounds;
                    // 都没有:记住"快照中心相对敌人原点的偏移",之后用实时位置推算
                    Vector3 off;
                    if (!_enemyBoxOffset.TryGetValue(enemyAttr, out off))
                    {
                        off = b.center - tc.transform.position;
                        _enemyBoxOffset[enemyAttr] = off;
                    }
                    b.center = tc.transform.position + off;
                }
            }
            catch { }
            return b;
        }

        private void DrawBox(Camera cam, Bounds b, Color c)
        {
            Vector3 bl = cam.WorldToScreenPoint(new Vector3(b.min.x, b.min.y, 0f));
            Vector3 tr = cam.WorldToScreenPoint(new Vector3(b.max.x, b.max.y, 0f));
            float x = bl.x, y = Screen.height - tr.y, w = tr.x - bl.x, h = tr.y - bl.y;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, 2f), _texWhite);
            GUI.DrawTexture(new Rect(x, y + h - 2f, w, 2f), _texWhite);
            GUI.DrawTexture(new Rect(x, y, 2f, h), _texWhite);
            GUI.DrawTexture(new Rect(x + w - 2f, y, 2f, h), _texWhite);
            GUI.color = Color.white;
        }

        // ================= primitives =================
        // 说明:下面这套原语**不带滚动裁剪**,现在只作为参考留在原地;
        // 面板实际用的是 CheatPanelPlugin.Theme2.cs 里带裁剪/渐变的 RRect / RGrad / RText / Fill。
        private static Color Mul(Color c, float a) { c.a *= a; return c; }

        private void DrawRound(Rect r, float rad, Color c)
        {
            rad = Mathf.Min(rad, Mathf.Min(r.width, r.height) * 0.5f);
            if (rad <= 0.5f) { GUI.color = c; GUI.DrawTexture(r, _texWhite); return; }
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x + rad, r.y, r.width - 2f * rad, r.height), _texWhite);
            GUI.DrawTexture(new Rect(r.x, r.y + rad, rad, r.height - 2f * rad), _texWhite);
            GUI.DrawTexture(new Rect(r.x + r.width - rad, r.y + rad, rad, r.height - 2f * rad), _texWhite);
            DrawQuad(new Rect(r.x, r.y, rad, rad), 0);
            DrawQuad(new Rect(r.x + r.width - rad, r.y, rad, rad), 1);
            DrawQuad(new Rect(r.x, r.y + r.height - rad, rad, rad), 2);
            DrawQuad(new Rect(r.x + r.width - rad, r.y + r.height - rad, rad, rad), 3);
        }

        private void DrawRoundTop(Rect r, float rad, Color c)
        {
            rad = Mathf.Min(rad, Mathf.Min(r.width, r.height) * 0.5f);
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y + rad, r.width, r.height - rad), _texWhite);
            GUI.DrawTexture(new Rect(r.x + rad, r.y, r.width - 2f * rad, rad), _texWhite);
            DrawQuad(new Rect(r.x, r.y, rad, rad), 0);
            DrawQuad(new Rect(r.x + r.width - rad, r.y, rad, rad), 1);
        }

        private void DrawQuad(Rect r, int q)
        {
            float u = (q == 1 || q == 3) ? 0.5f : 0f;
            float v = (q >= 2) ? 0f : 0.5f;
            GUI.DrawTextureWithTexCoords(r, _texCircle, new Rect(u, v, 0.5f, 0.5f));
        }

        private void DrawCircle(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, _texCircle); }

        private void Label(Rect r, string s, GUIStyle st, Color col, bool outline, float a)
        {
            Color c = col; c.a *= a;
            if (outline)
            {
                Color o = DkOutline; o.a *= a;
                st.normal.textColor = o;
                GUI.Label(new Rect(r.x - 1f, r.y, r.width, r.height), s, st);
                GUI.Label(new Rect(r.x + 1f, r.y, r.width, r.height), s, st);
                GUI.Label(new Rect(r.x, r.y - 1f, r.width, r.height), s, st);
                GUI.Label(new Rect(r.x, r.y + 1f, r.width, r.height), s, st);
            }
            st.normal.textColor = c;
            GUI.Label(r, s, st);
        }

        private bool Match(string name)
        {
            if (!Searching) return true;
            return name.IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ================= styles =================
        private void BuildStyles()
        {
            EnsureFont();
            _stTitle = NewStyle(14f * S, TextAnchor.MiddleLeft, ColText);     // 最初那套的卡片标题(不粗体)
            _stTitleB = NewStyle(14f * S, TextAnchor.MiddleLeft, Color.white); // 深色皮肤:白粗体标题
            _stTitleB.fontStyle = FontStyle.Bold;
            _stMod = NewStyle(12.5f * S, TextAnchor.MiddleLeft, ColTextDim);   // left aligned = more elegant
            _stSmall = NewStyle(12f * S, TextAnchor.MiddleLeft, ColText);
            _stCenter = NewStyle(12f * S, TextAnchor.MiddleCenter, ColText);
            _stTab = NewStyle(12.5f * S, TextAnchor.MiddleCenter, ColTextDim);
            _stSearch = NewStyle(14f * S, TextAnchor.MiddleLeft, ColText);
            _stGroup = NewStyle(10.5f * S, TextAnchor.MiddleLeft, ColGroup);
            _stBig = NewStyle(24f * S, TextAnchor.MiddleCenter, ColText);
            _stBig.fontStyle = FontStyle.Bold;

            // Reset icon: use the circular-arrow glyph only if the font actually has it,
            // otherwise fall back to a plain letter the font certainly contains.
            _hasResetGlyph = _font != null && _font.HasCharacter('↺');
            _stIcon = NewStyle(12.5f * S, TextAnchor.MiddleCenter, ColTextDim);

            _texWhite = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            _texWhite.SetPixel(0, 0, Color.white);
            _texWhite.Apply();
            _texCircle = MakeDisc(64);

            ApplyStyleColors();   // 文字色按当前主题刷一遍(TextField 读的是样式里的颜色)
            EnsureEnemyNames();
            EnsureFilterTypes();
            _stylesReady = true;
            Logger.LogInfo("Styles built (v0.5 dual theme, dark=" + (_uiDark ? 1 : 0) + "). Font="
                + (_font != null ? _font.name : "default"));
        }

        private GUIStyle NewStyle(float size, TextAnchor anchor, Color col)
        {
            GUIStyle st = new GUIStyle(GUI.skin.label);
            st.fontSize = Mathf.RoundToInt(size);
            st.alignment = anchor;
            st.normal.textColor = col;
            // 同一份样式也用在 TextField 上 → 四种状态都设一遍,
            // 否则聚焦时用的是内置皮肤的黑字,在新的深色输入框里会看不见。
            st.hover.textColor = col;
            st.active.textColor = col;
            st.focused.textColor = col;
            st.richText = false;
            st.wordWrap = false;
            if (_font != null) st.font = _font;
            return st;
        }

        private void EnsureFont()
        {
            if (_fontTried) return;
            _fontTried = true;
            try
            {
                _font = Font.CreateDynamicFontFromOSFont(
                    new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun" }, 16);
            }
            catch (Exception e) { Logger.LogWarning("CJK font failed: " + e.Message); }
            if (_font != null && !_font.HasCharacter('中')) { Logger.LogWarning("Font has no CJK."); _font = null; }
        }

        private static Texture2D MakeDisc(int size)
        {
            Texture2D t = new Texture2D(size, size, TextureFormat.ARGB32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            float r = size * 0.5f;
            Color[] px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - r + 0.5f, dy = y - r + 0.5f;
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f));
                }
            t.SetPixels(px); t.Apply();
            return t;
        }
    }
}
