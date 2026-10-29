// ICEY 作弊面板 · GUI 安装器 —— 第一版(看观感)
//
// 视觉**逐项对应** mustayot.love 的源码:
//   背景   : linear-gradient(178deg,#e9f5ff,#f1ecff 46%,#fff0f8)
//   海浪   : 两层 path 横向平移,后排 18s 正向 / 前排 26s 反向,前排高 62%
//   卡片   : 半透明白 + 1px rgba(44,39,64,.12) 描边 + 圆角
//   配色   : Theme.cs,全部抄自源码 :root
//   鲸鱼   : **不要**(主人明确说不需要) —— Art 里的 Whale* 常量已一并移除
//
// 本版**安装逻辑是模拟的**(进度条 + 日志走假数据),目的只有一个:让主人先看观感。
// 真接载荷见 _work\InstallerPayload.g.cs(那部分逻辑在 Installer.cs 里,直接搬)。
//
// ★ 坑:别用 PowerShell 的 Get-Content -Raw / Set-Content 往返改这个文件!
//   PS 5.1 会按 ANSI 读 UTF-8 再写成 UTF-8,中文字符串全部损坏 → csc 报 CS1010。
//   改源码一律用 edit 工具。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IceyGui
{
    static class Program
    {
        public static string[] Args;

        [STAThread]
        static int Main(string[] args)
        {
            Args = args;

            // 命令行模式(自动化/自测): install <目录> / uninstall <目录>
            // 无参数或参数不是这两个 → 照常弹 GUI。
            if (args != null && args.Length >= 2)
            {
                string m = args[0].ToLowerInvariant();
                if (m == "install" || m == "uninstall") return Cli.Run(m, args[1]);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new GuiForm());
            return 0;
        }
    }

    // ---------- 源码里的几何常量 ----------
    // ★ 海浪 path **做过一处修正**,原因记在这里,别当抄错:
    //   源码的 d 以 M0 70 起、以 (720,45) 终(前端是 100 → 75),**首尾 y 不相等 → 不是周期曲线**。
    //   而 CSS 是靠 .sea__track 宽 200%、两个副本、平移 -50% 来循环的,首尾不等就会在接缝处
    //   露出一道约 25 单位(≈33px)的竖边。而且源码里第二组波峰只有 135 宽、末尾 45 宽,本身也不齐。
    //   这里把四个波峰齐成等宽(每 180 一个)并让首尾回到同一 y,**波幅/相位/配色/滚动速度全部不变**。
    //   原始 d 留作对照,想还原逐字版本就用注释里那两条。
    //   原始(有接缝): M0 70C45 45 90 45 180 70c90 25 135 25 180 0s45-25 135 0 135 25 180 0 45-25 45-25v115H0z
    //                   M0 100C45 75 90 75 180 100s135 25 180 0 45-25 135 0 135 25 180 0 45-25 45-25v85H0z
    static class Art
    {
        // 海浪 viewBox 0 0 720 160(周期化后)
        public const string WaveBack  = "M0 70C45 45 90 45 180 70C270 95 315 95 360 70C450 45 495 45 540 70C630 95 675 95 720 70V160H0Z";
        public const string WaveFront = "M0 100C45 75 90 75 180 100C270 125 315 125 360 100C450 75 495 75 540 100C630 125 675 125 720 100V160H0Z";
    }

    sealed class GuiForm : Form
    {
        // ---------- 状态 ----------
        readonly Timer _anim = new Timer();
        readonly DateTime _t0 = DateTime.Now;

        float _dp = 1f;                 // DPI 比例
        string _gameDir = "";
        float _progress = 0f;
        volatile bool _busy;            // 安装/卸载进行中:锁住按钮
        readonly List<string> _log = new List<string>();
        readonly List<Btn> _btns = new List<Btn>();

        // 布局(逻辑坐标,设计基准 880x600)
        const float W = 880f, H = 600f;
        const float Pad = 28f;
        const float SeaH = 176f;        // 海浪区高度(没鲸鱼了,可以铺满底边)
        RectangleF _rClose, _rBrowse, _rLog;
        PointF _dragFrom; bool _dragging;

        int _hoverBtn = -1;
        bool _hoverClose, _hoverBrowse;

        // 载荷表由 pack-gui.ps1 生成(GeneratedPayload.g.cs,全局命名空间),资源名 f0..fN
        static readonly string[,] PayloadFiles = GeneratedPayload.Files;

        // ---------- 按钮定义 ----------
        sealed class Btn
        {
            public RectangleF Rect;
            public string Text;
            public string Sub;
            public int Kind;     // 0=主(实心紫) 1=次(描边) 2=危险(描边粉)
        }

        public GuiForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "ICEY 作弊面板 · 安装器";
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Theme.BgFlat;
            KeyPreview = true;

            _gameDir = (Program.Args != null && Program.Args.Length > 0 &&
                        System.IO.Directory.Exists(Program.Args[0]))
                       ? Program.Args[0] : AutoDetect();

            _anim.Interval = 16;
            _anim.Tick += delegate { Invalidate(); };
            _anim.Start();

            CheckStatus();
        }

        // 日志只放**关键信息**:这里是真实的安装状态探测,不是宣传语/自我介绍。
        void CheckStatus()
        {
            if (_gameDir.Length == 0)
            {
                Log("未检测到游戏目录,点『浏览…』手动选择。");
                return;
            }
            string dll = System.IO.Path.Combine(_gameDir, "BepInEx", "plugins", "IceyCheatPanel.dll");
            if (System.IO.File.Exists(dll))
            {
                long kb = new System.IO.FileInfo(dll).Length / 1024;
                Log("面板已安装 —— IceyCheatPanel.dll " + kb + " KB · " +
                    System.IO.File.GetLastWriteTime(dll).ToString("yyyy-MM-dd HH:mm"));
            }
            else
            {
                Log("面板未安装。");
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TryRoundCorners();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyDpi();
        }

        void ApplyDpi()
        {
            using (Graphics g = CreateGraphics()) _dp = g.DpiY / 96f;
            ClientSize = new Size((int)Math.Round(W * _dp), (int)Math.Round(H * _dp));
            DoLayout();
        }

        // ---------- 布局 ----------
        void DoLayout()
        {
            _btns.Clear();
            float x = Pad, w = W - Pad * 2;

            float bw = (w - 32f) / 3f, by = 240f, bh = 54f;
            _btns.Add(new Btn { Rect = new RectangleF(x, by, bw, bh), Text = "安装", Sub = "写入 BepInEx 与面板", Kind = 0 });
            _btns.Add(new Btn { Rect = new RectangleF(x + bw + 16f, by, bw, bh), Text = "修复重装", Sub = "覆盖文件,保留配置", Kind = 1 });
            _btns.Add(new Btn { Rect = new RectangleF(x + (bw + 16f) * 2f, by, bw, bh), Text = "卸载", Sub = "删 BepInEx 文件夹", Kind = 2 });

            _rBrowse = new RectangleF(W - Pad - 96f, 174f, 96f, 36f);
            _rClose = new RectangleF(W - 44f, 14f, 26f, 26f);
            _rLog = new RectangleF(Pad, 322f, w, 116f);
        }

        // ---------- 坐标换算 ----------
        // 布局全部写"逻辑坐标"(设计基准 880x620);画之前统一乘 _dp 变成设备像素。
        // ★ 踩过的坑:早先只有文字乘了 _dp、形状没乘 → 卡片和文字整体错位半行。
        //   规矩:**凡是传给 GDI+ 的坐标,一律先过 S()**;鼠标来的坐标反过来除 _dp。
        float S(float v) { return v * _dp; }
        RectangleF S(RectangleF r)
        {
            return new RectangleF(r.X * _dp, r.Y * _dp, r.Width * _dp, r.Height * _dp);
        }

        // ---------- 绘制 ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            double t = (DateTime.Now - _t0).TotalSeconds;

            PaintBg(g);
            PaintWaves(g, t);
            PaintChrome(g);
            PaintContent(g);

            base.OnPaint(e);
        }

        void PaintBg(Graphics g)
        {
            Rectangle r = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            using (LinearGradientBrush b = new LinearGradientBrush(r, Theme.BgA, Theme.BgC, 90f))
            {
                ColorBlend cb = new ColorBlend(3);
                cb.Colors = new Color[] { Theme.BgA, Theme.BgB, Theme.BgC };
                cb.Positions = new float[] { 0f, 0.46f, 1f };
                b.InterpolationColors = cb;
                g.FillRectangle(b, r);
            }
        }

        // 两层海浪:照 CSS 的 .sea__track(200% 宽、两个 50% 副本、平移一个窗宽正好一个周期)
        void PaintWaves(Graphics g, double t)
        {
            float seaTop = H - SeaH, w = W;
            RectangleF clip = new RectangleF(0, seaTop * _dp, ClientSize.Width, SeaH * _dp);

            GraphicsState st = g.Save();
            g.SetClip(clip);

            // 后排:整高、18s 正向
            DrawWaveTrack(g, Art.WaveBack, Theme.WaveBack, seaTop, w, SeaH, t, 18.0, false);
            // 前排:62% 高、26s 反向
            float fh = SeaH * 0.62f;
            DrawWaveTrack(g, Art.WaveFront, Theme.WaveFront, H - fh, w, fh, t, 26.0, true);

            g.Restore(st);
        }

        void DrawWaveTrack(Graphics g, string d, Color fill, float yTop, float w, float h, double t, double dur, bool reverse)
        {
            double phase = (t / dur) % 1.0;
            float shift = (float)(phase * w);
            shift = reverse ? (shift - w) : (-shift);

            using (SolidBrush br = new SolidBrush(fill))
            {
                for (int i = 0; i < 2; i++)
                {
                    RectangleF target = new RectangleF((i * w + shift) * _dp, yTop * _dp, w * _dp, h * _dp);
                    using (GraphicsPath p = SvgPath.Parse(d, 720f, 160f, target))
                        g.FillPath(br, p);
                }
            }
        }

        void PaintChrome(Graphics g)
        {
            // 顶栏只留一行 "INSTALLER v0.1"(主人要求:去掉左上角图标和「ICEY 作弊面板」)
            using (Font f = Theme.Mono(11f * _dp))
            using (SolidBrush b = new SolidBrush(Theme.Faint))
                g.DrawString("INSTALLER v0.1", f, b, Pad * _dp, 20f * _dp);

            // 关闭按钮
            Color cx = _hoverClose ? Theme.Hot : Theme.Faint;
            RectangleF cr = _rClose;
            using (Pen pen = new Pen(cx, 1.6f * _dp))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                float m = 9f;
                g.DrawLine(pen, (cr.X + m) * _dp, (cr.Y + m) * _dp, (cr.Right - m) * _dp, (cr.Bottom - m) * _dp);
                g.DrawLine(pen, (cr.Right - m) * _dp, (cr.Y + m) * _dp, (cr.X + m) * _dp, (cr.Bottom - m) * _dp);
            }
        }

        void PaintContent(Graphics g)
        {
            float pad = Pad, w = W - Pad * 2;

            using (Font f = Theme.SansB(34f * _dp))
            using (SolidBrush b = new SolidBrush(Theme.Ink))
                g.DrawString("ICEY V4", f, b, pad * _dp, 68f * _dp);

            using (Font f = Theme.Mono(11.5f * _dp))
            using (SolidBrush b = new SolidBrush(Theme.Accent))
                g.DrawString("ICEY  ·  BEPINEX 5  ·  HARMONYX", f, b, (pad + 3f) * _dp, 116f * _dp);

            using (Font f = Theme.Sans(13f * _dp))
            using (SolidBrush b = new SolidBrush(Theme.Sub))
                g.DrawString("只往游戏目录里加文件,不碰游戏本体;删掉即是完全卸载。", f, b, (pad + 3f) * _dp, 138f * _dp);

            // 游戏目录卡片
            RectangleF card = new RectangleF(pad, 164f, w, 56f);
            using (GraphicsPath p = Round(S(card), S(14f)))
            using (SolidBrush b = new SolidBrush(Theme.CardBg))
                g.FillPath(b, p);
            using (GraphicsPath p = Round(S(card), S(14f)))
            using (Pen pen = new Pen(Theme.Line, S(1f)))
                g.DrawPath(pen, p);

            using (Font f = Theme.Mono(10f * _dp))
            using (SolidBrush b = new SolidBrush(Theme.Faint))
                g.DrawString("游戏目录", f, b, (pad + 18f) * _dp, 172f * _dp);

            string shown = _gameDir.Length > 0 ? _gameDir : "未检测到,请点右侧『浏览…』";
            using (Font f = Theme.Sans(12.5f * _dp))
            using (SolidBrush b = new SolidBrush(_gameDir.Length > 0 ? Theme.Ink : Theme.Faint))
                g.DrawString(Ellipsis(g, shown, f, (_rBrowse.X - pad - 44f) * _dp), f, b, (pad + 18f) * _dp, 188f * _dp);

            DrawPill(g, _rBrowse, "浏览…", _hoverBrowse);

            for (int i = 0; i < _btns.Count; i++)
                DrawActionCard(g, _btns[i], _hoverBtn == i);

            // 进度条
            RectangleF track = new RectangleF(pad, 306f, w, 7f);
            using (GraphicsPath p = Round(S(track), S(3.5f)))
            using (SolidBrush b = new SolidBrush(Theme.FromArgb(0.10, Theme.Accent)))
                g.FillPath(b, p);
            if (_progress > 0f)
            {
                RectangleF fill = new RectangleF(track.X, track.Y, Math.Max(4f, track.Width * _progress), track.Height);
                using (GraphicsPath p = Round(S(fill), S(3.5f)))
                using (LinearGradientBrush b = new LinearGradientBrush(S(fill), Theme.Accent, Theme.Hot, 0f))
                    g.FillPath(b, p);
            }

            // 日志
            using (GraphicsPath p = Round(S(_rLog), S(14f)))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                g.FillPath(b, p);
            using (GraphicsPath p = Round(S(_rLog), S(14f)))
            using (Pen pen = new Pen(Theme.Line, S(1f)))
                g.DrawPath(pen, p);

            using (Font f = Theme.Mono(11f * _dp))
            {
                float ly = _rLog.Y + 11f;
                int from = Math.Max(0, _log.Count - 6);
                for (int i = from; i < _log.Count; i++)
                {
                    Color c = Theme.Sub;
                    if (_log[i].IndexOf("OK") >= 0) c = Theme.Ok;
                    if (_log[i].IndexOf("完成") >= 0) c = Theme.Accent;
                    using (SolidBrush b = new SolidBrush(c))
                        g.DrawString(_log[i], f, b, (_rLog.X + 16f) * _dp, ly * _dp);
                    ly += 15f;
                }
            }
        }

        void DrawActionCard(Graphics g, Btn bt, bool hover)
        {
            RectangleF r = bt.Rect;
            Color bg, fg, sub;

            if (bt.Kind == 0)       // 主:实心紫
            {
                bg = hover ? Theme.Over(Theme.Accent, Color.White, 0.10) : Theme.Accent;
                fg = Color.White; sub = Color.FromArgb(210, 255, 255, 255);
            }
            else if (bt.Kind == 2)  // 危险:描边粉
            {
                bg = hover ? Theme.FromArgb(0.08, Theme.Hot) : Theme.CardBg;
                fg = Theme.Hot; sub = Theme.Faint;
            }
            else                    // 次:描边
            {
                bg = hover ? Theme.CardHov : Theme.CardBg;
                fg = Theme.Accent; sub = Theme.Faint;
            }

            using (GraphicsPath p = Round(S(r), S(14f)))
            using (SolidBrush b = new SolidBrush(bg))
                g.FillPath(b, p);

            if (bt.Kind != 0)
            {
                using (GraphicsPath p = Round(S(r), S(14f)))
                using (Pen pen = new Pen(bt.Kind == 2 ? Theme.FromArgb(0.35, Theme.Hot) : Theme.LineStrong, S(1f)))
                    g.DrawPath(pen, p);
            }

            using (Font f = Theme.SansB(14.5f * _dp))
            using (SolidBrush b = new SolidBrush(fg))
                g.DrawString(bt.Text, f, b, (r.X + 18f) * _dp, (r.Y + 9f) * _dp);

            using (Font f = Theme.Sans(11f * _dp))
            using (SolidBrush b = new SolidBrush(sub))
                g.DrawString(bt.Sub, f, b, (r.X + 18f) * _dp, (r.Y + 32f) * _dp);
        }

        void DrawPill(Graphics g, RectangleF r, string text, bool hover)
        {
            Color bg = hover ? Theme.FromArgb(0.07, Theme.Accent) : Color.FromArgb(90, 255, 255, 255);
            using (GraphicsPath p = Round(S(r), S(r.Height / 2f)))
            using (SolidBrush b = new SolidBrush(bg))
                g.FillPath(b, p);
            using (GraphicsPath p = Round(S(r), S(r.Height / 2f)))
            using (Pen pen = new Pen(Theme.FromArgb(0.25, Theme.Accent), S(1f)))
                g.DrawPath(pen, p);

            using (Font f = Theme.Sans(S(12f)))
            using (SolidBrush b = new SolidBrush(Theme.Accent))
            {
                SizeF sz = g.MeasureString(text, f);
                g.DrawString(text, f, b,
                    S(r.X) + (S(r.Width) - sz.Width) / 2f,
                    S(r.Y) + (S(r.Height) - sz.Height) / 2f);
            }
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            if (rad * 2f > r.Width) rad = r.Width / 2f;
            if (rad * 2f > r.Height) rad = r.Height / 2f;
            float d = rad * 2f;
            p.AddArc(r.X, r.Y, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }

        static string Ellipsis(Graphics g, string s, Font f, float maxW)
        {
            if (g.MeasureString(s, f).Width <= maxW) return s;
            string t = s;
            while (t.Length > 4 && g.MeasureString(t + "…", f).Width > maxW) t = t.Substring(0, t.Length - 1);
            return t + "…";
        }

        // ---------- 交互 ----------
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            PointF p = new PointF(e.X / _dp, e.Y / _dp);

            int hb = -1;
            for (int i = 0; i < _btns.Count; i++) if (_btns[i].Rect.Contains(p)) hb = i;
            bool hc = _rClose.Contains(p);
            bool hr = _rBrowse.Contains(p);

            if (hb != _hoverBtn || hc != _hoverClose || hr != _hoverBrowse)
            {
                _hoverBtn = hb; _hoverClose = hc; _hoverBrowse = hr;
                Cursor = (hb >= 0 || hc || hr) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            if (_dragging)
            {
                Point cur = PointToScreen(e.Location);
                Location = new Point(cur.X - (int)_dragFrom.X, cur.Y - (int)_dragFrom.Y);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverBtn = -1; _hoverClose = false; _hoverBrowse = false;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            PointF p = new PointF(e.X / _dp, e.Y / _dp);
            if (p.Y < 52f && !_rClose.Contains(p))
            {
                _dragging = true;
                Point sp = PointToScreen(e.Location);
                _dragFrom = new PointF(sp.X - Location.X, sp.Y - Location.Y);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;

            PointF p = new PointF(e.X / _dp, e.Y / _dp);
            if (_rClose.Contains(p)) { Close(); return; }
            if (_rBrowse.Contains(p)) { Browse(); return; }

            for (int i = 0; i < _btns.Count; i++)
            {
                if (_btns[i].Rect.Contains(p))
                {
                    if (i == 0) StartJob("安装");
                    else if (i == 1) StartJob("修复重装");
                    else Uninstall();
                    return;
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        // ---------- 行为 ----------
        void Browse()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "选择 ICEY 游戏根目录(里面要有 ICEY.exe)";
                if (_gameDir.Length > 0) d.SelectedPath = _gameDir;
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    _gameDir = d.SelectedPath;
                    CheckStatus();
                }
            }
            Invalidate();
        }

        // ================= 真正的安装 / 卸载 =================
        // 跑在后台线程;回 UI 一律走 BeginInvoke(UI 控件不许跨线程碰)。
        // 日志**按顶层分组报**,不逐文件刷 41 行 —— 那一行行的没信息量。

        void StartJob(string name)
        {
            if (_busy) return;
            if (_gameDir.Length == 0) { Log("[!] 先选游戏目录。"); Invalidate(); return; }
            if (!System.IO.File.Exists(System.IO.Path.Combine(_gameDir, "ICEY.exe")))
            { Log("[!] " + _gameDir + " 里没有 ICEY.exe,已中止。"); Invalidate(); return; }

            _busy = true;
            _progress = 0f;
            Log("开始 —— " + name);
            string root = _gameDir;
            System.Threading.Thread th = new System.Threading.Thread(delegate () { DoInstall(root); });
            th.IsBackground = true;
            th.Start();
        }

        // 相对路径 → 日志分组名:BepInEx\x\y → "BepInEx\x";顶层文件 → 自己
        static string GroupOf(string rel)
        {
            string[] p = rel.Split('\\');
            if (p.Length <= 1) return rel;
            if (p[0] == "BepInEx" && p.Length >= 3) return p[0] + "\\" + p[1];
            return p[0];
        }

        void DoInstall(string root)
        {
            System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
            int total = PayloadFiles.GetLength(0);

            // 先分组,再按组解包 → 每组一行日志
            List<string> order = new List<string>();
            Dictionary<string, List<int>> map = new Dictionary<string, List<int>>();
            for (int i = 0; i < total; i++)
            {
                string g = GroupOf(PayloadFiles[i, 1]);
                if (!map.ContainsKey(g)) { map[g] = new List<int>(); order.Add(g); }
                map[g].Add(i);
            }

            int ok = 0, fail = 0, done = 0;
            foreach (string g in order)
            {
                List<int> idx = map[g];
                int gok = 0, gfail = 0;
                foreach (int i in idx)
                {
                    string res = PayloadFiles[i, 0], rel = PayloadFiles[i, 1];
                    try
                    {
                        string dst = System.IO.Path.Combine(root, rel);
                        string dir = System.IO.Path.GetDirectoryName(dst);
                        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                            System.IO.Directory.CreateDirectory(dir);
                        using (System.IO.Stream s = asm.GetManifestResourceStream(res))
                        {
                            if (s == null) { gfail++; PostLog("  [!] 缺少资源 " + res); continue; }
                            using (System.IO.FileStream f = new System.IO.FileStream(dst, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                            {
                                byte[] buf = new byte[81920];
                                int n;
                                while ((n = s.Read(buf, 0, buf.Length)) > 0) f.Write(buf, 0, n);
                            }
                        }
                        gok++;
                    }
                    catch (Exception e) { gfail++; PostLog("  [!] " + rel + " -> " + e.Message); }
                    done++;
                    PostProgress(done, total);
                }
                ok += gok; fail += gfail;
                PostLog((gfail > 0 ? "[!] " : "[OK] ") + g + " —— " + gok + " 个文件" + (gfail > 0 ? ",失败 " + gfail : ""));
            }

            // 清类型缓存:不清的话新插件有时不识别(和 Installer.cs 同一处理)
            try
            {
                string cache = System.IO.Path.Combine(root, "BepInEx", "cache");
                if (System.IO.Directory.Exists(cache)) System.IO.Directory.Delete(cache, true);
                PostLog("[OK] 已清 BepInEx 类型缓存");
            }
            catch { }

            PostDone("安装完成 —— 共 " + ok + " 个文件" + (fail > 0 ? ",失败 " + fail + " 个" : "") + "。启动游戏后按 F1 开面板。");
        }

        void StartUninstall()
        {
            if (_busy) return;
            _busy = true;
            _progress = 0f;
            Log("开始 —— 卸载");
            string root = _gameDir;
            System.Threading.Thread th = new System.Threading.Thread(delegate () { DoUninstall(root); });
            th.IsBackground = true;
            th.Start();
        }

        void DoUninstall(string root)
        {
            // 先把面板配置救出来(重装后拷回就能恢复全部开关与布局)
            try
            {
                string cfg = System.IO.Path.Combine(root, System.IO.Path.Combine("BepInEx", System.IO.Path.Combine("config", "icey-cheatpanel.txt")));
                if (System.IO.File.Exists(cfg))
                {
                    string bak = System.IO.Path.Combine(root, "_ICEY_panel_config_backup");
                    if (!System.IO.Directory.Exists(bak)) System.IO.Directory.CreateDirectory(bak);
                    System.IO.File.Copy(cfg, System.IO.Path.Combine(bak, "icey-cheatpanel.txt"), true);
                    PostLog("[OK] 面板配置已备份到 _ICEY_panel_config_backup");
                }
            }
            catch (Exception e) { PostLog("[!] 配置备份失败:" + e.Message); }

            string[] files = new string[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "使用说明.txt" };
            foreach (string f in files)
            {
                try
                {
                    string p = System.IO.Path.Combine(root, f);
                    if (System.IO.File.Exists(p)) { System.IO.File.Delete(p); PostLog("[OK] 已删除 " + f); }
                }
                catch (Exception e) { PostLog("[!] " + f + " -> " + e.Message); }
            }
            try
            {
                string b = System.IO.Path.Combine(root, "BepInEx");
                if (System.IO.Directory.Exists(b)) { System.IO.Directory.Delete(b, true); PostLog("[OK] 已删除 BepInEx 文件夹"); }
            }
            catch (Exception e) { PostLog("[!] BepInEx -> " + e.Message); }

            PostDone("卸载完成。存档和游戏本体都没动。");
            PostStatus();
        }

        void PostLog(string s)
        {
            try { BeginInvoke((MethodInvoker)delegate { Log(s); }); } catch { }
        }
        void PostProgress(int done, int total)
        {
            float v = total > 0 ? (float)done / total : 0f;
            try { BeginInvoke((MethodInvoker)delegate { _progress = v; }); } catch { }
        }
        void PostDone(string s)
        {
            try { BeginInvoke((MethodInvoker)delegate { _busy = false; _progress = 1f; Log(s); Invalidate(); }); } catch { }
        }
        void PostStatus()
        {
            try { BeginInvoke((MethodInvoker)delegate { CheckStatus(); Invalidate(); }); } catch { }
        }

        void Uninstall()
        {
            if (MessageBox.Show(this,
                "将删除以下新增内容(不碰存档和游戏本体):\n\n" +
                "  winhttp.dll / doorstop_config.ini / .doorstop_version\n" +
                "  BepInEx 文件夹(整删)\n\n" +
                "注意:如果游戏目录里还有别的 BepInEx 插件,会被一起删掉。\n" +
                "BepInEx\\config\\_savebackup 里是读档前的自动备份,想留请先拷出来。",
                "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                Log("已取消卸载。");
                Invalidate();
                return;
            }
            _progress = 0f;
            StartUninstall();
        }

        void Log(string s)
        {
            _log.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + s);
            if (_log.Count > 400) _log.RemoveAt(0);
        }

        static string AutoDetect()
        {
            string[] cands = new string[] {
                @"D:\SteamLibrary\steamapps\common\ICEY",
                @"C:\Program Files (x86)\Steam\steamapps\common\ICEY",
                @"C:\Program Files\Steam\steamapps\common\ICEY"
            };
            foreach (string c in cands)
                if (System.IO.File.Exists(System.IO.Path.Combine(c, "ICEY.exe"))) return c;
            return "";
        }

        // ---------- Win11 系统圆角(Win10 静默忽略) ----------
        [DllImport("dwmapi.dll", PreserveSig = true)]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void TryRoundCorners()
        {
            try
            {
                int pref = 2;   // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref pref, sizeof(int));
            }
            catch { }
        }
    }
}
