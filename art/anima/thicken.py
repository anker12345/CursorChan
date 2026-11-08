r"""hatarrow_303 の矢印の軸を太くする。

1. 軸の左側（女の子と反対側）を手で塗り足して太くした下絵を作る → raw\thick_base.png
2. 下絵を Anima の img2img で軽く描き直して馴染ませる → raw\thick_<強さ>_<seed>.png
使い方: python thicken.py base   （下絵だけ作る）
        python thicken.py        （下絵＋img2img）
"""
import os, sys, time
import numpy as np

sys.path.insert(0, r"D:\AI生成\漫画")
import manga
from PIL import Image, ImageDraw

import make_chara as mc

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "raw", "hatarrow_303.png")
BASE = os.path.join(HERE, "raw", "thick_base.png")
WIDEN = int(os.environ.get("WIDEN", "60"))   # 何 px 太くするか（1024 px 画像で）

# 軸の左の縁（くびれ → 下端）。画像を見て決めた座標
A, B = np.array([262.0, 340.0]), np.array([530.0, 935.0])
NAVY, WHITE, LINE = (31, 34, 77), (246, 246, 252), 16


def make_base(W=None, hide_hand=False, dest=None):
    """軸の左の紺の縁は x = 309 + 0.5125 (y - 500) から 33 px（画素を測った値）。
    その縁を W px 左へずらし、元の縁があった所は白で塗る。
    hide_hand: 軸の左の縁をつかんでいる右手（見る側から左の小さい手）を白で消す。"""
    im = Image.open(SRC).convert("RGB")
    d = ImageDraw.Draw(im)
    k, W = 0.5125, W or WIDEN
    dest = dest or BASE
    if hide_hand:
        d.ellipse([336, 508, 392, 572], fill=WHITE)
    out = lambda y: 309 + k * (y - 500)        # 紺の縁の外側
    inn = lambda y: out(y) + 33                 # 紺の縁の内側
    y0, y1 = 452, 940                           # 矢じりの下 〜 軸の下端の手前
    # 1. 元の紺の縁とその外側を白で塗る（新しい軸の中身になる）
    d.polygon([(inn(y0) - W, y0), (inn(y0) + 3, y0), (inn(y1) + 3, y1), (inn(y1) - W, y1)], fill=WHITE)
    # 2. 新しい紺の縁
    d.polygon([(out(y0) - W, y0), (inn(y0) - W, y0), (inn(y1) - W, y1), (out(y1) - W, y1)], fill=NAVY)
    # 3. 下端のふた（元のふたと同じ向き＝軸に直角）を左へ延ばす
    cx, cy = 1.0, -k                            # ふたの向き（右上へ）
    n = np.hypot(cx, cy); cx, cy = cx / n, cy / n
    p = np.array([out(y1) - W, y1])             # 新しい縁の下端
    q = np.array([inn(y1) + 20, y1]) + np.array([cx, cy]) * 0  # 元の縁の下端あたり
    band = np.array([-cy, cx]) * 33             # ふたの厚み（下側へ）
    if band[1] < 0:
        band = -band
    d.polygon([tuple(p), tuple(q), tuple(q + band), tuple(p + band)], fill=NAVY)
    # 4. ステッカーの細い外枠
    d.line([(out(y0) - W - 26, y0 + 10), (out(y1) - W - 26, y1 + 10)], fill=(60, 60, 90), width=2)
    im.save(dest)
    print("base ->", dest)


def img2img(denoise, seed, src=None, tag="thick", extra=""):
    src = src or BASE
    out = os.path.join(HERE, "raw", f"{tag}_{int(denoise * 100)}_{seed}.png")
    if os.path.exists(out):
        return out
    tags, scene = mc.DESIGNS["hatarrow"]
    tags += ", extremely thick arrow, wide arrow shaft, chunky arrow" + extra
    g = manga.graph(mc.S, tags, "square", seed, scene=scene)
    g["30"] = {"class_type": "LoadImage", "inputs": {"image": manga.comfy_upload(src)}}
    g["31"] = {"class_type": "VAEEncode", "inputs": {"pixels": ["30", 0], "vae": ["4", 0]}}
    g["3"]["inputs"]["latent_image"] = ["31", 0]
    g["3"]["inputs"]["denoise"] = denoise
    del g["5"]
    t = time.time()
    manga.comfy_run(g, out)
    print(os.path.basename(out), f"{time.time() - t:.0f}s", flush=True)
    return out


def sheet(paths):
    C, COLS = 400, 4
    rows = (len(paths) + COLS - 1) // COLS
    im = Image.new("RGB", (COLS * C, rows * C), "white")
    d = ImageDraw.Draw(im)
    for i, p in enumerate(paths):
        r, k = divmod(i, COLS)
        sp = Image.open(p).convert("RGB")
        sp.thumbnail((C - 8, C - 8))
        im.paste(sp, (k * C + 4, r * C + 4))
        d.text((k * C + 6, r * C + 4), os.path.basename(p)[:-4], fill="red")
    im.save(os.path.join(HERE, "太くした候補.jpg"), quality=90)


if __name__ == "__main__":
    if sys.argv[1:] == ["redraw"]:
        # 下絵を作らず、元絵をそのまま「太い矢印」の指示で描き直す
        paths = [SRC]
        for dn in (0.55, 0.65, 0.75):
            for seed in (303, 1, 2, 3, 4):
                paths.append(img2img(dn, seed, SRC, "redraw"))
        sheet(paths)
        sys.exit()
    if sys.argv[1:] == ["hide"]:
        # 右手は見えなくしてよいので、もっと太くする
        paths = [SRC]
        for W in (95, 125):
            base = os.path.join(HERE, "raw", f"hide{W}_base.png")
            make_base(W, hide_hand=True, dest=base)
            paths.append(base)
            for dn in (0.45, 0.55):
                for seed in (1, 2, 3):
                    paths.append(img2img(dn, seed, base, f"hide{W}", ", one arm hidden behind arrow"))
        sheet(paths)
        sys.exit()
    make_base()
    if sys.argv[1:] != ["base"]:
        paths = [SRC, BASE]
        for dn in (0.45, 0.55, 0.65):
            for seed in (303, 1, 2, 3, 4):
                paths.append(img2img(dn, seed))
        sheet(paths)
