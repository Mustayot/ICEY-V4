using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

static class Program
{
    // 载荷文件表由 pack-installer.ps1 生成(GeneratedPayload.g.cs):资源名 f0..fN 必须与
    // 内嵌顺序严格一致,手维护这个表迟早会错位(错位了只会打一行"缺少资源",静默装不全)。
    static readonly string[,] Files = GeneratedPayload.Files;

    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Console.WriteLine("========================================");
        Console.WriteLine("  ICEY 作弊面板 · 安装 / 卸载器   v0.7.2");
        Console.WriteLine("========================================");

        string mode = "";
        string target = null;
        if (args.Length >= 2) { mode = args[0].ToLower(); target = args[1]; }
        else if (args.Length == 1 && (args[0] == "install" || args[0] == "uninstall")) mode = args[0];
        else if (args.Length == 1) target = args[0];

        if (target == null) target = AutoDetect();
        if (target == null)
        {
            Console.Write("请输入游戏根目录(里面要有 ICEY.exe): ");
            target = Console.ReadLine();
        }
        if (string.IsNullOrEmpty(target)) { Console.WriteLine("[!] 没有目标目录。"); Pause(); return 1; }
        target = target.Trim().Trim('"');
        if (!File.Exists(Path.Combine(target, "ICEY.exe")))
        { Console.WriteLine("[!] " + target + " 里没有 ICEY.exe,已中止。"); Pause(); return 1; }
        Console.WriteLine("目标目录: " + target);
        Console.WriteLine();

        if (mode == "") {
            Console.WriteLine("  1 = 安装");
            Console.WriteLine("  2 = 卸载");
            Console.WriteLine("  0 = 退出");
            Console.Write("选择: ");
            string c = Console.ReadLine();
            if (c == "1") mode = "install"; else if (c == "2") mode = "uninstall"; else return 0;
        }

        if (mode == "uninstall") return Uninstall(target);
        return Install(target);
    }

    static string AutoDetect()
    {
        string self = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (File.Exists(Path.Combine(self, "ICEY.exe"))) return self;
        string[] guess = new string[] {
            @"D:\SteamLibrary\steamapps\common\ICEY",
            @"E:\SteamLibrary\steamapps\common\ICEY",
            @"C:\Program Files (x86)\Steam\steamapps\common\ICEY",
            @"C:\Program Files\Steam\steamapps\common\ICEY",
            @"D:\Steam\steamapps\common\ICEY"
        };
        for (int i = 0; i < guess.Length; i++)
            if (File.Exists(Path.Combine(guess[i], "ICEY.exe"))) return guess[i];
        return null;
    }

    static int Install(string root)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        int ok = 0, fail = 0;
        for (int i = 0; i < Files.GetLength(0); i++)
        {
            string res = Files[i, 0], rel = Files[i, 1];
            string dst = Path.Combine(root, rel);
            try
            {
                string dir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                using (Stream s = asm.GetManifestResourceStream(res))
                {
                    if (s == null) { fail++; Console.WriteLine("  [!] 缺少资源 " + res); continue; }
                    using (FileStream f = new FileStream(dst, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buf = new byte[81920];
                        int n;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0) f.Write(buf, 0, n);
                    }
                }
                ok++;
            }
            catch (Exception e) { fail++; Console.WriteLine("  [!] " + rel + " -> " + e.Message); }
        }
        Console.WriteLine();
        Console.WriteLine("[OK] 已安装 " + ok + " 个文件" + (fail > 0 ? (",失败 " + fail + " 个") : "") + "。");
        Console.WriteLine("     启动游戏(Steam 或直接双击 ICEY.exe),进游戏后按 F1 开关面板。");
        Console.WriteLine("     不想用了:再运行一次本程序,选 2 卸载。");
        Console.Write("现在启动游戏吗? (y/n): ");
        string a = Console.ReadLine();
        if (a != null && a.Trim().ToLower() == "y")
        {
            try { Process.Start(Path.Combine(root, "ICEY.exe")); } catch (Exception e) { Console.WriteLine("启动失败: " + e.Message); }
        }
        Pause();
        return 0;
    }

    static int Uninstall(string root)
    {
        Console.WriteLine("将删除以下新增内容(不会碰存档和游戏本体):");
        Console.WriteLine("  winhttp.dll / doorstop_config.ini / .doorstop_version / BepInEx 文件夹");
        Console.WriteLine("注意: BepInEx\\config\\_savebackup 里是读档前的自动备份,想留请先拷出来。");
        Console.Write("确认卸载? (y/n): ");
        string a = Console.ReadLine();
        if (a == null || a.Trim().ToLower() != "y") { Console.WriteLine("已取消。"); Pause(); return 0; }
        // Keep the panel's settings alive across an uninstall: copy them next to the game first.
        try
        {
            string cfg = Path.Combine(root, Path.Combine("BepInEx", Path.Combine("config", "icey-cheatpanel.txt")));
            if (File.Exists(cfg))
            {
                string bak = Path.Combine(root, "_ICEY_panel_config_backup");
                if (!Directory.Exists(bak)) Directory.CreateDirectory(bak);
                File.Copy(cfg, Path.Combine(bak, "icey-cheatpanel.txt"), true);
                Console.WriteLine("[OK] 面板配置已备份到 " + bak);
                Console.WriteLine("     重装后把它拷回 BepInEx\\config\\ 即可恢复全部开关与布局。");
            }
        }
        catch (Exception e) { Console.WriteLine("[!] 配置备份失败: " + e.Message); }

        TryDelete(Path.Combine(root, "winhttp.dll"));
        TryDelete(Path.Combine(root, "doorstop_config.ini"));
        TryDelete(Path.Combine(root, ".doorstop_version"));
        TryDelete(Path.Combine(root, "使用说明.txt"));
        TryDeleteDir(Path.Combine(root, "BepInEx"));
        Console.WriteLine("[OK] 已卸载。");
        Pause();
        return 0;
    }

    static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch (Exception e) { Console.WriteLine("[!] " + p + ": " + e.Message); } }
    static void TryDeleteDir(string p) { try { if (Directory.Exists(p)) Directory.Delete(p, true); } catch (Exception e) { Console.WriteLine("[!] " + p + ": " + e.Message); } }
    static void Pause() { Console.WriteLine(); Console.Write("按回车关闭…"); Console.ReadLine(); }
}
