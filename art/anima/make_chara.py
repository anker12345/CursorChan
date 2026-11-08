r"""カーソルちゃん（デフォルメのちびキャラ）を Anima Turbo で作る。

使い方: python make_chara.py [案の名前...]   （省略時は全部。ComfyUI 8188 が起動している必要がある）
生成画像は raw\<案>_<seed>.png、白背景を抜いたものは cut\<案>_<seed>.png、一覧は 候補一覧.jpg。
"""
import os, sys, time

sys.path.insert(0, r"D:\AI生成\漫画")
import manga  # ComfyUI に投げる部分（graph / comfy_run）を借りる
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
RAW, OUT = os.path.join(HERE, "raw"), os.path.join(HERE, "cut")
SEEDS = [11, 22, 33, 44]

S = {
    "title": "カーソルちゃん", "model": "anima",
    "style": dict(
        positive="masterpiece, best quality, safe, chibi, full body, simple background, white background, "
                 "sticker, cute, big head, small body, 1girl, solo, large figure, character fills the frame, mascot",
        negative="worst quality, low quality, score_1, score_2, score_3, blurry, jpeg artifacts, text, "
                 "watermark, realistic, multiple views, 2girls, small figure, tiny, distant, shadow, cropped, logo, "
                 "grey background, gradient background, photo, paper, table, sticker on paper"),
    "character": {"tags": ""},
}

# カーソルちゃん: 矢印形の白い帽子、ラベンダーの髪、星のヘアピン、セーラー服、大きな矢印を抱っこ。
# マスコットに使っているのは hatarrow の seed 303（raw\hatarrow_303.png）
DESIGNS = {
    "hatarrow": (
        "short hair, lavender hair, blue eyes, white hat, arrow-shaped hat, arrow symbol on hat, "
        "yellow star hair ornament, white dress, navy blue sailor collar, pink bow, "
        "holding giant mouse cursor, hugging arrow, thick white arrow, navy outline, smile, open mouth, blush",
        "A cute chibi mascot girl who personifies a computer mouse cursor. She wears a white hat shaped like an "
        "arrow mouse pointer and hugs a giant, thick white arrow-shaped mouse pointer with a bold navy outline, "
        "as tall as her body. Simple sticker art on a plain white background."),
}
SEEDS_BY = {"hatarrow": [101, 202, 303, 404, 505, 606, 707, 808]}


def seeds(name):
    return SEEDS_BY.get(name, SEEDS)


def cut_background(src, dst):
    """端から白をたどって透明にする（キャラの中の白は残る）。縁は少しぼかして馴染ませる。"""
    im = Image.open(src).convert("RGB")
    w, h = im.size
    flood = im.copy()
    for xy in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1), (w // 2, 0), (w // 2, h - 1), (0, h // 2), (w - 1, h // 2)]:
        if sum(flood.getpixel(xy)) > 690:
            ImageDraw.floodfill(flood, xy, (255, 0, 255), thresh=40)
    a = np.array(flood)
    bg = (a[..., 0] == 255) & (a[..., 1] == 0) & (a[..., 2] == 255)
    mask = Image.fromarray(np.where(bg, 0, 255).astype("uint8")).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(1))
    out = im.convert("RGBA")
    out.putalpha(mask)
    out = out.crop(out.getbbox())
    out.save(dst)


def main(names):
    os.makedirs(RAW, exist_ok=True)
    os.makedirs(OUT, exist_ok=True)
    for name, (tags, scene) in DESIGNS.items():
        if names and name not in names:
            continue
        for seed in seeds(name):
            raw = os.path.join(RAW, f"{name}_{seed}.png")
            if not os.path.exists(raw):
                t = time.time()
                manga.comfy_run(manga.graph(S, tags, "square", seed, scene=scene), raw)
                print(name, seed, f"{time.time() - t:.0f}s", flush=True)
            cut_background(raw, os.path.join(OUT, f"{name}_{seed}.png"))
    sheet(names)


def sheet(names=None):
    """候補の一覧（市松模様の上に置いて、背景の抜けを確かめやすくする）。8 枚の案は 2 行に折り返す"""
    C, COLS = 300, 4
    cells = [(n, s) for n in DESIGNS if not names or n in names for s in seeds(n)
             if os.path.exists(os.path.join(OUT, f"{n}_{s}.png"))]
    rows = max(1, (len(cells) + COLS - 1) // COLS)
    im = Image.new("RGB", (COLS * C, rows * C), "white")
    d = ImageDraw.Draw(im)
    for y in range(0, im.height, 20):
        for x in range(0, im.width, 20):
            if (x + y) // 20 % 2:
                d.rectangle([x, y, x + 19, y + 19], fill=(225, 225, 235))
    for i, (name, s) in enumerate(cells):
        r, k = divmod(i, COLS)
        sp = Image.open(os.path.join(OUT, f"{name}_{s}.png"))
        sp.thumbnail((C - 12, C - 12))
        im.paste(sp, (k * C + (C - sp.width) // 2, r * C + (C - sp.height) // 2), sp)
        d.text((k * C + 4, r * C + 2), f"{name}_{s}", fill="black")
    im.save(os.path.join(HERE, "候補一覧.jpg"), quality=90)


if __name__ == "__main__":
    main(sys.argv[1:])
