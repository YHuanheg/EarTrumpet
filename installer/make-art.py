#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成 NSIS 向导用的两张配图(纯标准库,不依赖 Pillow)。

    python installer/make-art.py

产物(直接入库,改了配色或尺寸才需要重跑):
    installer/art/header.bmp    150x57    安装/卸载向导页眉(浅蓝 -> 白渐变 + 蓝色 logo)
    installer/art/welcome.bmp   164x314   欢迎页/完成页左侧竖图(深蓝 -> 亮蓝渐变 + 白色 logo)

素材是仓库自带的官方 logo `Resources/EarTrumpet-768-black.png`(调色板 PNG,
黑图 + tRNS 透明度)。这里把「墨量」定义成 1-luminance(再乘 alpha),
所以纯黑图能被重新着成任意颜色,也不会出现白底方框。

注意:NSIS 的 MUI 要求页眉严格 150x57、欢迎图严格 164x314,尺寸不对会被
FitControl 拉伸变形。
"""
import pathlib, struct, zlib

HERE = pathlib.Path(__file__).resolve().parent
SRC = HERE.parent / "Resources" / "EarTrumpet-768-black.png"
OUT = HERE / "art"

# ---- 配色 ----
HEADER_BG = ((233, 241, 251), (255, 255, 255))   # 左 -> 右
HEADER_INK = (0, 103, 192)                        # Windows 系统蓝
PANEL_BG = ((11, 95, 165), (24, 126, 214))        # 上 -> 下
PANEL_INK = (255, 255, 255)


# ---------------- PNG 解码(8 位,支持灰度/真彩/调色板/带 alpha) ----------------
def decode_png(path):
    d = pathlib.Path(path).read_bytes()
    assert d[:8] == b"\x89PNG\r\n\x1a\n", "不是 PNG"
    pos, idat, plte, trns = 8, b"", None, None
    w = h = bitd = ctype = None
    while pos < len(d):
        ln = struct.unpack_from(">I", d, pos)[0]
        tag = d[pos + 4:pos + 8]
        body = d[pos + 8:pos + 8 + ln]
        pos += 12 + ln
        if tag == b"IHDR":
            w, h, bitd, ctype, _c, _f, inter = struct.unpack(">IIBBBBB", body)
            assert inter == 0, "不支持隔行 PNG"
        elif tag == b"PLTE":
            plte = [tuple(body[i:i + 3]) for i in range(0, len(body), 3)]
        elif tag == b"tRNS":
            trns = list(body)
        elif tag == b"IDAT":
            idat += body
        elif tag == b"IEND":
            break
    assert bitd == 8, "只支持 8 位深(实际 %s)" % bitd

    nch = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ctype]
    raw = zlib.decompress(idat)
    stride = w * nch
    rows, prev, p = [], bytearray(stride), 0
    for _ in range(h):
        f = raw[p]; p += 1
        line = bytearray(raw[p:p + stride]); p += stride
        if f == 1:
            for i in range(nch, stride):
                line[i] = (line[i] + line[i - nch]) & 0xFF
        elif f == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif f == 3:
            for i in range(stride):
                a = line[i - nch] if i >= nch else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
        elif f == 4:
            for i in range(stride):
                a = line[i - nch] if i >= nch else 0
                b = prev[i]
                c = prev[i - nch] if i >= nch else 0
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        rows.append(bytes(line))
        prev = line
    return w, h, ctype, nch, plte, trns, rows


def ink_map(path):
    """返回 (w, h, ink):ink[y][x] ∈ [0,1],1 = 完全着墨。"""
    w, h, ctype, nch, plte, trns, rows = decode_png(path)
    colors, ink = set(), []
    for line in rows:
        r = []
        for x in range(w):
            o = x * nch
            if ctype == 3:
                idx = line[o]
                rgb = plte[idx]
                a = 255 if trns is None or idx >= len(trns) else trns[idx]
            elif ctype == 0:
                rgb, a = (line[o],) * 3, 255
            elif ctype == 4:
                rgb, a = (line[o],) * 3, line[o + 1]
            elif ctype == 2:
                rgb, a = (line[o], line[o + 1], line[o + 2]), 255
            else:
                rgb, a = (line[o], line[o + 1], line[o + 2]), line[o + 3]
            if len(colors) < 40:
                colors.add(rgb)
            lum = (0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]) / 255.0
            r.append((1.0 - lum) * (a / 255.0))
        ink.append(r)
    return w, h, ink, colors


def bbox(ink, w, h, thr=0.03):
    """墨迹包围盒 —— logo 四周有大片留白,不裁掉会白白缩小主体。"""
    x0, y0, x1, y1 = w, h, -1, -1
    for y in range(h):
        row = ink[y]
        for x in range(w):
            if row[x] > thr:
                if x < x0: x0 = x
                if x > x1: x1 = x
                if y < y0: y0 = y
                if y > y1: y1 = y
    return x0, y0, x1, y1


def fit(ink, bx, max_w, max_h):
    """把包围盒内的墨迹等比缩放进 max_w x max_h(contain),返回 (logo, w, h)。"""
    x0, y0, x1, y1 = bx
    bw, bh = x1 - x0 + 1, y1 - y0 + 1
    s = min(max_w / bw, max_h / bh)
    tw, th = max(1, round(bw * s)), max(1, round(bh * s))
    out = []
    for ty in range(th):
        ia = int(y0 + ty * bh / th)
        ib = max(ia + 1, min(y1 + 1, int(y0 + (ty + 1) * bh / th + 0.999)))
        row = []
        for tx in range(tw):
            ja = int(x0 + tx * bw / tw)
            jb = max(ja + 1, min(x1 + 1, int(x0 + (tx + 1) * bw / tw + 0.999)))
            acc, n = 0.0, 0
            for y in range(ia, ib):
                src = ink[y]
                for x in range(ja, jb):
                    acc += src[x]
                    n += 1
            row.append(acc / n if n else 0.0)
        out.append(row)
    return out, tw, th


def lerp(c0, c1, t):
    return tuple(round(c0[i] + (c1[i] - c0[i]) * t) for i in range(3))


def compose(bg0, bg1, ink_color, logo, tw, th, logo_x, logo_y, horizontal=True):
    canvas = [[list(lerp(bg0, bg1, (x / max(1, tw - 1)) if horizontal else (y / max(1, th - 1))))
               for x in range(tw)] for y in range(th)]
    for y, row in enumerate(logo):
        yy = logo_y + y
        if not (0 <= yy < th):
            continue
        for x, k in enumerate(row):
            xx = logo_x + x
            if k <= 0 or not (0 <= xx < tw):
                continue
            c = canvas[yy][xx]
            for i in range(3):
                c[i] = c[i] * (1 - k) + ink_color[i] * k
    return canvas


def write_bmp(path, canvas):
    th, tw = len(canvas), len(canvas[0])
    stride = (tw * 3 + 3) // 4 * 4
    pad = b"\x00" * (stride - tw * 3)
    body = b"".join(
        b"".join(bytes((round(c[2]), round(c[1]), round(c[0]))) for c in canvas[y]) + pad
        for y in range(th - 1, -1, -1)      # BMP 自下而上存储
    )
    hdr = b"BM" + struct.pack("<IHHI", 14 + 40 + len(body), 0, 0, 14 + 40)
    dib = struct.pack("<IiiHHIIiiII", 40, tw, th, 1, 24, 0, len(body), 2835, 2835, 0, 0)
    pathlib.Path(path).write_bytes(hdr + dib + body)


def main():
    w, h, ink, colors = ink_map(SRC)
    bx = bbox(ink, w, h)
    print("源图 %dx%d,颜色数 %d,墨迹包围盒 x %d..%d y %d..%d (宽高比 %.2f:1)"
          % (w, h, len(colors), bx[0], bx[2], bx[1], bx[3],
             (bx[2] - bx[0] + 1) / (bx[3] - bx[1] + 1)))

    OUT.mkdir(parents=True, exist_ok=True)

    hw, hh = 150, 57
    head_logo, lw, lh = fit(ink, bx, 86, 34)
    write_bmp(OUT / "header.bmp",
              compose(HEADER_BG[0], HEADER_BG[1], HEADER_INK, head_logo, hw, hh,
                      12, (hh - lh) // 2, horizontal=True))

    pw, ph = 164, 314
    panel_logo, plw, plh = fit(ink, bx, 132, 66)
    write_bmp(OUT / "welcome.bmp",
              compose(PANEL_BG[0], PANEL_BG[1], PANEL_INK, panel_logo, pw, ph,
                      (pw - plw) // 2, 62, horizontal=False))

    for n in ("header.bmp", "welcome.bmp"):
        p = OUT / n
        b = p.read_bytes()
        iw, ih = struct.unpack_from("<ii", b, 18)
        bpp = struct.unpack_from("<H", b, 28)[0]
        print("  %-12s %7d 字节  %dx%d  %d bpp" % (n, p.stat().st_size, iw, ih, bpp))


if __name__ == "__main__":
    main()
