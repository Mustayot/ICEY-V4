#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
ICEY 图集工具 —— 导出 / 调色 / 布局清单 / 回写 / 校验 / 还原 主角图集贴图

依赖(已装,不要 pip install):
    UnityPy            —— 只读对象元数据。**绝不要碰 .image / UnityPy.export**,
                          它在 Py3.13 + UnityPy 1.25.4 上抛 OverflowError。
                          本工具自己按 m_StreamData.offset/size 从 .resS 切字节。
    texture2ddecoder   —— 解 DXT(BC1/BC3)
    etcpak             —— 压 DXT
    Pillow             —— 存/读/比 PNG
    numpy              —— 本工具**不用**(该环境里 numpy 自身 import 就坏)

用法(--target 与 --i-know-what-im-doing 写在子命令**前**或**后**都行):
  python atlas_tool.py list
  python atlas_tool.py export  [--target copy] [--out DIR]
  python atlas_tool.py palette [--target copy] [--preset sakura|pink|purple|blue|gold|red|cyan]
                               [--lighten F] [--saturate F] [--min-sat N] [--colorize F]
                               [--out SRC_DIR] [--png-dir DST_DIR]

    --colorize F  整体染色强度 0.0~1.0;不传 = 用预设默认(sakura/blue 0.30,其余 0.25)。
                  两层效果:先按老策略做「保护式色相偏移」(只动青色像素),
                  再把可见像素按亮度往目标色拉 —— 灰白护甲也上色,黑描边保留。
                  "可见"分两种:基础图集 = alpha>8;特效图(文件名含 Flash/Illum)= **整张**,
                  因为 PlayerFlash 连 alpha=0 的像素 RGB 都是青色(那层光效走加色混合,
                  只染"看得见的"等于没染)。alpha 通道永远原样贴回。
                  --colorize 0 = 关闭第二层。
                  注意:第一层自 2026-10-07 起加了"基础图集不动透明区"的修正,
                  所以 --colorize 0 的输出**不再**等于加这个参数之前的老版本(差别在看不见的像素上)。
  python atlas_tool.py atlas   [--target copy] [--out DIR]
  python atlas_tool.py regions [--target copy] [--atlas-dir DIR] [--out DIR] [--quiet]
  python atlas_tool.py mcskin  [--skin PNG] [--pixel N] [--out DIR] [--install]
                               [--tint-illum [F]] [--target copy]

    --skin PNG   Minecraft 64x64 皮肤,默认 _work/mcskin/German.png(必须标准 64x64 布局)。
    --pixel N    方块边长,默认 4;邻近采样 + N 像素方块量化(1 = 逐像素,不量化)。
    --out DIR    输出目录,默认 _work/player_atlas_mc(11 张,命名与原图一致)。
    --install    额外把这 11 张拷进 _moddev\\BepInEx\\config\\player_skin\\皮肤-贴图版\\
                 (以及 _dist\\ICEYCheatPanel 的同名位置,存在才拷)。
    --tint-illum [F]  _Illum 4 张 + PlayerFlash 默认**原样拷贝**;给了这个开关就整体
                 往皮肤主色调拉,F 默认 0.20、硬上限 0.25(F<=0 = 不染)。

    做法:按区域名把区域分成 头/头发/躯干/手臂/腿 五类,每类取皮肤上固定的矩形,
    按归一化坐标均匀铺满该区域(邻近采样),旋转区域(rotate: true)把 u/v 对调填
    footprint —— **旋转区域是近似,可能差 90 度**。只写原图 alpha > 8 的像素,
    透明区一个字节都不动,最后 putalpha(原 alpha);同时打印逐像素的 alpha 未变证据。
    顺带产出 _work/_mcskin_皮肤预览.png(皮肤放大 6 倍)与 _work/_mcskin_对比.png。
  python atlas_tool.py apply   --png-dir DIR [--only A,B] [--dry-run] [--target copy]
  python atlas_tool.py verify  [--png-dir DIR] [--target copy]
  python atlas_tool.py restore [--target copy]

  等价写法示例(两种顺序都能跑):
  python atlas_tool.py export --target original
  python atlas_tool.py --target original export

安全设计(必须保留):
  * 默认只允许改 **副本** (_moddev)。要改原版必须显式 --target original
    **并且**加 --i-know-what-im-doing。
  * 写之前强制备份被覆盖的原始字节到 _work/atlas_backup/ (每张几 MB,不是整个 resS)。
  * 写入前校验编码后字节数必须等于该格式基础层(mip0)字节数,不等就拒绝写。
  * apply 只覆盖基础层;槽位更大时(带 mip 链)尾部原样保留,写后回读校验尾部未变。
"""

import argparse
import json
import os
import sys
import time

ORIGINAL = r"D:\SteamLibrary\steamapps\common\ICEY"
COPY = os.path.join(ORIGINAL, "_moddev")
WORK = os.path.join(COPY, "_work")
BACKUP = os.path.join(WORK, "atlas_backup")
MANIFEST = os.path.join(BACKUP, "manifest.json")
DEFAULT_ATLAS_DIR = os.path.join(WORK, "player_atlas")

# 主角视觉的全部贴图:6 张主图集(对应 6 个骨架槽)+ 4 张自发光 + 闪现
TARGETS = [
    "NewPlayerNormal",          # normalSkeleton
    "NewPlayerAtk",             # atkSkeleton
    "NewPlayerAtkHV",           # heavyAtkkeleton
    "NewPlayerSpAtk",           # SP
    "NewPlayerUnderAtk",        # hurtSkeleton
    "NewPlayerAtkUpRising",     # upRisingkeleton
    "NewPlayerAtk_Illum",
    "NewPlayerNormal_Illum",
    "NewPlayerSPAtk_Illum",
    "NewPlayerAtkUpRising_Illum",
    "PlayerFlash",
]

# TextureFormat 枚举里我们用到的几个
FMT_ARGB32, FMT_RGB24, FMT_RGBA32, FMT_DXT1, FMT_DXT5 = 5, 3, 4, 10, 12
FMT_NAME = {5: "ARGB32", 3: "RGB24", 4: "RGBA32", 10: "DXT1", 12: "DXT5"}

# 预设配色:名字 -> (色相目标, 预设提亮倍数, 预设饱和倍数, 预设染色强度)
#   sakura 是默认:在 pink 的色相上再提亮 + 降一点饱和,做柔和浅粉。
#   其余预设保持原语义(只换色相,提亮/降饱和都是 1.0)。
#   第 4 项 colorize 是「整体染色强度」(0.0~1.0):只改青色像素时护甲/皮肤那种接近
#   灰白的部分看不出来,这一层把**整张图**(alpha>8 且非黑描边)往目标色调拉。
PRESETS = {
    "sakura": (234, 1.10, 0.85, 0.30),
    "pink":   (234, 1.00, 1.00, 0.25),
    "purple": (200, 1.00, 1.00, 0.25),
    "blue":   (170, 1.00, 1.00, 0.30),
    "gold":   (30,  1.00, 1.00, 0.25),
    "red":    (250, 1.00, 1.00, 0.25),
    "cyan":   (128, 1.00, 1.00, 0.25),
}
HUE_LO, HUE_HI = 100, 165     # 只改这个青色区间(保护皮肤/描边/白高光)
LIGHTEN_MAX = 1.15            # 提亮硬上限:超过就把图洗白了
SATURATE_MIN = 0.75           # 降饱和硬下限
DIFF_WARN = 3.0               # 回读平均通道差超过它就警告(单位:0~255)

# 整体染色(colorize)相关的常数
COLORIZE_SAT = 0.55           # 目标色的饱和度(S)
COLORIZE_VAL = 0.95           # 目标色亮版的明度(V) —— ImageOps.colorize 的 white
COLORIZE_VAL_DARK = 0.15      # 目标色暗版的明度(V) —— ImageOps.colorize 的 black
COLORIZE_MIN_ALPHA = 8        # 只染 alpha > 8 的像素(透明区是骨架形状,不许碰)
OUTLINE_LUMA = 25             # 亮度 < 25 视为黑描边,保持原样(染了会发灰,难看)

# .atlas 文本里的头部键(缩进 0),不是区域名
ATLAS_HEADER_KEYS = ("size", "format", "filter", "repeat", "split", "pad", "pma", "scale")


def game_dir(target):
    return ORIGINAL if target == "original" else COPY


def assets_paths(target):
    d = os.path.join(game_dir(target), "ICEY_Data")
    return os.path.join(d, "resources.assets"), os.path.join(d, "resources.assets.resS")


def load_meta(target):
    """只读对象元数据:名字 / 尺寸 / 格式 / 在 resS 里的偏移与大小 / 所属 assets 文件。

    同时返回游戏里**全部** .atlas TextAsset 文本(调用方自己按需筛选)。
    """
    import UnityPy
    a, r = assets_paths(target)
    env = UnityPy.load(a)
    meta = {}
    for o in env.objects:
        if o.type.name != "Texture2D":
            continue
        try:
            d = o.read()
        except Exception:
            continue
        nm = d.m_Name
        if nm in TARGETS and nm not in meta:
            sd = d.m_StreamData
            meta[nm] = {
                "width": int(d.m_Width), "height": int(d.m_Height),
                "format": int(d.m_TextureFormat),
                "offset": int(sd.offset), "size": int(sd.size),
                "resS": os.path.basename(sd.path or ""),
            }
    # atlas 文本(UV 布局)也一起收集
    texts = {}
    for o in env.objects:
        if o.type.name != "TextAsset":
            continue
        try:
            d = o.read()
        except Exception:
            continue
        nm = d.m_Name or ""
        if nm.endswith(".atlas"):
            s = d.m_Script
            texts[nm] = s if isinstance(s, str) else s.decode("utf-8", "replace")
    return meta, texts


def decode(fmt, raw, w, h):
    import texture2ddecoder
    from PIL import Image
    if fmt == FMT_DXT5:
        return Image.frombytes("RGBA", (w, h), texture2ddecoder.decode_bc3(raw, w, h), "raw", "BGRA")
    if fmt == FMT_DXT1:
        return Image.frombytes("RGBA", (w, h), texture2ddecoder.decode_bc1(raw, w, h), "raw", "BGRA")
    if fmt == FMT_ARGB32:
        return Image.frombytes("RGBA", (w, h), raw[:w * h * 4], "raw", "BGRA")
    if fmt == FMT_RGBA32:
        return Image.frombytes("RGBA", (w, h), raw[:w * h * 4], "raw", "RGBA")
    if fmt == FMT_RGB24:
        return Image.frombytes("RGB", (w, h), raw[:w * h * 3], "raw", "RGB")
    raise ValueError("不支持的贴图格式 %s" % fmt)


def encode(fmt, img, w, h):
    """把 PIL 图编码回原格式;返回的字节数必须等于 w*h*每像素字节数"""
    import etcpak
    if img.size != (w, h):
        raise ValueError("尺寸不符:图是 %s,原槽位是 %sx%s" % (img.size, w, h))
    if fmt == FMT_DXT5:
        return etcpak.compress_bc3(img.convert("RGBA").tobytes(), w, h)
    if fmt == FMT_DXT1:
        return etcpak.compress_bc1(img.convert("RGBA").tobytes(), w, h)
    if fmt == FMT_ARGB32:
        return img.convert("RGBA").tobytes("raw", "BGRA")
    if fmt == FMT_RGBA32:
        return img.convert("RGBA").tobytes()
    if fmt == FMT_RGB24:
        return img.convert("RGB").tobytes()
    raise ValueError("不支持的贴图格式 %s" % fmt)


def base_size(fmt, w, h):
    """该格式的『基础层(mip0)』字节数。注意有些贴图槽位更大 —— 那说明它带完整 mip 链
    (例:2048² DXT1 基础层 2097152,带 mip 的槽位是 2796216 ≈ ×4/3)。"""
    if fmt in (FMT_DXT1, FMT_DXT5):
        blocks = max(1, (w + 3) // 4) * max(1, (h + 3) // 4)
        return blocks * (16 if fmt == FMT_DXT5 else 8)
    if fmt in (FMT_ARGB32, FMT_RGBA32):
        return w * h * 4
    if fmt == FMT_RGB24:
        return w * h * 3
    raise ValueError("不支持的贴图格式 %s" % fmt)


def hsv_to_rgb255(h, s, v):
    """HSV(h 用 PIL 的 0~255 色相刻度,s/v 是 0~1)-> (R,G,B) 0~255。

    刻意用标准库 colorsys,不引入 numpy(本机 numpy import 就炸)。
    """
    import colorsys
    r, g, b = colorsys.hsv_to_rgb((h % 256) / 256.0, s, v)
    return (int(r * 255 + 0.5), int(g * 255 + 0.5), int(b * 255 + 0.5))


def colorize_pair(hue):
    """目标色相 -> (亮版, 暗版),给 ImageOps.colorize 的 white / black 用"""
    return (hsv_to_rgb255(hue, COLORIZE_SAT, COLORIZE_VAL),
            hsv_to_rgb255(hue, COLORIZE_SAT, COLORIZE_VAL_DARK))


def colorize_image(base_rgb, vis_mask, hue, strength):
    """在「色相偏移 + 提亮/降饱和」的结果之上,再叠一层整体染色。

    * 亮度取当前结果自己的 L(luminance),用 ImageOps.colorize 按亮度上色
      —— 所以明暗细节和相对层次保留,只是整体带上目标色调;
    * 只作用在 vis_mask 为 255 的像素上。vis_mask 由调用方给:
      - 基础图集 = alpha > COLORIZE_MIN_ALPHA(透明区是骨架形状,一个像素都不许动);
      - **特效图(Flash / Illum)= 全 255**。原因见 cmd_palette 里的注释:
        PlayerFlash 这种图整张都是青色(连 alpha=0 的像素都是),那层光效很可能用加色混合
        (Blend One One,RGB 不乘 alpha),只染"看得见"的像素等于没染。
    * 亮度 < OUTLINE_LUMA 的像素(黑描边)保持染色前的样子 —— 染了会发灰,难看;
    * 返回 (RGB 图, 被染色的像素数);alpha 由调用方原样贴回。
    """
    from PIL import Image, ImageChops, ImageOps
    light, dark = colorize_pair(hue)
    L = base_rgb.convert("L")
    tinted = ImageOps.colorize(L, black=dark, white=light).convert("RGB")
    blended = Image.blend(base_rgb, tinted, strength)
    keep = ImageChops.multiply(
        L.point(lambda v: 255 if v >= OUTLINE_LUMA else 0), vis_mask)
    return Image.composite(blended, base_rgb, keep), keep.histogram()[255]


def mean_channel_diff(img_a, img_b):
    """两张同尺寸图的逐通道平均绝对差 -> (R, G, B, A, 四通道平均)"""
    from PIL import ImageChops, ImageStat
    a = img_a.convert("RGBA")
    b = img_b.convert("RGBA")
    if a.size != b.size:
        raise ValueError("尺寸不符: %s vs %s" % (a.size, b.size))
    m = ImageStat.Stat(ImageChops.difference(a, b)).mean
    return m[0], m[1], m[2], m[3], sum(m) / 4.0


def read_slot(rpath, offset, size):
    with open(rpath, "rb") as f:
        f.seek(offset)
        return f.read(size)


def ascii_table(headers, rows):
    """纯 ASCII 表格(中文别放表格里,宽度对不齐)。"""
    cols = len(headers)
    w = [len(str(h)) for h in headers]
    for r in rows:
        for i, c in enumerate(r):
            if i < cols:
                w[i] = max(w[i], len(str(c)))
    line = "+" + "+".join("-" * (x + 2) for x in w) + "+"
    out = [line,
           "|" + "|".join(" %s " % str(h).ljust(w[i]) for i, h in enumerate(headers)) + "|",
           line]
    for r in rows:
        out.append("|" + "|".join(" %s " % str(c).ljust(w[i]) for i, c in enumerate(r[:cols])) + "|")
    out.append(line)
    return "\n".join(out)


def parse_only(s):
    """--only A,B  ->  ['A','B'];没传 -> None"""
    if not s:
        return None
    names = [x.strip() for x in s.split(",") if x.strip()]
    return names or None


# ----------------------------------------------------------------------------
# .atlas 布局文本解析
# ----------------------------------------------------------------------------
def parse_atlas_text(text):
    """解析 .atlas 文本 -> 页列表。

    规则(Unity TexturePacker 格式):
      * 行分隔是 \\r\\r\\n —— 所以先把 \\r 全去掉再按 \\n 切,否则会多出一堆空行。
      * 缩进为 0 且以 .png 结尾的行 = 页图片名,开始一页(一个 .atlas 可能有多页)。
      * 缩进为 0 且 key 属于 ATLAS_HEADER_KEYS 的行 = 头部键值(size/format/filter/repeat)。
      * 其余缩进为 0 的行 = **区域名**(可能含 '/',如 Idle/Tail),直到下一个缩进为 0 的行。
      * 缩进的行是该区域的属性(rotate/xy/size/orig/offset/index)。
    """
    lines = text.replace("\r", "").split("\n")
    pages = []
    cur_page = None
    cur_region = None
    for ln in lines:
        if not ln.strip():
            continue
        if ln[0] in (" ", "\t"):
            if cur_region is not None:
                k, _, v = ln.strip().partition(":")
                if k in cur_region:
                    cur_region[k] = v.strip()
            continue
        if ln.endswith(".png"):
            cur_page = {"page": ln, "size": "", "format": "", "regions": []}
            pages.append(cur_page)
            cur_region = None
            continue
        k, sep, v = ln.partition(":")
        if sep and k.strip() in ATLAS_HEADER_KEYS:
            if cur_page is not None:
                cur_page[k.strip()] = v.strip()
            continue
        # 区域名
        if cur_page is None:
            cur_page = {"page": "(无页名)", "size": "", "format": "", "regions": []}
            pages.append(cur_page)
        cur_region = {"name": ln, "rotate": "", "xy": "", "size": "", "orig": "",
                      "offset": "", "index": ""}
        cur_page["regions"].append(cur_region)
    return pages


def read_local_atlas_texts(atlas_dir):
    """从目录读 *.atlas 文本 -> {文件名: 文本}"""
    out = {}
    if not os.path.isdir(atlas_dir):
        return out
    for fn in sorted(os.listdir(atlas_dir)):
        if not fn.endswith(".atlas"):
            continue
        p = os.path.join(atlas_dir, fn)
        try:
            with open(p, "rb") as f:
                out[fn] = f.read().decode("utf-8", "replace")
        except Exception:
            continue
    return out


def dump_atlas_texts(target, out_dir):
    """把游戏里全部 .atlas 文本写到 out_dir(只写文本,不碰 PNG / resS)"""
    _, texts = load_meta(target)
    os.makedirs(out_dir, exist_ok=True)
    for fn, txt in sorted(texts.items()):
        with open(os.path.join(out_dir, fn), "w", encoding="utf-8", newline="") as fh:
            fh.write(txt)
    return texts


def collect_region_report(texts):
    """{文件名: 文本} -> [(文件名, 页列表, 区域总数)]"""
    rep = []
    for fn in sorted(texts.keys()):
        pages = parse_atlas_text(texts[fn])
        n = sum(len(p["regions"]) for p in pages)
        rep.append((fn, pages, n))
    return rep


def write_layout_md(rep, out_path, source_note):
    """写 布局清单.md:按图集分组,列出全部区域名 + xy/size"""
    total = sum(n for _, _, n in rep)
    L = []
    L.append("# ICEY 图集布局清单")
    L.append("")
    L.append("- 生成时间: %s" % time.strftime("%Y-%m-%d %H:%M:%S"))
    L.append("- 数据来源: %s" % source_note)
    L.append("- 图集数: %d,区域总数: %d" % (len(rep), total))
    L.append("")
    L.append("区域名的读法:一个 .atlas 里**缩进为 0** 的行就是区域名(可以带 `/`,如 `Idle/Tail`),")
    L.append("它下面缩进的 `rotate/xy/size/orig/offset/index` 是它的属性。")
    L.append("`xy` 是该区域在原图集 PNG 里的左上角坐标,`size` 是它的像素尺寸。")
    L.append("")
    for fn, pages, n in rep:
        L.append("## %s" % fn)
        L.append("")
        L.append("- 区域数: %d,页数: %d" % (n, len(pages)))
        for pi, pg in enumerate(pages):
            if len(pages) > 1:
                L.append("")
                L.append("### 第 %d 页: %s" % (pi + 1, pg["page"]))
            else:
                L.append("- 页图片: %s" % pg["page"])
            if pg["size"] or pg["format"]:
                L.append("- 页尺寸/格式: %s / %s" % (pg["size"] or "?", pg["format"] or "?"))
            L.append("")
            L.append("| # | 区域名 | xy | size | rotate | index |")
            L.append("|---|--------|----|------|--------|-------|")
            for i, r in enumerate(pg["regions"], 1):
                L.append("| %d | %s | %s | %s | %s | %s |" % (
                    i, r["name"].replace("|", "\\|"), r["xy"] or "-", r["size"] or "-",
                    r["rotate"] or "-", r["index"] or "-"))
            L.append("")
    with open(out_path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(L) + "\n")
    return total


# ----------------------------------------------------------------------------
# 命令
# ----------------------------------------------------------------------------
def cmd_list(args):
    meta, texts = load_meta(args.target)
    print("目标游戏: %s" % game_dir(args.target))
    print("%-30s %10s %-7s %12s %12s  %s" % ("名称", "尺寸", "格式", "resS偏移", "字节数", "atlas"))
    for n in TARGETS:
        if n in meta:
            m = meta[n]
            print("%-30s %4dx%-5d %-7s %12d %12d  %s" % (
                n, m["width"], m["height"], FMT_NAME.get(m["format"], m["format"]),
                m["offset"], m["size"], "有" if (n + ".atlas") in texts else "-"))
        else:
            print("%-30s 未找到" % n)
    print("共 %d / %d 张" % (len(meta), len(TARGETS)))
    return 0


def cmd_export(args):
    a, r = assets_paths(args.target)
    meta, texts = load_meta(args.target)
    out = args.out or DEFAULT_ATLAS_DIR
    os.makedirs(out, exist_ok=True)
    with open(r, "rb") as f:
        for n in TARGETS:
            if n not in meta:
                print("  跳过(未找到): %s" % n)
                continue
            m = meta[n]
            f.seek(m["offset"])
            raw = f.read(m["size"])
            if len(raw) != m["size"]:
                print("  读取不足,跳过: %s" % n)
                continue
            img = decode(m["format"], raw, m["width"], m["height"])
            fp = os.path.join(out, n + ".png")
            img.save(fp)
            print("  导出 %-28s %4dx%-5d %-7s -> %d KB" % (
                n, m["width"], m["height"], FMT_NAME.get(m["format"]), os.path.getsize(fp) // 1024))
    # 只导主角那 6 张图集的 .atlas(要全部图集的布局用 atlas / regions 命令)
    n_txt = 0
    for fn, txt in sorted(texts.items()):
        if fn.split(".")[0] not in TARGETS:
            continue
        with open(os.path.join(out, fn), "w", encoding="utf-8", newline="") as fh:
            fh.write(txt)
        n_txt += 1
    print("  atlas 布局文本 %d 个" % n_txt)
    print("输出目录: %s" % out)
    return 0


def cmd_palette(args):
    """把图集里的青色整体换成预设色相,直接产出可回写的 PNG。

    两步:
      1) 保护式色相偏移 —— 只动「色相落在青色区间(HUE_LO~HUE_HI)且饱和度 > min-sat」
         的像素,提亮/降饱和也只作用在这些被改色的像素上;肤色、描边、白高光都不动。
      2) (可选)整体染色 --colorize —— 在上面结果之上,把 alpha>8 且非黑描边的像素
         按自身亮度往目标色拉,让灰白护甲/皮肤也带上前一档看不出来的目标色调。
         colorize=0 时整段跳过,输出与旧版逐字节一致。
    """
    from PIL import Image, ImageChops

    preset = args.preset
    hue, pre_l, pre_s, pre_c = PRESETS[preset]
    user_l = args.lighten if args.lighten is not None else 1.0
    user_s = args.saturate if args.saturate is not None else 1.0
    lighten = pre_l * user_l
    saturate = pre_s * user_s
    notes = []
    if lighten > LIGHTEN_MAX:
        notes.append("提亮 %.3f 超过上限 %.2f,已夹到上限(不然会把图洗白)" % (lighten, LIGHTEN_MAX))
        lighten = LIGHTEN_MAX
    if saturate < SATURATE_MIN:
        notes.append("降饱和 %.3f 低于下限 %.2f,已夹到下限" % (saturate, SATURATE_MIN))
        saturate = SATURATE_MIN
    if args.colorize is None:
        colorize = pre_c
        colorize_src = "预设默认"
    else:
        colorize = args.colorize
        colorize_src = "命令行"
        if colorize < 0.0 or colorize > 1.0:
            notes.append("染色强度 %.3f 超出 0.0~1.0,已夹到范围内" % colorize)
            colorize = min(1.0, max(0.0, colorize))

    src = args.out or DEFAULT_ATLAS_DIR          # 输入:原始导出目录
    dst = args.png_dir or os.path.join(WORK, "player_atlas_" + preset)   # 输出目录
    if not os.path.isdir(src):
        print("!! 输入目录不存在: %s(先跑 export)" % src)
        return 2
    os.makedirs(dst, exist_ok=True)
    shift = (hue - 128) % 256                    # 青色在 PIL 的 H 通道里约等于 128

    n = 0
    px = 0
    px_tint = 0
    for fn in sorted(os.listdir(src)):
        if not fn.endswith(".png"):
            continue
        img = Image.open(os.path.join(src, fn)).convert("RGBA")
        alpha = img.getchannel("A")
        H, S, V = img.convert("RGB").convert("HSV").split()
        # 可见性掩膜:基础图集只动 alpha>8 的像素;特效图(Flash/Illum)**整张都算可见**。
        # 为什么区别对待:
        #   * 基础图集的透明区 RGB 是 (0,4,8) 这种近似黑 —— 改了看不见,纯粹是脏数据;
        #   * PlayerFlash 是反过来的:它整张图(连 alpha=0 的 83% 区域)RGB 都是青色,
        #     而那层光效很可能走加色混合(Blend One One,RGB 不乘 alpha),
        #     只染"看得见的"像素等于没染 —— 实机现象就是"光效不跟着换色"。
        # 注意:无论哪条路,最终 putalpha 都用**原 alpha**,透明区形状永不变。
        effect_png = ("Flash" in fn) or ("Illum" in fn)
        vis = Image.new("L", img.size, 255) if effect_png else \
            alpha.point(lambda v: 255 if v > COLORIZE_MIN_ALPHA else 0)
        mask = ImageChops.multiply(
            ImageChops.multiply(
                H.point(lambda v: 255 if HUE_LO <= v <= HUE_HI else 0),
                S.point(lambda v: 255 if v > args.min_sat else 0)),
            vis)
        px += mask.histogram()[255]
        H2 = Image.composite(H.point(lambda v: (v + shift) % 256), H, mask)
        S2 = Image.composite(S.point(lambda v: min(255, int(v * saturate + 0.5))), S, mask)
        V2 = Image.composite(V.point(lambda v: min(255, int(v * lighten + 0.5))), V, mask)
        out = Image.merge("HSV", (H2, S2, V2)).convert("RGB")
        if colorize > 0.0:
            # colorize == 0 时这一整段跳过 —— 旧行为,输出逐字节不变
            out, tinted = colorize_image(out, vis, hue, colorize)
            px_tint += tinted
        out.putalpha(alpha)
        out.save(os.path.join(dst, fn))
        n += 1

    print(ascii_table(
        ["preset", "hue", "lighten", "saturate", "min_sat", "colorize", "images",
         "px_recolored", "px_tinted"],
        [[preset, hue, "%.3f" % lighten, "%.3f" % saturate, args.min_sat,
          "%.3f" % colorize, n, px, px_tint]]))
    print("说明:")
    print("  1) preset=%s 的目标色相 = %d;相对青色(约 128)的偏移 = %+d" % (preset, hue, shift))
    print("     实际倍数 = 预设默认 x 命令行值;%s 预设默认 lighten=%.2f / saturate=%.2f / colorize=%.2f"
          % (preset, pre_l, pre_s, pre_c))
    print("  2) 只改色相在 %d~%d 且饱和度 > %d 的像素 —— 肤色/描边/白高光不动;" % (
        HUE_LO, HUE_HI, args.min_sat))
    print("     提亮(V x %.3f)和降饱和(S x %.3f)也只作用在这些像素上。" % (lighten, saturate))
    if colorize > 0.0:
        light, dark = colorize_pair(hue)
        print("  3) 整体染色 colorize=%.3f(%s):在上一档结果之上,对 alpha > %d 且亮度 >= %d 的像素"
              % (colorize, colorize_src, COLORIZE_MIN_ALPHA, OUTLINE_LUMA))
        print("     按自身亮度往目标色拉;目标色亮版 RGB%s / 暗版 RGB%s(H=%.2f S=%.2f)。"
              % (light, dark, COLORIZE_SAT, COLORIZE_VAL))
        print("     亮度 < %d 的黑描边保持原样,alpha 通道原样贴回(透明区形状不变)。" % OUTLINE_LUMA)
    else:
        print("  3) 整体染色 colorize=0.000(%s):关闭,只做第 2 条的保护式色相偏移(旧行为)。"
              % colorize_src)
    for t in notes:
        print("  ! %s" % t)
    print("  4) 输入: %s" % src)
    print("     输出: %s" % dst)
    print("下一步: python atlas_tool.py apply --png-dir \"%s\"" % dst)
    return 0


def cmd_atlas(args):
    """只导出 .atlas 布局文本(不导出 PNG),顺带写 布局清单.md"""
    out = args.out or DEFAULT_ATLAS_DIR
    texts = dump_atlas_texts(args.target, out)
    rep = collect_region_report(texts)
    md = os.path.join(out, "布局清单.md")
    total = write_layout_md(rep, md, "%s(ICEY_Data/resources.assets 的 .atlas TextAsset)" %
                            game_dir(args.target))
    print("已导出 %d 个 .atlas 布局文本 -> %s" % (len(texts), out))
    for fn, pages, n in rep:
        print("  %-28s 区域 %4d  页 %d" % (fn, n, len(pages)))
    print("共 %d 个图集 / %d 个区域" % (len(rep), total))
    print("布局清单: %s" % md)
    return 0


def cmd_regions(args):
    """解析 .atlas 文本,列出每个图集的全部区域名,并写 布局清单.md"""
    atlas_dir = args.atlas_dir or DEFAULT_ATLAS_DIR
    texts = read_local_atlas_texts(atlas_dir)
    auto = False
    if not texts:
        print("本地没有 .atlas 文本(%s),改从游戏读取(只读,不碰 resS)" % atlas_dir)
        texts = dump_atlas_texts(args.target, atlas_dir)
        auto = True
    if not texts:
        print("!! 一个 .atlas 都没找到")
        return 2

    rep = collect_region_report(texts)
    for fn, pages, n in rep:
        print("%s: %d 个区域" % (fn, n))
        if not args.quiet:
            for pi, pg in enumerate(pages):
                if len(pages) > 1:
                    print("  [第 %d 页 %s]" % (pi + 1, pg["page"]))
                for r in pg["regions"]:
                    print("    %-40s xy=%-14s size=%-12s rotate=%s" % (
                        r["name"], r["xy"] or "-", r["size"] or "-", r["rotate"] or "-"))

    out = args.out or DEFAULT_ATLAS_DIR
    os.makedirs(out, exist_ok=True)
    md = os.path.join(out, "布局清单.md")
    src_note = ("%s 目录里的 .atlas 文本" % atlas_dir) if not auto else \
               ("%s(从游戏只读提取)" % game_dir(args.target))
    total = write_layout_md(rep, md, src_note)
    print("共 %d 个图集 / %d 个区域" % (len(rep), total))
    print("布局清单: %s" % md)
    return 0


def cmd_apply(args):
    if args.target == "original" and not args.i_know:
        print("!! 拒绝:改原版必须显式加 --i-know-what-im-doing")
        return 2
    if not os.path.isdir(args.png_dir):
        print("!! PNG 目录不存在: %s" % args.png_dir)
        return 2

    only = parse_only(args.only)
    if only is not None:
        unknown = [x for x in only if x not in TARGETS]
        if unknown:
            print("!! --only 里有不认识的贴图名: %s" % ", ".join(unknown))
            print("   可用名字: %s" % ", ".join(TARGETS))
            return 2

    from PIL import Image
    meta, _ = load_meta(args.target)
    os.makedirs(BACKUP, exist_ok=True)
    manifest = {}
    if os.path.exists(MANIFEST):
        with open(MANIFEST, "r", encoding="utf-8") as f:
            manifest = json.load(f)

    a, r = assets_paths(args.target)
    planned = []
    skipped = []
    for n in TARGETS:
        if only is not None and n not in only:
            continue
        p = os.path.join(args.png_dir, n + ".png")
        if n not in meta:
            skipped.append((n, "游戏里没这张贴图"))
            continue
        if not os.path.exists(p):
            skipped.append((n, "目录里没有 %s.png" % n))
            continue
        m = meta[n]
        try:
            data = encode(m["format"], Image.open(p), m["width"], m["height"])
        except Exception as e:
            print("!! %s 编码失败: %s —— 拒绝写入" % (n, e))
            return 3
        base = base_size(m["format"], m["width"], m["height"])
        if len(data) != base:
            print("!! %s 编码后 %d 字节,基础层应为 %d 字节 —— 拒绝写入" % (n, len(data), base))
            return 3
        if base > m["size"]:
            print("!! %s 基础层 %d 字节 > 槽位 %d 字节 —— 拒绝写入" % (n, base, m["size"]))
            return 3
        planned.append({"name": n, "meta": m, "data": data, "base": base,
                        "mips": m["size"] - base, "old": None})
    for n, why in skipped:
        print("  跳过: %-28s %s" % (n, why))
    if not planned:
        print("!! 没有找到任何可回写的 PNG")
        return 2

    print("计划写入 %d 张图 -> %s%s" % (
        len(planned), r, "(只写这些)" if only else "(目录里所有能对上的)"))
    for e in planned:
        n, m = e["name"], e["meta"]
        extra = ("(该槽位带 mip 链 %d 字节,保留原样)" % e["mips"]) if e["mips"] else ""
        print("   %-28s %-7s 基础层 %9d 字节 @ %d %s"
              % (n, FMT_NAME.get(m["format"]), len(e["data"]), m["offset"], extra))
    if args.dry_run:
        print("(--dry-run,没有真的写)")
        return 0

    # 备份原始字节(只备份被覆盖的区间),再写基础层。
    # 关键:同一槽位**绝不覆盖已有备份** —— 否则连续 apply 两次(中间不 restore),
    # 第二遍就把"上一次改过的内容"当原始字节存进去了,备份就废了,还原不到原版。
    # 只有槽位变了(offset/size/resS 不同,比如游戏更新后)才重写备份。
    kept = 0
    with open(r, "r+b") as f:
        for e in planned:
            n, m = e["name"], e["meta"]
            f.seek(m["offset"])
            old = f.read(m["size"])
            e["old"] = old
            bp = os.path.join(BACKUP, n + ".bin")
            prev = manifest.get(n) or {}
            same_slot = (prev.get("offset") == m["offset"] and prev.get("size") == m["size"]
                         and prev.get("resS") == m["resS"] and os.path.exists(bp))
            if same_slot:
                kept += 1
            else:
                with open(bp, "wb") as b:
                    b.write(old)
            manifest[n] = {"offset": m["offset"], "size": m["size"], "resS": m["resS"],
                           "format": m["format"], "width": m["width"], "height": m["height"]}
        with open(MANIFEST, "w", encoding="utf-8") as fh:
            json.dump(manifest, fh, ensure_ascii=False, indent=1)
        for e in planned:
            f.seek(e["meta"]["offset"])
            f.write(e["data"])          # 只覆盖基础层;槽位更大时尾部(mip 链)保持原样
            f.flush()
    print("[OK] 已写入 %d 张。原字节备份在 %s" % (len(planned), BACKUP))
    if kept:
        print("     (%d 张的备份已存在,保留最早那份原始字节 —— 连续 apply 不会把备份覆盖成改过的内容)" % kept)

    # ---- 自动回读校验:从 resS 重新解码刚写的基础层,和输入 PNG 比 ----
    print("")
    print("回读校验(重新从 resS 解码基础层 vs 输入 PNG;mip 尾部另比):")
    rows = []
    warns = []
    with open(r, "rb") as f:
        for e in planned:
            n, m = e["name"], e["meta"]
            f.seek(m["offset"])
            cur_raw = f.read(e["base"])          # 只读基础层
            cur = decode(m["format"], cur_raw, m["width"], m["height"])
            src = Image.open(os.path.join(args.png_dir, n + ".png"))
            dr, dg, db, da, dmean = mean_channel_diff(src, cur)
            if e["mips"]:
                f.seek(m["offset"] + e["base"])
                tail = f.read(e["mips"])
                mip_ok = (tail == e["old"][e["base"]:])
            else:
                mip_ok = True
            verdict = "WARN" if dmean > DIFF_WARN else "OK"
            if verdict == "WARN":
                warns.append("%s 平均差 %.3f > %.1f" % (n, dmean, DIFF_WARN))
            if not mip_ok:
                warns.append("%s 的 mip 尾部被改动了(不该发生!)" % n)
            rows.append([n, FMT_NAME.get(m["format"]), "%.4f" % dr, "%.4f" % dg,
                         "%.4f" % db, "%.4f" % da, "%.4f" % dmean,
                         ("yes" if e["mips"] else "-"), ("yes" if mip_ok else "NO") if e["mips"] else "-", verdict])
    print(ascii_table(["name", "fmt", "dR", "dG", "dB", "dA", "mean", "hasmip", "mip_ok", "verdict"], rows))
    print("说明: 平均通道差单位是 0~255。有损格式(DXT1/DXT5)约 0.5/255 的量化误差属正常;")
    print("      超过 %.1f/255 会打 WARN。无损格式(ARGB32/RGBA32/RGB24)应当是 0。" % DIFF_WARN)
    if warns:
        for w in warns:
            print("  !! WARN: %s" % w)
    else:
        print("  全部通过,没有超过 %.1f/255 的图。" % DIFF_WARN)
    print("     还原: python atlas_tool.py restore --target %s" % args.target)
    print("     启动游戏看效果(工具不会自己启动游戏)")
    return 0


def cmd_verify(args):
    """只读:resS 当前基础层解码 vs 目录里的 PNG,并给出是否与备份字节一致(=未被改动)"""
    meta, _ = load_meta(args.target)
    a, r = assets_paths(args.target)
    png_dir = args.png_dir
    if png_dir and not os.path.isdir(png_dir):
        print("!! PNG 目录不存在: %s" % png_dir)
        return 2
    rows = []
    warns = []
    n_changed = 0
    n_nobackup = 0
    for n in TARGETS:
        if n not in meta:
            rows.append([n, "-", "-", "-", "-"])
            continue
        m = meta[n]
        base = base_size(m["format"], m["width"], m["height"])
        raw = read_slot(r, m["offset"], m["size"])
        if len(raw) != m["size"]:
            print("!! 读取不足: %s" % n)
            return 3
        cur = decode(m["format"], raw[:base], m["width"], m["height"])

        diff = "-"
        if png_dir:
            p = os.path.join(png_dir, n + ".png")
            if os.path.exists(p):
                from PIL import Image
                dr, dg, db, da, dmean = mean_channel_diff(Image.open(p), cur)
                diff = "%.4f" % dmean
                if dmean > DIFF_WARN:
                    warns.append("%s 这份 PNG 还没写进 resS(平均差 %.3f > %.1f)"
                                 % (n, dmean, DIFF_WARN))
            else:
                diff = "(无PNG)"

        bp = os.path.join(BACKUP, n + ".bin")
        if os.path.exists(bp):
            with open(bp, "rb") as b:
                bk = b.read()
            if len(bk) == m["size"]:
                same = (bk == raw)
                unchanged = "yes" if same else "no"
                if not same:
                    n_changed += 1
            else:
                unchanged = "??大小不符"
        else:
            unchanged = "-"
            n_nobackup += 1
        rows.append([n, FMT_NAME.get(m["format"]), diff,
                     "yes" if os.path.exists(bp) else "no", unchanged])

    print("只读校验(目标: %s)" % game_dir(args.target))
    print("resS: %s" % r)
    if png_dir:
        print("PNG 目录: %s" % png_dir)
    print(ascii_table(["name", "fmt", "diff_vs_png", "has_backup", "unchanged"], rows))
    print("说明:")
    print("  diff_vs_png = resS 里**当前**基础层解码后与目录里同名 PNG 的平均通道差(0~255)。")
    print("  unchanged   = 当前槽位字节与 _work/atlas_backup/<名字>.bin **完全一致**,即这张没被改过。")
    print("                no = 被改过(或备份不是这个槽位的);- = 没有备份可比。")
    print("  无损格式(ARGB32/RGBA32/RGB24)改了 PNG 后 diff 应当≈0;DXT 约有 0.5/255 量化误差。")
    if n_nobackup == len(TARGETS):
        print("  一个备份都没有,无法判定是否被改过。")
    elif n_changed:
        print("  当前有 %d 张与备份不一致(=被改过)。" % n_changed)
    else:
        print("  全部与备份字节一致 = 一张都没被改过。" if not n_nobackup else
              "  有备份的图全部与备份字节一致。")
    if n_nobackup:
        print("  %d 张没有备份文件,unchanged 记 '-'。" % n_nobackup)
    for w in warns:
        print("  !! WARN: %s" % w)
    return 0


def cmd_restore(args):
    if not os.path.exists(MANIFEST):
        print("!! 没有备份清单: %s" % MANIFEST)
        return 2
    with open(MANIFEST, "r", encoding="utf-8") as f:
        manifest = json.load(f)
    _, r = assets_paths(args.target)
    n = 0
    with open(r, "r+b") as fh:
        for name, m in manifest.items():
            p = os.path.join(BACKUP, name + ".bin")
            if not os.path.exists(p):
                continue
            with open(p, "rb") as b:
                data = b.read()
            if len(data) != m["size"]:
                print("!! 备份大小不符,跳过 %s" % name)
                continue
            fh.seek(m["offset"])
            fh.write(data)
            n += 1
    print("[OK] 已还原 %d 张图到原始状态" % n)
    print("     想确认字节一致: python atlas_tool.py verify --target %s" % args.target)
    return 0


# ----------------------------------------------------------------------------
# mcskin:把一张 Minecraft 64x64 皮肤按「区域语义」贴到主角图集上
# ----------------------------------------------------------------------------
MC_SKIN_DEFAULT = os.path.join(WORK, "mcskin", "German.png")
MC_OUT_DIR = os.path.join(WORK, "player_atlas_mc")
MC_PREVIEW_PATH = os.path.join(WORK, "_mcskin_皮肤预览.png")
MC_COMPARE_PATH = os.path.join(WORK, "_mcskin_对比.png")
MC_README_NAME = "README.txt"
MC_INSTALL_REL = os.path.join("BepInEx", "config", "player_skin", "皮肤-贴图版")
MC_PAYLOAD_DIR = os.path.join(COPY, "_dist", "ICEYCheatPanel")
MC_SKIN_W = MC_SKIN_H = 64
MC_ALPHA_MIN = 8               # 只写 alpha > 8 的像素;<= 8 的像素一个字节都不许动
MC_PIXEL_DEFAULT = 4           # 方块边长;1 = 逐像素采样不量化
MC_HAIR_MIN_OPAQUE = 0.05      # 头饰层不透明像素少于 5% 就退回头部层
MC_TINT_DEFAULT = 0.20         # --tint-illum 的默认幅度
MC_TINT_MAX = 0.25             # 幅度硬上限(主人定的)
MC_PREVIEW_ZOOM = 6            # 皮肤预览放大倍数(邻近)
MC_COMPARE_TILE = 512          # 对比图每格边长
MC_COMPARE_MAX_BYTES = 8 * 1024 * 1024
MC_FONTS_CJK = (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\simhei.ttf",
                r"C:\Windows\Fonts\Deng.ttf")

# 区域名关键词 -> 类别。**顺序就是优先级**(先命中的赢),一律转小写做子串匹配。
MC_SKIN_CATS = (
    ("head", ("head", "face", "eye", "head3")),
    ("hair", ("hair",)),
    ("body", ("body", "upbody", "cloth", "cape", "belt", "yaobao", "waist")),
    ("arm", ("arm", "hand", "shoulder", "qiang", "qiangshua", "weapon", "sword", "blade")),
    ("leg", ("leg", "shank", "foot", "shoe", "legs", "thigh", "knee")),
)
MC_CAT_FALLBACK = "body"       # 认不出的区域按躯干处理
MC_CAT_ORDER = ("head", "hair", "body", "arm", "leg")
# 每类取皮肤上的哪个矩形(标准 64x64 布局)
MC_CAT_RECT = {
    "head": (0, 0, 32, 16),      # 头(含脸/眼)
    "hair": (32, 0, 64, 16),     # 头饰层(头发/帽子),不透明太少就退回 head
    "body": (16, 16, 40, 32),    # 躯干 + 衣服
    "arm": (40, 16, 56, 32),     # 右臂(手/武器也归这儿)
    "leg": (0, 16, 16, 32),      # 右腿(脚/鞋也归这儿)
}
MC_EFFECT_TARGETS = tuple(n for n in TARGETS if ("Illum" in n or "Flash" in n))
MC_MAIN_TARGETS = tuple(n for n in TARGETS if n not in MC_EFFECT_TARGETS)


def mc_pil():
    """返回 (Image, NEAREST, LANCZOS, TRANSPOSE) —— 兼容新旧 Pillow 的枚举名。"""
    from PIL import Image
    rs = getattr(Image, "Resampling", Image)
    tr = getattr(Image, "Transpose", Image)
    return Image, rs.NEAREST, rs.LANCZOS, tr.TRANSPOSE


def mc_font(size):
    """中文字体 -> (font, 能不能写中文);没字体就退回默认位图字体。"""
    from PIL import ImageFont
    for p in MC_FONTS_CJK:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size), True
            except Exception:
                pass
    return ImageFont.load_default(), False


def mcskin_category(region_name):
    """区域名 -> 类别键(认不出归 MC_CAT_FALLBACK)。"""
    low = (region_name or "").lower()
    for key, kws in MC_SKIN_CATS:
        for kw in kws:
            if kw in low:
                return key
    return MC_CAT_FALLBACK


def mc_box_opaque_frac(skin, box):
    """矩形里 alpha > MC_ALPHA_MIN 的像素占比(判断头饰层是不是空的)。"""
    a = skin.crop(box).getchannel("A").histogram()
    tot = (box[2] - box[0]) * (box[3] - box[1])
    return (sum(a[MC_ALPHA_MIN + 1:]) / float(tot)) if tot else 0.0


def mc_dominant_color(skin):
    """皮肤主色调 = 不透明像素的算术平均(刻意不用 numpy)。"""
    px = skin.load()
    w, h = skin.size
    sr = sg = sb = n = 0
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a > MC_ALPHA_MIN:
                sr += r
                sg += g
                sb += b
                n += 1
    if not n:
        return (255, 255, 255)
    return (int(sr / n), int(sg / n), int(sb / n))


def mc_skin_rects(skin):
    """类别 -> (皮肤矩形, 说明)。hair 走「头饰层太透明就退回头部层」的兜底。"""
    out = {}
    for cat in MC_CAT_ORDER:
        box = MC_CAT_RECT[cat]
        note = ""
        if cat == "hair":
            frac = mc_box_opaque_frac(skin, box)
            if frac < MC_HAIR_MIN_OPAQUE:
                box = MC_CAT_RECT["head"]
                note = "头饰层不透明只有 %.1f%%(<%.0f%%),退回头部层" % (
                    frac * 100.0, MC_HAIR_MIN_OPAQUE * 100.0)
            else:
                note = "头饰层不透明 %.1f%%" % (frac * 100.0)
        out[cat] = (box, note)
    return out


def mc_sample_patch(skin, sbox, size, pixel, rotate):
    """把皮肤矩形 sbox 按**归一化坐标**均匀铺满 size -> RGB 图。

    * 邻近采样(PIL NEAREST),所以是硬边像素风,不会糊;
    * pixel = N > 1:先在 (w//N, h//N) 上采一次再邻近放大 —— 每个 N×N 方块同一个颜色,
      做出方正像素感;N = 1 就是逐像素采样、不量化;
    * rotate = True(图集里该区域是转着存的):把归一化坐标 u/v **对调**(= 转置),
      转置后的尺寸正好是图集里的 footprint(宽高互换)。
    """
    _, NEAREST, _, TRANSPOSE = mc_pil()
    w, h = int(size[0]), int(size[1])
    w = max(1, w)
    h = max(1, h)
    src = skin.crop(tuple(sbox))
    n = max(1, int(pixel))
    if n > 1 and (w > n or h > n):
        bw = max(1, w // n)
        bh = max(1, h // n)
        src = src.resize((bw, bh), NEAREST).resize((w, h), NEAREST)
    else:
        src = src.resize((w, h), NEAREST)
    if rotate:
        src = src.transpose(TRANSPOSE)      # u/v 对调
    return src.convert("RGB")


def mc_region_footprint(region):
    """区域在 PNG 里**实际占的框** + 声明尺寸 + 是否旋转。

    rotate: true 的区域是「转着存」的:footprint 宽高互换。
    例 Idle/Tail xy=1459,1812 size=234,470 -> 实际占 470x234
    (不换的话 1812+470=2282 直接出图,已经实测确认过)。
    """
    if not region.get("xy") or not region.get("size"):
        return None
    try:
        x, y = [int(v.strip()) for v in region["xy"].split(",")]
        w, h = [int(v.strip()) for v in region["size"].split(",")]
    except Exception:
        return None
    rot = (region.get("rotate") or "").strip().lower() == "true"
    box = (x, y, x + h, y + w) if rot else (x, y, x + w, y + h)
    return {"box": box, "size": (w, h), "rot": rot, "xy": (x, y)}


def mc_alpha_evidence(orig, new):
    """逐像素证据:(alpha 不同像素数, alpha 最大差, 透明区 RGB 被动过的像素数, RGB 变了多少像素)

    透明区的定义是原图 alpha <= MC_ALPHA_MIN —— 那些像素一个字节都不该动。
    全程用 PIL 的 C 实现(ImageChops),不写 Python 循环、不引 numpy。
    """
    from PIL import ImageChops
    a1 = orig.getchannel("A")
    a2 = new.getchannel("A")
    d = ImageChops.difference(a1, a2)
    h = d.histogram()
    n_alpha = sum(h[1:])
    max_alpha = max([i for i, v in enumerate(h) if v] or [0])
    nz = lambda v: 255 if v else 0
    r, g, b = ImageChops.difference(orig.convert("RGB"), new.convert("RGB")).split()
    changed = ImageChops.lighter(ImageChops.lighter(r.point(nz), g.point(nz)), b.point(nz))
    n_rgb = changed.histogram()[255]
    clear = a1.point(lambda v: 255 if v <= MC_ALPHA_MIN else 0)
    n_clear = ImageChops.multiply(changed, clear).histogram()[255]
    return n_alpha, max_alpha, n_clear, n_rgb


def mc_remap_atlas(name, page, png_path, skin, rects, pixel):
    """把一张主图集按区域语义重画 -> (新图, 原图, 统计 dict, 备注)

    统计 = {"regions": 处理了几个区域, "rot": 其中旋转区域数, "skip": 属性不全的区域,
            "outside": 框越界被跳过, "vis": 本页可见像素数, "covered": 其中被区域框盖住的,
            "cats": {类别: [区域数, 写入像素数]}}
    """
    from PIL import ImageChops, ImageDraw
    Image, NEAREST, _, _ = mc_pil()
    orig = Image.open(png_path).convert("RGBA")
    psize = page.get("size") or ""
    if psize:
        try:
            pw, ph = [int(v.strip()) for v in psize.split(",")]
            if (pw, ph) != orig.size:
                return None, orig, None, "布局写的是 %dx%d,PNG 实际是 %dx%d" % (
                    pw, ph, orig.size[0], orig.size[1])
        except Exception:
            pass
    alpha = orig.getchannel("A")
    vis = alpha.point(lambda v: 255 if v > MC_ALPHA_MIN else 0)
    out = orig.copy()
    W, H = out.size
    cover = Image.new("L", (W, H), 0)           # 区域框并集,用来看「哪些像素没被命名」
    dc = ImageDraw.Draw(cover)
    st = {"regions": 0, "rot": 0, "skip": 0, "outside": 0, "vis": 0, "covered": 0, "cats": {}}
    for r in page["regions"]:
        fp = mc_region_footprint(r)
        if fp is None:
            st["skip"] += 1
            continue
        x1, y1, x2, y2 = fp["box"]
        if x2 <= x1 or y2 <= y1 or x1 < 0 or y1 < 0 or x2 > W or y2 > H:
            st["outside"] += 1
            continue
        dc.rectangle([x1, y1, x2 - 1, y2 - 1], fill=255)
        cat = mcskin_category(r["name"])
        sbox = rects[cat][0]
        patch = mc_sample_patch(skin, sbox, fp["size"], pixel, fp["rot"])
        patch = patch.resize((x2 - x1, y2 - y1), NEAREST)   # 恒等(保险起见)
        m = vis.crop(fp["box"])
        out.paste(patch, (x1, y1), m)                       # 只在 alpha>8 处落笔
        px = m.histogram()[255]
        c = st["cats"].setdefault(cat, [0, 0])
        c[0] += 1
        c[1] += px
        st["regions"] += 1
        if fp["rot"]:
            st["rot"] += 1
    st["vis"] = vis.histogram()[255]
    st["covered"] = ImageChops.multiply(vis, cover).histogram()[255]
    out.putalpha(alpha)                                     # 原 alpha 原样贴回
    return out, orig, st, ""


def mc_save_effect(src_png, dst_png, tint_rgb, amount):
    """发光/闪光图:_Illum 4 张 + PlayerFlash。amount=0 就原字节拷贝,否则整体往主色拉。"""
    if not amount:
        with open(src_png, "rb") as f:
            data = f.read()
        with open(dst_png, "wb") as f:
            f.write(data)
        return 0.0
    from PIL import Image
    amt = min(float(amount), MC_TINT_MAX)
    img = Image.open(src_png).convert("RGBA")
    alpha = img.getchannel("A")
    flat = Image.new("RGB", img.size, tuple(tint_rgb))
    img = Image.blend(img.convert("RGB"), flat, amt).convert("RGBA")
    img.putalpha(alpha)                                     # alpha 依旧原样
    img.save(dst_png)
    return amt


def mc_tile(path, size):
    """读一张图集,垫深色底(透明区看得见),缩到 size x size。"""
    Image, _, LANCZOS, _ = mc_pil()
    im = Image.open(path).convert("RGBA")
    bg = Image.new("RGBA", im.size, (34, 34, 40, 255))
    bg.alpha_composite(im)
    return bg.convert("RGB").resize((size, size), LANCZOS)


def mc_make_preview(skin, path, zoom=None):
    """64x64 皮肤放大 N 倍(邻近)存一份,方便对照。"""
    Image, NEAREST, _, _ = mc_pil()
    z = int(zoom or MC_PREVIEW_ZOOM)
    bg = Image.new("RGBA", skin.size, (34, 34, 40, 255))
    bg.alpha_composite(skin.convert("RGBA"))
    bg.convert("RGB").resize((skin.size[0] * z, skin.size[1] * z), NEAREST).save(path)
    return path


def mc_make_compare(pairs, skin, out_path):
    """原版 / MC 版并排 + 皮肤预览 -> 一张对比图(压到 8MB 以内)。"""
    from PIL import ImageDraw
    Image, _, _, _ = mc_pil()
    tile = MC_COMPARE_TILE
    while True:
        pad, gap, lh = 16, 14, 32
        z = MC_PREVIEW_ZOOM
        skin_px = MC_SKIN_W * z
        row_h = lh + tile
        W = pad * 2 + tile * 2 + gap + 26 + skin_px
        H = pad * 2 + row_h * len(pairs) + gap * (len(pairs) - 1)
        canvas = Image.new("RGB", (W, H), (22, 22, 26))
        d = ImageDraw.Draw(canvas)
        f1, cn = mc_font(21)
        f2, _ = mc_font(16)
        L = (lambda zh, en: zh) if cn else (lambda zh, en: en)
        for i, (title, orig_p, mc_p) in enumerate(pairs):
            y = pad + i * (row_h + gap)
            d.text((pad, y + 4), "%s   %s" % (title, L("左: 原版     右: MC 皮肤版",
                                                       "left: original   right: mc skin")),
                   font=f1, fill=(230, 230, 238))
            ty = y + lh
            canvas.paste(mc_tile(orig_p, tile), (pad, ty))
            canvas.paste(mc_tile(mc_p, tile), (pad + tile + gap, ty))
        xr = pad + tile * 2 + gap + 26
        pv = Image.open(mc_make_preview(skin, MC_PREVIEW_PATH)).convert("RGB")
        canvas.paste(pv, (xr, pad + lh + 8))
        d.text((xr, pad + 4), L("MC 皮肤 64x64 放大 %d 倍" % z, "mc skin x%d" % z),
               font=f1, fill=(230, 230, 238))
        lines = [L("深色底 = 透明区", "dark = transparent"),
                 L("区域语义映射:", "region semantics:"),
                 L(" head -> (0,0,32,16)", " head -> (0,0,32,16)"),
                 L(" hair -> (32,0,64,16)", " hair -> (32,0,64,16)"),
                 L(" body -> (16,16,40,32)", " body -> (16,16,40,32)"),
                 L(" arm  -> (40,16,56,32)", " arm  -> (40,16,56,32)"),
                 L(" leg  -> (0,16,16,32)", " leg  -> (0,16,16,32)"),
                 L("旋转区域按转置近似,", "rotated regions: transposed"),
                 L("可能有 90 度误差", "(approx, up to 90 deg off)"),
                 L("", ""),
                 L("青色残留 = 本页没被", "leftover cyan = page px"),
                 L(".atlas 命名的像素,", "not named by the"),
                 L("没有任何 atlas 引用", ".atlas (never drawn,"),
                 L("(不进画面,按规格", "kept as-is)"),
                 L("保持原样)", "")]
        ly = pad + lh + 8 + pv.size[1] + 14
        for t in lines:
            d.text((xr, ly), t, font=f2, fill=(196, 196, 205))
            ly += 22
        canvas.save(out_path, optimize=True)
        if os.path.getsize(out_path) <= MC_COMPARE_MAX_BYTES or tile <= 256:
            return out_path, tile
        tile = tile * 3 // 4


def mc_install(out_dir, names):
    """把 11 张拷进 BepInEx/config/player_skin/mc/(副本,以及 _dist 载荷如果存在)。"""
    import shutil
    dsts = [os.path.join(COPY, MC_INSTALL_REL)]
    if os.path.isdir(MC_PAYLOAD_DIR):
        dsts.append(os.path.join(MC_PAYLOAD_DIR, MC_INSTALL_REL))
    done = []
    for dst in dsts:
        os.makedirs(dst, exist_ok=True)
        miss = []
        for n in names:
            src = os.path.join(out_dir, n + ".png")
            if not os.path.exists(src):
                miss.append(n)
                continue
            shutil.copyfile(src, os.path.join(dst, n + ".png"))
        done.append((dst, miss))
    return done


def cmd_mcskin(args):
    """把一张 Minecraft 64x64 皮肤按区域语义贴到 6 张主图集上 -> _work/player_atlas_mc/。

    复用现成的 .atlas 解析(parse_atlas_text / read_local_atlas_texts /
    collect_region_report),不重写解析器。

    流程:
      1) 读皮肤(必须 64x64),按区域名分类(头/头发/躯干/手臂/腿),每类取固定皮肤矩形;
      2) 每个区域按归一化坐标把皮肤矩形均匀铺满(邻近采样 + N 像素方块化),
         旋转区域把 u/v 对调(转置)填 footprint;
      3) **只写在原图 alpha > 8 的像素上**,透明区一个字节都不动,最后 putalpha(原 alpha);
      4) _Illum 4 张 + PlayerFlash 默认原样拷贝,可选 --tint-illum 整体拉主色(<= 0.25);
      5) 自证 alpha 未变(逐像素),再出 6 倍皮肤预览 + 对比图;
      6) --install 拷进 _moddev/BepInEx/config/player_skin/mc/(落点同 make-skin-presets.ps1)。
    """
    Image, _, _, _ = mc_pil()

    skin_path = args.skin or MC_SKIN_DEFAULT
    if not os.path.exists(skin_path):
        print("!! 找不到皮肤: %s" % skin_path)
        return 2
    skin = Image.open(skin_path).convert("RGBA")
    if skin.size != (MC_SKIN_W, MC_SKIN_H):
        print("!! 皮肤必须是 %dx%d(标准布局),这张是 %dx%d —— 矩形坐标是写死的,拒绝跑"
              % (MC_SKIN_W, MC_SKIN_H, skin.size[0], skin.size[1]))
        return 2

    src = DEFAULT_ATLAS_DIR
    out = args.out or MC_OUT_DIR
    if not os.path.isdir(src):
        print("!! 原色图集目录不存在: %s(先跑 export)" % src)
        return 2
    if os.path.abspath(out) == os.path.abspath(src):
        print("!! --out 不能就是原色目录 %s(会覆盖原图)" % src)
        return 2
    pixel = max(1, int(args.pixel or MC_PIXEL_DEFAULT))
    os.makedirs(out, exist_ok=True)

    texts = read_local_atlas_texts(src)
    rep = collect_region_report(texts)
    pages = {}
    for fn, pgs, _n in rep:
        base = fn[:-6] if fn.endswith(".atlas") else fn
        if base in MC_MAIN_TARGETS and pgs:
            pages[base] = pgs[0]

    rects = mc_skin_rects(skin)
    dom = mc_dominant_color(skin)
    tint_amt = args.tint_illum
    if tint_amt is not None:
        if tint_amt <= 0.0:
            tint_amt = None
        elif tint_amt > MC_TINT_MAX:
            print("   ! --tint-illum %.3f 超过上限 %.2f,夹到上限" % (tint_amt, MC_TINT_MAX))
            tint_amt = MC_TINT_MAX

    print("mcskin:把 MC 皮肤贴到主角图集上")
    print("  皮肤    : %s  %dx%d" % (skin_path, skin.size[0], skin.size[1]))
    print("  布局来源: %s(.atlas 文本)" % src)
    print("  输出    : %s" % out)
    print("  方块边长: %d 像素%s" % (pixel, "(逐像素,不量化)" if pixel == 1 else ""))
    print("  皮肤主色: RGB%s" % (dom,))
    print("")
    print("类别 -> 皮肤矩形(标准 64x64 布局):")
    for cat in MC_CAT_ORDER:
        box, note = rects[cat]
        kws = dict(MC_SKIN_CATS).get(cat, ())
        print("  %-5s %-46s -> (%d,%d,%d,%d)%s" % (
            cat, ",".join(kws), box[0], box[1], box[2], box[3],
            ("  [%s]" % note) if note else ""))
    print("  认不出的区域 -> %s 类(%s)" % (MC_CAT_FALLBACK, MC_CAT_RECT[MC_CAT_FALLBACK]))
    print("")

    rows = []
    all_cats = {}
    written = []
    for name in MC_MAIN_TARGETS:
        png = os.path.join(src, name + ".png")
        if name not in pages:
            rows.append([name, "remap", "-", "-", "-", "-", "-", "no atlas", "-", "-", "SKIP"])
            continue
        if not os.path.exists(png):
            print("   !! 缺 PNG: %s" % png)
            return 2
        img, orig, st, why = mc_remap_atlas(name, pages[name], png, skin, rects, pixel)
        if img is None:
            print("   !! %s: %s" % (name, why))
            return 2
        dst = os.path.join(out, name + ".png")
        img.save(dst)
        n_alpha, max_alpha, n_clear, n_rgb = mc_alpha_evidence(orig, img)
        ok = (n_alpha == 0 and n_clear == 0)
        cells = []
        for cat in MC_CAT_ORDER:
            c = st["cats"].get(cat)
            if c:
                cells.append("%d/%d" % (c[0], c[1]))
                t = all_cats.setdefault(cat, [0, 0])
                t[0] += c[0]
                t[1] += c[1]
            else:
                cells.append("-")
        rows.append([name, "remap"] + cells +
                    ["%d" % n_rgb, "%d/%d" % (n_alpha, n_clear),
                     "%.1f%%" % (100.0 * st["covered"] / st["vis"] if st["vis"] else 0.0),
                     ("OK" if ok else "BAD")])
        written.append((name, os.path.getsize(dst), n_rgb))
        if st["skip"] or st["outside"]:
            print("   ! %s: 有 %d 个区域属性不全、%d 个区域框越界,这些没动"
                  % (name, st["skip"], st["outside"]))

    for name in MC_EFFECT_TARGETS:
        png = os.path.join(src, name + ".png")
        if not os.path.exists(png):
            print("   !! 缺 PNG: %s" % png)
            return 2
        dst = os.path.join(out, name + ".png")
        amt = mc_save_effect(png, dst, dom, tint_amt)
        orig = Image.open(png).convert("RGBA")
        new = Image.open(dst).convert("RGBA")
        n_alpha, max_alpha, n_clear, n_rgb = mc_alpha_evidence(orig, new)
        mode = "copy" if not amt else "copy+tint %.2f" % amt
        # 加了 --tint-illum 就是「整体」拉色,连 alpha=0 的像素 RGB 都会变(和基础图集不同),
        # 所以这类只要求 alpha 没变;clear 那一列变成参考值。
        ok = (n_alpha == 0) if amt else (n_alpha == 0 and n_clear == 0)
        rows.append([name, mode, "-", "-", "-", "-", "-",
                     "%d" % n_rgb, "%d/%d" % (n_alpha, n_clear), "-",
                     "OK" if ok else "BAD"])
        written.append((name, os.path.getsize(dst), n_rgb))

    print("每张图集:类别 = 区域数/写入像素数;px_rgb = 真的改了的像素数;")
    print("alpha/clear = alpha 不同的像素数 / 透明区 RGB 被动的像素数(两个都必须是 0);")
    print("cover = 本页可见像素里被 .atlas 区域框盖住的占比 —— 没盖住的部分不是任何区域,")
    print("        按「只改区域」的规格保持原样(每个页只被一个 .atlas 引用,那些像素不进画面)。")
    print(ascii_table(["atlas", "mode"] + list(MC_CAT_ORDER) +
                      ["px_rgb", "alpha/clear", "cover", "check"], rows))
    print("")
    cat_rows = []
    for cat in MC_CAT_ORDER:
        c = all_cats.get(cat, [0, 0])
        cat_rows.append([cat, "%d,%d,%d,%d" % rects[cat][0], c[0], c[1],
                         ("used" if c[0] else "-")])
    print("类别汇总(全 6 张主图集):")
    print(ascii_table(["cat", "skin_rect", "regions", "px_written", "note"], cat_rows))
    print("")
    print("输出文件(%d 张,命名与原图一致):px_rgb = 真的改了的像素数;" % len(written))
    print("(表格里的 px_written 是区域框内 alpha>8 的像素数,区域重叠会重复计,所以会略大于 px_rgb)")
    print(ascii_table(["name", "bytes", "KB", "px_rgb"],
                      [[n, b, "%.1f" % (b / 1024.0), p] for n, b, p in written]))

    # 6 倍皮肤预览
    mc_make_preview(skin, MC_PREVIEW_PATH)
    print("")
    print("皮肤预览: %s (%dx%d, 邻近放大 %d 倍)" % (
        MC_PREVIEW_PATH, MC_SKIN_W * MC_PREVIEW_ZOOM, MC_SKIN_H * MC_PREVIEW_ZOOM,
        MC_PREVIEW_ZOOM))

    # 对比图:NewPlayerNormal + NewPlayerAtk
    pairs = []
    for n in ("NewPlayerNormal", "NewPlayerAtk"):
        o = os.path.join(src, n + ".png")
        m = os.path.join(out, n + ".png")
        if os.path.exists(o) and os.path.exists(m):
            pairs.append((n, o, m))
    if pairs:
        path, tile = mc_make_compare(pairs, skin, MC_COMPARE_PATH)
        print("对比图  : %s (%d 格, %.2f MB)" % (
            path, tile, os.path.getsize(path) / 1048576.0))
    else:
        print("!! 没凑齐对比用的原版 PNG,跳过对比图")

    # README 提示(放在输出目录,不会跟着 11 张 PNG 进游戏)
    notes = [
        "ICEY mcskin 输出说明",
        "",
        "这 11 张 PNG 就是把 %s 这张 MC 64x64 皮肤按区域语义贴到主角图集上的结果。" % os.path.basename(skin_path),
        "回写: python atlas_tool.py apply --png-dir \"%s\"" % out,
        "",
        "区域语义映射(区域名含关键词即命中,不区分大小写,按顺序先命中的赢):",
    ]
    for cat in MC_CAT_ORDER:
        box, note = rects[cat]
        notes.append("  %-5s %-46s -> 皮肤 (%d,%d,%d,%d) %s" % (
            cat, ",".join(dict(MC_SKIN_CATS).get(cat, ())), box[0], box[1], box[2], box[3], note))
    notes += [
        "  其它  认不出的区域                    -> 躯干 (16,16,40,32)",
        "",
        "采样:归一化坐标均匀铺满该区域,邻近采样;--pixel N 做 N 像素方块量化(N=1 不量化)。",
        "alpha:只在原图 alpha > %d 的像素上落笔,透明区一个字节都没动,最后 putalpha(原 alpha)。" % MC_ALPHA_MIN,
        "",
        "**旋转区域(rotate: true)的处理是近似,可能有 90 度误差。**",
        "  图集里旋转区域是转着存的,footprint 宽高互换(例 Idle/Tail size 234x470 实际占 470x234)。",
        "  本工具把归一化坐标 u/v 对调(= 转置)后填进 footprint —— 转置 = 转 90 度 + 镜像一个轴,",
        "  所以如果图集真实的旋转方向和假设不一致,表现是镜像/差 180 度,而不是形状错位或漏填。",
        "  区域一定被铺满(整块矩形全覆盖),只有纹理内部朝向是近似的。",
        "",
        "_Illum 4 张 + PlayerFlash 默认**原样拷贝**(发光/闪光贴图,整套换色会让光效变味);",
        "加 --tint-illum 时这 5 张整体向皮肤主色 RGB%s 拉(幅度 <= %.2f)。" % (str(dom), MC_TINT_MAX),
    ]
    rp = os.path.join(out, MC_README_NAME)
    with open(rp, "w", encoding="utf-8") as fh:
        fh.write("\n".join(notes) + "\n")
    print("说明    : %s" % rp)

    if args.install:
        names = list(TARGETS)
        done = mc_install(out, names)
        print("")
        for dst, miss in done:
            if miss:
                print("   !! 缺 %d 张没拷: %s" % (len(miss), ", ".join(miss)))
            fs = sorted((f, os.path.getsize(os.path.join(dst, f)))
                        for f in os.listdir(dst) if f.endswith(".png"))
            print("已安装 %d 张 -> %s" % (len(fs), dst))
            print(ascii_table(["name", "bytes", "KB"],
                              [[f, s, "%.1f" % (s / 1024.0)] for f, s in fs]))
    else:
        print("")
        print("要进游戏看:python atlas_tool.py apply --png-dir \"%s\"" % out)
        print("            (或者只灌预设目录:加 --install)")
    print("")
    print("提醒:旋转区域按转置近似(可能差 90 度);--pixel 越大量化越粗。")
    return 0


def add_common(sp):
    """--target / --i-know-what-im-doing 同时挂在主 parser 和每个子 parser 上,
    这样写在子命令前或后都能生效(argparse 子 parser 不会覆盖主 parser 已设的值)。"""
    sp.add_argument("--target", default="copy", choices=["copy", "original"],
                    help="改哪个游戏:copy=副本(默认,安全) original=原版(危险)")
    sp.add_argument("--i-know-what-im-doing", dest="i_know", action="store_true",
                    help="改原版必须显式声明")
    return sp


def main():
    ap = argparse.ArgumentParser(description="ICEY 图集工具")
    add_common(ap)
    sub = ap.add_subparsers(dest="cmd", required=True)

    add_common(sub.add_parser("list")).set_defaults(func=cmd_list)

    e = add_common(sub.add_parser("export"))
    e.add_argument("--out")
    e.set_defaults(func=cmd_export)

    p = add_common(sub.add_parser("palette"))
    p.add_argument("--preset", default="sakura", choices=sorted(PRESETS.keys()),
                   help="配色预设,默认 sakura(浅粉)")
    p.add_argument("--lighten", type=float, default=None,
                   help="V 通道乘数(1.0 = 不变);实际 = 预设默认 x 本值,上限 %.2f" % LIGHTEN_MAX)
    p.add_argument("--saturate", type=float, default=None,
                   help="S 通道乘数(1.0 = 不变);实际 = 预设默认 x 本值,下限 %.2f" % SATURATE_MIN)
    p.add_argument("--out", help="输入目录(原始导出目录),默认 _work/player_atlas")
    p.add_argument("--png-dir", help="输出目录,默认 _work/player_atlas_<preset>")
    p.add_argument("--min-sat", type=int, default=45, help="饱和度阈值,默认 45")
    p.add_argument("--colorize", type=float, default=None,
                   help="整体染色强度 0.0~1.0(0=关闭,等同旧行为);不传=用预设默认"
                        "(sakura/blue 0.30,其余 0.25)")
    p.set_defaults(func=cmd_palette)

    t = add_common(sub.add_parser("atlas"))
    t.add_argument("--out", default=None, help="输出目录,默认 _work/player_atlas")
    t.set_defaults(func=cmd_atlas)

    g = add_common(sub.add_parser("regions"))
    g.add_argument("--atlas-dir", default=None, help=".atlas 文本所在目录,默认 _work/player_atlas")
    g.add_argument("--out", default=None, help="布局清单.md 写这里,默认 _work/player_atlas")
    g.add_argument("--quiet", action="store_true", help="只打印图集名+区域数,不逐个列区域名")
    g.set_defaults(func=cmd_regions)

    m = add_common(sub.add_parser("mcskin"))
    m.add_argument("--skin", default=None,
                   help="Minecraft 64x64 皮肤 PNG,默认 _work/mcskin/German.png")
    m.add_argument("--pixel", type=int, default=MC_PIXEL_DEFAULT,
                   help="方块边长(像素风),默认 %d;1 = 逐像素采样不量化" % MC_PIXEL_DEFAULT)
    m.add_argument("--out", default=None, help="输出目录,默认 _work/player_atlas_mc")
    m.add_argument("--install", action="store_true",
                   help="把 11 张拷进 BepInEx\\config\\player_skin\\皮肤-贴图版\\(存在 _dist 载荷也拷)")
    m.add_argument("--tint-illum", type=float, nargs="?", const=MC_TINT_DEFAULT, default=None,
                   help="_Illum 4 张 + PlayerFlash 也整体往皮肤主色调拉,默认幅度 %.2f,上限 %.2f"
                        "(不给 = 原样拷贝)" % (MC_TINT_DEFAULT, MC_TINT_MAX))
    m.set_defaults(func=cmd_mcskin)

    a = add_common(sub.add_parser("apply"))
    a.add_argument("--png-dir", required=True)
    a.add_argument("--only", default=None,
                   help="只写这些图,逗号分隔,如 NewPlayerNormal,NewPlayerAtk;不传=目录里所有能对上的")
    a.add_argument("--dry-run", action="store_true")
    a.set_defaults(func=cmd_apply)

    v = add_common(sub.add_parser("verify"))
    v.add_argument("--png-dir", default=None, help="要对比的 PNG 目录")
    v.set_defaults(func=cmd_verify)

    add_common(sub.add_parser("restore")).set_defaults(func=cmd_restore)

    args = ap.parse_args()
    return args.func(args) or 0


if __name__ == "__main__":
    sys.exit(main())
