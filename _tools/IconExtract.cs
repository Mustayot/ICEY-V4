// IconExtract —— 从 exe/dll 里抽出 RT_GROUP_ICON + RT_ICON,重组成标准多尺寸 .ico
//
// 为什么不用 Icon.ExtractAssociatedIcon:那个只给你 32x32,大图标位图就糊了。
// 这个走 Win32 资源 API,把原始的所有尺寸(16/32/48/256)一个不落地拿出来。
// 用 LOAD_LIBRARY_AS_DATAFILE 加载,**不执行**目标模块。
//
// 用法: IconExtract.exe <源 exe> <输出 .ico>
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

static class IconExtract
{
    const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    static readonly IntPtr RT_ICON = (IntPtr)3;
    static readonly IntPtr RT_GROUP_ICON = (IntPtr)14;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibraryEx(string file, IntPtr h, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool FreeLibrary(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool EnumResourceNames(IntPtr h, IntPtr type, EnumResNameProc cb, IntPtr p);
    delegate bool EnumResNameProc(IntPtr h, IntPtr type, IntPtr name, IntPtr p);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr FindResource(IntPtr h, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LoadResource(IntPtr h, IntPtr res);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LockResource(IntPtr res);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint SizeofResource(IntPtr h, IntPtr res);

    static IntPtr _handle;
    static string _bestGroup;

    static void Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("用法: IconExtract.exe <源 exe> <输出 .ico>"); Environment.Exit(1); }
        string src = args[0], dst = args[1];

        _handle = LoadLibraryEx(src, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
        if (_handle == IntPtr.Zero) { Console.WriteLine("加载失败: " + src + " err=" + Marshal.GetLastWin32Error()); Environment.Exit(2); }

        // 找一个图标组;有多个就挑名字最小/第一个
        EnumResourceNames(_handle, RT_GROUP_ICON, delegate (IntPtr h, IntPtr t, IntPtr name, IntPtr p)
        {
            if (_bestGroup == null) _bestGroup = NameOf(name);
            return true;
        }, IntPtr.Zero);

        if (_bestGroup == null) { Console.WriteLine("这个 exe 里没有图标资源"); FreeLibrary(_handle); Environment.Exit(3); }

        byte[] grp = GetRes(RT_GROUP_ICON, _bestGroup);
        if (grp == null || grp.Length < 6) { Console.WriteLine("图标组读取失败"); FreeLibrary(_handle); Environment.Exit(4); }

        int count = BitConverter.ToUInt16(grp, 4);
        Console.WriteLine("图标组 [" + _bestGroup + "] 含 " + count + " 个尺寸");

        List<byte[]> blobs = new List<byte[]>();
        List<byte[]> entries = new List<byte[]>();

        int off = 6;
        for (int i = 0; i < count; i++)
        {
            byte w = grp[off + 0], h = grp[off + 1], colors = grp[off + 2];
            ushort planes = BitConverter.ToUInt16(grp, off + 4);
            ushort bits = BitConverter.ToUInt16(grp, off + 6);
            uint bytes = BitConverter.ToUInt32(grp, off + 8);
            ushort id = BitConverter.ToUInt16(grp, off + 12);
            off += 14;

            byte[] img = GetRes(RT_ICON, id.ToString());
            if (img == null) { Console.WriteLine("  [!] 缺 RT_ICON " + id); continue; }
            blobs.Add(img);

            byte[] e = new byte[16];
            e[0] = w; e[1] = h; e[2] = colors; e[3] = 0;
            BitConverter.GetBytes(planes).CopyTo(e, 4);
            BitConverter.GetBytes(bits).CopyTo(e, 6);
            BitConverter.GetBytes((uint)img.Length).CopyTo(e, 8);
            // 偏移稍后填
            entries.Add(e);

            Console.WriteLine(string.Format("  {0}x{1}  {2}bpp  {3} 字节  (声明 {4})",
                w == 0 ? 256 : w, h == 0 ? 256 : h, bits, img.Length, bytes));
        }

        if (blobs.Count == 0) { Console.WriteLine("没有可用图标数据"); FreeLibrary(_handle); Environment.Exit(5); }

        using (FileStream f = new FileStream(dst, FileMode.Create, FileAccess.Write))
        using (BinaryWriter bw = new BinaryWriter(f))
        {
            bw.Write((ushort)0);            // reserved
            bw.Write((ushort)1);            // type = icon
            bw.Write((ushort)blobs.Count);
            uint dataOff = (uint)(6 + 16 * blobs.Count);
            for (int i = 0; i < blobs.Count; i++)
            {
                BitConverter.GetBytes(dataOff).CopyTo(entries[i], 12);
                bw.Write(entries[i]);
                dataOff += (uint)blobs[i].Length;
            }
            foreach (byte[] b in blobs) bw.Write(b);
        }

        FreeLibrary(_handle);
        Console.WriteLine("已写出: " + dst + "  (" + new FileInfo(dst).Length + " 字节)");
    }

    static string NameOf(IntPtr name)
    {
        if ((ulong)name < 0x10000UL) return ((ulong)name).ToString();
        return Marshal.PtrToStringUni(name);
    }

    static byte[] GetRes(IntPtr type, string name)
    {
        IntPtr res = FindResource(_handle, ResName(name), type);
        if (res == IntPtr.Zero) return null;
        IntPtr loaded = LoadResource(_handle, res);
        if (loaded == IntPtr.Zero) return null;
        uint size = SizeofResource(_handle, res);
        IntPtr ptr = LockResource(loaded);
        if (ptr == IntPtr.Zero || size == 0) return null;
        byte[] buf = new byte[size];
        Marshal.Copy(ptr, buf, 0, (int)size);
        return buf;
    }

    static IntPtr ResName(string s)
    {
        ushort id;
        if (ushort.TryParse(s, out id)) return (IntPtr)id;
        return Marshal.StringToHGlobalUni(s);
    }
}
