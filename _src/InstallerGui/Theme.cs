// ICEY 作弊面板 · GUI 安装器 —— 配色与字体
// 配色**逐条抄自** mustayot.love 的 CSS :root(home-mustayot-love-源码-20261004.html)
// 对应关系写在各行注释里,改色请对照那个源码,别凭感觉调。
using System;
using System.Drawing;
using System.Drawing.Text;

namespace IceyGui
{
    static class Theme
    {
        // ---- 文字三档 ----
        public static readonly Color Ink   = FromHex("2C2740");  // --ink
        public static readonly Color Sub   = FromHex("6F6A86");  // --sub
        public static readonly Color Faint = FromHex("A49FB5");  // --faint

        // ---- 强调色 ----
        public static readonly Color Accent = FromHex("8E44D9");  // --accent 紫
        public static readonly Color Hot    = FromHex("FF6B9D");  // --hot    热粉
        public static readonly Color Ok     = FromHex("1C8A5A");  // --ok     成功绿

        // ---- 描边 ----
        public static readonly Color Line      = FromArgb(0.12, Ink);  // --line
        public static readonly Color LineStrong= FromArgb(0.18, Ink);  // --line-strong

        // ---- 背景三停渐变: linear-gradient(178deg,#e9f5ff,#f1ecff 46%,#fff0f8) ----
        public static readonly Color BgA = FromHex("E9F5FF");  // 0%
        public static readonly Color BgB = FromHex("F1ECFF");  // 46%
        public static readonly Color BgC = FromHex("FFF0F8");  // 100%
        public static readonly Color BgFlat = FromHex("F4F0FF"); // body background

        // ---- 海浪两层(源码里的 fill 就是这两个 rgba) ----
        public static readonly Color WaveBack  = FromArgb(0.34, FromHex("E8A1FF")); // 后排
        public static readonly Color WaveFront = FromArgb(0.50, FromHex("A8C8FF")); // 前排

        // ---- 卡片 / 图标底 ----
        public static readonly Color CardBg  = Color.FromArgb(150, 255, 255, 255);
        public static readonly Color CardHov = FromArgb(0.035, Accent);
        public static readonly Color IcoBg   = FromArgb(0.16, FromHex("E8A1FF"));   // card__ico 背景
        public static readonly Color IcoLine = FromArgb(0.16, Accent);              // card__ico 描边

        // ---- 鲸鱼(sprite 里的填色与描边) ----
        public static readonly Color WhA = FromHex("A8C8FF");
        public static readonly Color WhB = FromHex("BBA5FF");
        public static readonly Color WhC = FromHex("E8A1FF");
        public static readonly Color WhFin = FromHex("B79BF6");
        public static readonly Color WhEye = FromHex("3B3157");
        public static readonly Color WhBlush = FromHex("FF9EC4");
        public static readonly Color WhSpout = FromHex("8FB6E8");

        // ---- 字体(源码 --sans / --mono 在 Windows 上的对应) ----
        public static readonly string SansFamily = PickFont(new string[] {
            "Microsoft YaHei UI", "微软雅黑", "Microsoft YaHei", "Segoe UI", "SimSun" });
        public static readonly string MonoFamily = PickFont(new string[] {
            "Cascadia Code", "Consolas", "Courier New" });

        public static Font Sans(float size) { return new Font(SansFamily, size, FontStyle.Regular, GraphicsUnit.Pixel); }
        public static Font SansB(float size) { return new Font(SansFamily, size, FontStyle.Bold, GraphicsUnit.Pixel); }
        public static Font Mono(float size) { return new Font(MonoFamily, size, FontStyle.Regular, GraphicsUnit.Pixel); }

        // 让文字不糊:统一走 Graphics.TextRenderingHint 设置,见 Gui.cs

        // ---------- 小工具 ----------
        static Color FromHex(string hex)
        {
            int v = int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
            return Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        }

        /// <summary>把某个颜色按 alpha 比例叠成半透明(等价 CSS 的 rgba(r,g,b,a))</summary>
        public static Color FromArgb(double alpha, Color c)
        {
            int a = (int)Math.Round(alpha * 255.0);
            if (a < 0) a = 0; if (a > 255) a = 255;
            return Color.FromArgb(a, c);
        }

        /// <summary>在 C 上叠加一层半透明的 c(等价 CSS 叠色),用于 hover 之类的近似</summary>
        public static Color Over(Color c, Color top, double topAlpha)
        {
            double a = topAlpha;
            int r = (int)Math.Round(top.R * a + c.R * (1 - a));
            int g = (int)Math.Round(top.G * a + c.G * (1 - a));
            int b = (int)Math.Round(top.B * a + c.B * (1 - a));
            return Color.FromArgb(c.A, Clamp(r), Clamp(g), Clamp(b));
        }

        static int Clamp(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }

        static string PickFont(string[] names)
        {
            try
            {
                using (InstalledFontCollection col = new InstalledFontCollection())
                {
                    foreach (string want in names)
                        foreach (FontFamily f in col.Families)
                            if (string.Equals(f.Name, want, StringComparison.OrdinalIgnoreCase))
                                return f.Name;
                }
            }
            catch { }
            return "Arial";
        }
    }
}
