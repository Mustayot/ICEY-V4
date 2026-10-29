// 命令行模式 —— 自动化 / 自测用。
//   ICEY V4 安装器.exe install   <游戏目录>
//   ICEY V4 安装器.exe uninstall <游戏目录>
//
// 为什么要它:装解逻辑必须能**在没有鼠标点击的情况下**被验证,
// 而且要能装到一个空目录里跑,不必拿真实游戏目录做试验。
// 注意本程序是 /target:winexe,GUI 子系统没有控制台 —— 所以结果同时写一份 install-log.txt,
// 并且用退出码表态(0=全成功 / 1=参数或前置检查失败 / 2=有文件失败)。
using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace IceyGui
{
    static class Cli
    {
        public static int Run(string mode, string root)
        {
            StringBuilder log = new StringBuilder();
            Action<string> L = delegate (string s) { log.AppendLine(s); Console.WriteLine(s); };

            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            { L("[!] 目录不存在: " + root); Flush(root, log); return 1; }
            if (!File.Exists(Path.Combine(root, "ICEY.exe")))
            { L("[!] " + root + " 里没有 ICEY.exe,已中止。"); Flush(root, log); return 1; }

            if (mode == "uninstall") return Uninstall(root, L, log);
            return Install(root, L, log);
        }

        static int Install(string root, Action<string> L, StringBuilder log)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            int total = GeneratedPayload.Files.GetLength(0);
            int ok = 0, fail = 0;
            for (int i = 0; i < total; i++)
            {
                string res = GeneratedPayload.Files[i, 0], rel = GeneratedPayload.Files[i, 1];
                try
                {
                    string dst = Path.Combine(root, rel);
                    string dir = Path.GetDirectoryName(dst);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    using (Stream s = asm.GetManifestResourceStream(res))
                    {
                        if (s == null) { fail++; L("[!] 缺少资源 " + res); continue; }
                        using (FileStream f = new FileStream(dst, FileMode.Create, FileAccess.Write))
                        {
                            byte[] buf = new byte[81920];
                            int n;
                            while ((n = s.Read(buf, 0, buf.Length)) > 0) f.Write(buf, 0, n);
                        }
                    }
                    ok++;
                }
                catch (Exception e) { fail++; L("[!] " + rel + " -> " + e.Message); }
            }
            try
            {
                string cache = Path.Combine(root, @"BepInEx\cache");
                if (Directory.Exists(cache)) Directory.Delete(cache, true);
                L("[OK] 已清 BepInEx 类型缓存");
            }
            catch { }

            L("[OK] 已安装 " + ok + "/" + total + " 个文件" + (fail > 0 ? ",失败 " + fail : ""));
            Flush(root, log);
            return fail == 0 ? 0 : 2;
        }

        static int Uninstall(string root, Action<string> L, StringBuilder log)
        {
            try
            {
                string cfg = Path.Combine(root, @"BepInEx\config\icey-cheatpanel.txt");
                if (File.Exists(cfg))
                {
                    string bak = Path.Combine(root, "_ICEY_panel_config_backup");
                    if (!Directory.Exists(bak)) Directory.CreateDirectory(bak);
                    File.Copy(cfg, Path.Combine(bak, "icey-cheatpanel.txt"), true);
                    L("[OK] 面板配置已备份");
                }
            }
            catch (Exception e) { L("[!] 配置备份失败:" + e.Message); }

            string[] files = new string[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "使用说明.txt" };
            foreach (string f in files)
            {
                try { string p = Path.Combine(root, f); if (File.Exists(p)) { File.Delete(p); L("[OK] 删除 " + f); } }
                catch (Exception e) { L("[!] " + f + " -> " + e.Message); }
            }
            try
            {
                string b = Path.Combine(root, "BepInEx");
                if (Directory.Exists(b)) { Directory.Delete(b, true); L("[OK] 删除 BepInEx 文件夹"); }
            }
            catch (Exception e) { L("[!] BepInEx -> " + e.Message); }

            L("[OK] 卸载完成");
            Flush(root, log);
            return 0;
        }

        static void Flush(string root, StringBuilder log)
        {
            try
            {
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                    File.WriteAllText(Path.Combine(root, "install-log.txt"), log.ToString(),
                                      new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
