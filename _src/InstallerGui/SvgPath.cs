// 迷你 SVG path 解析器 —— 把源码里的 path d 字符串变成 GDI+ 的 GraphicsPath。
// 支持: M m L l H h V v C c S s Z z  (源码里用到的恰好就这几个,没有弧线 A)
// 不支持 Q/q T/t A/a —— 遇到会抛异常,免得"静默画错"(本项目铁律:能自证)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace IceyGui
{
    static class SvgPath
    {
        const string Cmds = "MmLlHhVvCcSsQqTtAaZz";

        /// <summary>解析 d,按 viewBox 等比映射到 target 矩形。FillMode 用 Winding(SVG 默认)。</summary>
        public static GraphicsPath Parse(string d, float vbW, float vbH, RectangleF target)
        {
            GraphicsPath path = new GraphicsPath();
            List<object> tok = Tokenize(d);
            float sx = target.Width / vbW, sy = target.Height / vbH;
            float ox = target.X, oy = target.Y;

            float x = 0, y = 0;      // 当前点(SVG 用户坐标)
            float mx = 0, my = 0;    // 子路径起点
            float cx = 0, cy = 0;    // 上一个三次控制点(给 S/s 镜像用)
            char prev = ' ';

            float px = 0, py = 0;    // 当前点(设备坐标)
            bool started = false;

            char cmd = 'M';
            int i = 0;

            while (i < tok.Count)
            {
                object t = tok[i];
                if (t is char)
                {
                    cmd = (char)t;
                    i++;
                    if (cmd == 'Z' || cmd == 'z')
                    {
                        path.CloseFigure();
                        x = mx; y = my; px = ox + x * sx; py = oy + y * sy;
                        prev = cmd;
                        continue;
                    }
                }

                bool rel = char.IsLower(cmd);
                char up = char.ToUpperInvariant(cmd);

                // 取一组参数;不够(或碰到下一个命令)就收工
                float[] a = Take(tok, ref i, up == 'H' || up == 'V' ? 1 : (up == 'S' ? 4 : (up == 'M' || up == 'L' ? 2 : 6)));
                if (a == null) break;

                if (up == 'M')
                {
                    x = rel ? x + a[0] : a[0];
                    y = rel ? y + a[1] : a[1];
                    mx = x; my = y;
                    path.StartFigure();
                    px = ox + x * sx; py = oy + y * sy;
                    started = true;
                    cmd = rel ? 'l' : 'L';   // 隐式 lineto
                    prev = 'M';
                }
                else if (up == 'L')
                {
                    x = rel ? x + a[0] : a[0];
                    y = rel ? y + a[1] : a[1];
                    float nx = ox + x * sx, ny = oy + y * sy;
                    if (!started) { path.StartFigure(); px = nx; py = ny; started = true; }
                    else path.AddLine(px, py, nx, ny);
                    px = nx; py = ny;
                    prev = 'L';
                }
                else if (up == 'H')
                {
                    x = rel ? x + a[0] : a[0];
                    float nx = ox + x * sx, ny = py;
                    if (!started) { path.StartFigure(); px = nx; py = ny; started = true; }
                    else path.AddLine(px, py, nx, ny);
                    px = nx; py = ny;
                    prev = 'H';
                }
                else if (up == 'V')
                {
                    y = rel ? y + a[0] : a[0];
                    float nx = px, ny = oy + y * sy;
                    if (!started) { path.StartFigure(); px = nx; py = ny; started = true; }
                    else path.AddLine(px, py, nx, ny);
                    px = nx; py = ny;
                    prev = 'V';
                }
                else if (up == 'C')
                {
                    float x1 = rel ? x + a[0] : a[0], y1 = rel ? y + a[1] : a[1];
                    float x2 = rel ? x + a[2] : a[2], y2 = rel ? y + a[3] : a[3];
                    float ex = rel ? x + a[4] : a[4], ey = rel ? y + a[5] : a[5];
                    float c1x = ox + x1 * sx, c1y = oy + y1 * sy;
                    float c2x = ox + x2 * sx, c2y = oy + y2 * sy;
                    float exd = ox + ex * sx, eyd = oy + ey * sy;
                    if (!started) { path.StartFigure(); px = c1x; py = c1y; started = true; }
                    path.AddBezier(px, py, c1x, c1y, c2x, c2y, exd, eyd);
                    cx = x2; cy = y2; x = ex; y = ey;
                    px = exd; py = eyd;
                    prev = 'C';
                }
                else if (up == 'S')
                {
                    float x1, y1;
                    if (prev == 'C' || prev == 'S') { x1 = 2 * x - cx; y1 = 2 * y - cy; }
                    else { x1 = x; y1 = y; }
                    float x2 = rel ? x + a[0] : a[0], y2 = rel ? y + a[1] : a[1];
                    float ex = rel ? x + a[2] : a[2], ey = rel ? y + a[3] : a[3];
                    float c1x = ox + x1 * sx, c1y = oy + y1 * sy;
                    float c2x = ox + x2 * sx, c2y = oy + y2 * sy;
                    float exd = ox + ex * sx, eyd = oy + ey * sy;
                    if (!started) { path.StartFigure(); px = c1x; py = c1y; started = true; }
                    path.AddBezier(px, py, c1x, c1y, c2x, c2y, exd, eyd);
                    cx = x2; cy = y2; x = ex; y = ey;
                    px = exd; py = eyd;
                    prev = 'S';
                }
                else
                {
                    throw new NotSupportedException("SvgPath 不支持的命令: " + cmd);
                }
            }
            return path;
        }

        /// <summary>尝试取 n 个数字;下一个是命令或不够就返回 null</summary>
        static float[] Take(List<object> tok, ref int i, int n)
        {
            if (i + n > tok.Count) return null;
            float[] a = new float[n];
            for (int k = 0; k < n; k++)
            {
                if (tok[i + k] is char) return null;
                a[k] = (float)tok[i + k];
            }
            i += n;
            return a;
        }

        static List<object> Tokenize(string d)
        {
            List<object> toks = new List<object>();
            int i = 0;
            while (i < d.Length)
            {
                char c = d[i];
                if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
                if (Cmds.IndexOf(c) >= 0) { toks.Add(c); i++; continue; }

                int s = i;
                if (i < d.Length && (d[i] == '+' || d[i] == '-')) i++;
                while (i < d.Length && char.IsDigit(d[i])) i++;
                if (i < d.Length && d[i] == '.') { i++; while (i < d.Length && char.IsDigit(d[i])) i++; }
                if (i < d.Length && (d[i] == 'e' || d[i] == 'E'))
                {
                    int save = i; i++;
                    if (i < d.Length && (d[i] == '+' || d[i] == '-')) i++;
                    if (i < d.Length && char.IsDigit(d[i])) { while (i < d.Length && char.IsDigit(d[i])) i++; }
                    else i = save;
                }
                if (i == s) { i++; continue; }
                toks.Add(float.Parse(d.Substring(s, i - s), CultureInfo.InvariantCulture));
            }
            return toks;
        }
    }
}
