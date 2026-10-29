#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
check-skin-log.py —— 一键核对插件写进 BepInEx\\LogOutput.log 的 [skin] 日志

做三件事:
  1. 抓出所有 [skin] 行(可只看最后一次启动,`--all` 看全部);
  2. 对每条 "载入 … -> WxH … 已上下翻/未翻 采样: (x,y)=r,g,b,a …" 都拿
     BepInEx\\config\\player_skin\\<预设>\\<贴图名>.png 的像素**逐点核对**
     —— 顺带验证"上下翻"到底有没有按预期生效;
  3. 汇总:命中的目标来源数、尺寸不符/缺图/没还原的告警、以及诊断段的关键行。

坐标约定(核对的依据,别搞混):
  * 插件日志里的 (x,y) 是 Unity GetPixel 的坐标(y 从**下**往上);
  * 贴图**未翻**时:GetPixel(x,y) == PNG 的 (x, H-1-y);
  * 贴图**已上下翻**后:GetPixel(x,y) == PNG 的 (x, y)(翻完行序就一致了)。

用法:
  python check-skin-log.py                 # 默认只分析最后一次启动的日志段
  python check-skin-log.py --all           # 分析整个日志文件
  python check-skin-log.py --log <路径>    # 指定日志
"""

import argparse
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
MODDEV = os.path.dirname(HERE)
DEFAULT_LOG = os.path.join(MODDEV, "BepInEx", "LogOutput.log")
SKIN_ROOT = os.path.join(MODDEV, "BepInEx", "config", "player_skin")

LOAD_RE = re.compile(
    r"\[skin\] 载入 (?P<path>.+?\.png) -> (?P<w>\d+)x(?P<h>\d+) fmt=(?P<fmt>\w+) mip=(?P<mip>\d+)"
    r"(?P<flip> 已上下翻| 未翻)? 采样:(?P<samples>.*)$")
SAMPLE_RE = re.compile(r"\((?P<x>\d+),(?P<y>\d+)\)=(?P<r>\d+),(?P<g>\d+),(?P<b>\d+),(?P<a>\d+)")


def last_boot(lines):
    """只保留最后一次启动之后的部分(BepInEx 每次启动都写 'BepInEx 5.4.23.5 - ICEY' 这行)"""
    idx = [i for i, l in enumerate(lines) if "BepInEx 5.4.23.5 - ICEY" in l]
    return lines[idx[-1]:] if idx else lines


def parse(args):
    path = args.log or DEFAULT_LOG
    if not os.path.exists(path):
        print("!! 找不到日志: %s" % path)
        return 1
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        lines = f.read().splitlines()
    if not args.all:
        lines = last_boot(lines)
    skin = [l for l in lines if "[skin]" in l]
    if not skin:
        print("!! 这段日志里没有 [skin] 行(插件没加载?还是没开过配色开关?)")
        return 2

    print("=== 概览 ===")
    print("  [skin] 行数: %d" % len(skin))
    for key, pat in (("载入贴图", "载入 "), ("扫描", "扫描:"), ("尺寸不符", "尺寸不符"),
                     ("缺 PNG", "缺少这些 PNG"), ("没还原", "没还原"), ("诊断", "诊断开始"),
                     ("渲染器清单", "玩家层级渲染器清单"), ("原贴图失效", "原贴图"),
                     ("上下翻", "上下翻")):
        n = sum(1 for l in skin if pat in l)
        if n:
            print("  %-10s %d 行" % (key, n))

    # ---- 采样核对 ----
    from PIL import Image
    print()
    print("=== 载入 / 采样核对(和 config\\player_skin 下的 PNG 逐点比)===")
    if not os.path.isdir(SKIN_ROOT):
        print("  !! 没找到预设目录: %s" % SKIN_ROOT)
    seen = {}
    ok = bad = nopng = 0
    for line in skin:
        m = LOAD_RE.search(line)
        if not m:
            continue
        p = m.group("path")
        flip = (m.group("flip") or "").strip()
        w, h = int(m.group("w")), int(m.group("h"))
        # 日志里的路径可能是别的机器/别的目录 → 用 config 下同名文件
        preset = os.path.basename(os.path.dirname(p))
        name = os.path.basename(p)
        local = os.path.join(SKIN_ROOT, preset, name)
        tag = "%s/%s" % (preset, name)
        if tag in seen:
            continue
        seen[tag] = True
        if not os.path.exists(local):
            print("  ?  %-42s 本地没有对应 PNG(%s)" % (tag, local))
            nopng += 1
            continue
        im = Image.open(local).convert("RGBA")
        if im.size != (w, h):
            print("  !  %-42s 尺寸不符: 日志 %dx%d / PNG %s" % (tag, w, h, im.size))
            bad += 1
            continue
        errs = []
        for sm in SAMPLE_RE.finditer(m.group("samples") or ""):
            x, y = int(sm.group("x")), int(sm.group("y"))
            want = (int(sm.group("r")), int(sm.group("g")), int(sm.group("b")), int(sm.group("a")))
            py = y if flip == "已上下翻" else (h - 1 - y)
            got = im.getpixel((min(x, im.size[0] - 1), min(py, im.size[1] - 1)))
            if tuple(got) != want:
                errs.append("(%d,%d) 日志=%s PNG(x,%d)=%s" % (x, y, want, py, got))
        if errs:
            print("  X  %-42s %s" % (tag, "; ".join(errs)))
            bad += 1
        else:
            print("  OK %-42s %s 采样逐点一致" % (tag, flip or "(没有翻转标记:旧版插件)"))
            ok += 1
    print("  ---- 采样核对: 一致 %d / 不一致 %d / 缺文件 %d ----" % (ok, bad, nopng))

    # ---- 关键告警 ----
    warn = [l for l in skin if any(k in l for k in
            ("尺寸不符", "缺少这些 PNG", "没还原", "查不到原图", "解码失败", "跳过写入", "出错"))]
    if warn:
        print()
        print("=== 告警(%d 条)===" % len(warn))
        for l in warn[-20:]:
            print("  " + l.split("] ", 1)[-1].strip())

    # ---- 命中来源 ----
    hits = [l for l in skin if re.search(r"\[skin\]\s+\S+\s+<-", l)]
    if hits:
        print()
        print("=== 命中的贴图来源(最后一次扫描)===")
        for l in hits[-(len(hits) if len(hits) <= 14 else 14):]:
            print("  " + l.split("] ", 1)[-1].strip())

    print()
    print("判读提示: '载入 … -> WxH … 采样逐点一致' = 上传内容与 PNG 相同(方向对不对看游戏画面);")
    print("          若全部 '不一致',说明 LoadImage 那条路真的坏了 → 改用静态路径 atlas_tool apply。")
    return 0 if bad == 0 else 3


def main():
    ap = argparse.ArgumentParser(description="核对 [skin] 日志")
    ap.add_argument("--log", help="日志路径,默认 _moddev\\BepInEx\\LogOutput.log")
    ap.add_argument("--all", action="store_true", help="分析整个日志文件(默认只看最后一次启动)")
    return parse(ap.parse_args())


if __name__ == "__main__":
    sys.exit(main())
