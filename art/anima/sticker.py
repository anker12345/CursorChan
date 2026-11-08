r"""生成画像の白背景を抜いて、白いステッカー縁を付けた正方形の PNG にする。

使い方: python sticker.py <生成画像> <出力PNG> [サイズ]
"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter


def cut(path):
    im = Image.open(path).convert("RGB")
    w, h = im.size
    flood = im.copy()
    edge = [(x, 0) for x in range(0, w, 8)] + [(x, h - 1) for x in range(0, w, 8)] + \
           [(0, y) for y in range(0, h, 8)] + [(w - 1, y) for y in range(0, h, 8)]
    for xy in edge:
        p = flood.getpixel(xy)
        if p != (255, 0, 255) and sum(p) > 660:
            ImageDraw.floodfill(flood, xy, (255, 0, 255), thresh=40)
    a = np.array(flood)
    bg = (a[..., 0] == 255) & (a[..., 1] == 0) & (a[..., 2] == 255)
    m = Image.fromarray(((~bg) * 255).astype("uint8"))
    m = m.filter(ImageFilter.MinFilter(15)).filter(ImageFilter.MaxFilter(15))   # 小さなゴミを消す
    # 真ん中からつながっている部分（キャラ本体）だけ残す
    seed = next((x, h // 2) for x in range(w // 2, w) if m.getpixel((x, h // 2)) == 255)
    ImageDraw.floodfill(m, seed, 128)
    keep = np.array(m) == 128
    m = Image.fromarray((keep * 255).astype("uint8")).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(1))
    out = im.convert("RGBA")
    out.putalpha(m)
    return out.crop(out.getbbox())


def sticker(im, border=25, pad=40):
    w, h = im.size
    base = Image.new("RGBA", (w + 2 * pad, h + 2 * pad), (0, 0, 0, 0))
    base.paste(im, (pad, pad), im)
    al = base.getchannel("A").point(lambda v: 255 if v > 40 else 0)
    outline = al.filter(ImageFilter.MaxFilter(border)).filter(ImageFilter.GaussianBlur(1.5))
    white = Image.new("RGBA", base.size, (255, 255, 255, 255))
    white.putalpha(outline)
    st = Image.alpha_composite(white, base)
    return st.crop(st.getbbox())


def square(im, size, pad=0.03):
    w, h = im.size
    s = max(w, h)
    c = int(s * (1 + pad * 2))
    cv = Image.new("RGBA", (c, c), (0, 0, 0, 0))
    cv.paste(im, ((c - w) // 2, (c - h) // 2), im)
    return cv.resize((size, size), Image.LANCZOS)


if __name__ == "__main__":
    src, dst = sys.argv[1], sys.argv[2]
    size = int(sys.argv[3]) if len(sys.argv) > 3 else 1024
    square(sticker(cut(src)), size).save(dst)
    print("->", dst)
