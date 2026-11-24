#!/usr/bin/env python3
"""FocusGuard のアイコンを生成する（要 Pillow: pip install pillow）。

デザイン: 墨色の角丸四角に、アプリの文字盤と同じ「リング・柿色の弧・現在時刻の点」。
使い方:  python3 tools/gen_icons.py
- windows/icon.ico            (16〜256px、exe とウィンドウのアイコン)
- web/icon.svg                (ファビコン)
- web/apple-touch-icon.png    (180px、iPhone のホーム画面用)
Android は android/app/src/main/res/ のベクター（同じ寸法）を直接使う。
"""
import math
import pathlib

from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent

INK = (28, 38, 32, 255)        # #1C2620
TRACK = (58, 71, 64, 255)      # #3A4740
ARC = (224, 122, 69, 255)      # #E07A45
FOG = (228, 232, 225, 255)     # #E4E8E1

# 1 = アイコンの一辺。リング中心は (0.5, 0.5)
RING_R = 0.27
RING_W = 0.10
SWEEP = 120          # 弧の長さ（度）。0時から時計回り
DOT_R = 0.072
DOT_RING = 0.028
CORNER = 0.23


def draw_icon(size: int, background: bool = True) -> Image.Image:
    ss = 4
    S = size * ss
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if background:
        d.rounded_rectangle([0, 0, S - 1, S - 1], radius=CORNER * S, fill=INK)
    small = size <= 24  # 小さいサイズは線を太くして潰れないようにする
    c = S / 2
    r = (0.26 if small else RING_R) * S
    w = (0.15 if small else RING_W) * S
    box = [c - r - w / 2, c - r - w / 2, c + r + w / 2, c + r + w / 2]
    d.arc(box, 0, 360, fill=TRACK, width=int(round(w)))
    d.arc(box, -90, -90 + SWEEP, fill=ARC, width=int(round(w)))
    # 弧の始まりを丸く
    d.ellipse([c - w / 2, c - r - w / 2, c + w / 2, c - r + w / 2], fill=ARC)
    # 現在時刻の点（弧の終わり）
    th = math.radians(SWEEP)
    x, y = c + r * math.sin(th), c - r * math.cos(th)
    dot_r = 0.09 if small else DOT_R
    o = (dot_r + DOT_RING) * S
    d.ellipse([x - o, y - o, x + o, y + o], fill=INK)
    i = dot_r * S
    d.ellipse([x - i, y - i, x + i, y + i], fill=FOG)
    return img.resize((size, size), Image.LANCZOS)


def svg() -> str:
    th = math.radians(SWEEP)
    r = RING_R * 100
    ex, ey = 50 + r * math.sin(th), 50 - r * math.cos(th)
    large = 1 if SWEEP > 180 else 0
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
  <rect width="100" height="100" rx="{CORNER * 100:.1f}" fill="#1C2620"/>
  <circle cx="50" cy="50" r="{r:.2f}" fill="none" stroke="#3A4740" stroke-width="{RING_W * 100:.1f}"/>
  <path d="M50 {50 - r:.2f} A{r:.2f} {r:.2f} 0 {large} 1 {ex:.2f} {ey:.2f}" fill="none" stroke="#E07A45" stroke-width="{RING_W * 100:.1f}" stroke-linecap="round"/>
  <circle cx="{ex:.2f}" cy="{ey:.2f}" r="{(DOT_R + DOT_RING) * 100:.2f}" fill="#1C2620"/>
  <circle cx="{ex:.2f}" cy="{ey:.2f}" r="{DOT_R * 100:.2f}" fill="#E4E8E1"/>
</svg>
"""


def main():
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    imgs = [draw_icon(s) for s in sizes]
    imgs[-1].save(ROOT / "windows" / "icon.ico", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    (ROOT / "web" / "icon.svg").write_text(svg(), encoding="utf-8")
    # iPhone は角を自分で丸めるので、背景は四角いまま塗る
    apple = Image.new("RGBA", (180, 180), INK)
    apple.alpha_composite(draw_icon(180, background=False))
    apple.convert("RGB").save(ROOT / "web" / "apple-touch-icon.png")
    print("windows/icon.ico, web/icon.svg, web/apple-touch-icon.png を書き出しました")


if __name__ == "__main__":
    main()
