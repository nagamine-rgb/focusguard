#!/usr/bin/env python3
"""FocusGuard のアイコンを生成する（要 Pillow: pip install pillow）。

デザイン: 霧色の背景に、目盛り付きの文字盤と、ブロック時間を表す赤い扇形・時計の針。
使い方:  python3 tools/gen_icons.py
- windows/icon.ico            (16〜256px、exe とウィンドウのアイコン)
- web/icon.svg                (ファビコン)
- web/apple-touch-icon.png    (180px、iPhone のホーム画面用)
- android/app/src/main/res/drawable/ic_launcher_{background,foreground,monochrome}.xml
"""
import math
import pathlib

from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent

FOG = "#E4E8E1"
INK = "#1C2620"
RED = "#C8392B"

CORNER = 0.23     # 角丸（一辺に対する割合）
SWEEP = 120       # 扇形の角度（0時から時計回り）


def pt(cx, cy, r, deg):
    t = math.radians(deg)
    return cx + r * math.sin(t), cy - r * math.cos(t)


def parts(small=False):
    """100x100 の座標で、アイコンの部品を返す（小さいサイズは目盛りを4本にして線を太く）"""
    ring_r, ring_w = (29, 4.2) if small else (27, 2.6)
    hand_w, dot_r = (5.5, 5.5) if small else (4.2, 4.6)
    sector_r = ring_r
    ticks = []
    for h in range(12):
        if small and h % 3 != 0:
            continue
        major = h % 3 == 0
        r1, r2 = (35, 41) if small else (33, 38 if major else 36.5)
        w = 4.6 if small else (3.4 if major else 2.2)
        ticks.append((pt(50, 50, r1, h * 30), pt(50, 50, r2, h * 30), w))
    return dict(ring_r=ring_r, ring_w=ring_w, sector_r=sector_r, hand_w=hand_w, dot_r=dot_r,
                hand=pt(50, 50, ring_r - 5, SWEEP), ticks=ticks)


def svg(small=False, background=True, rounded=True):
    p = parts(small)
    x1, y1 = pt(50, 50, p["sector_r"], 0)
    x2, y2 = pt(50, 50, p["sector_r"], SWEEP)
    out = []
    if background:
        rx = f' rx="{CORNER * 100:.0f}"' if rounded else ""
        out.append(f'<rect width="100" height="100"{rx} fill="{FOG}"/>')
    for (a, b, w) in p["ticks"]:
        out.append(f'<line x1="{a[0]:.2f}" y1="{a[1]:.2f}" x2="{b[0]:.2f}" y2="{b[1]:.2f}" stroke="{INK}" stroke-width="{w}" stroke-linecap="round"/>')
    out.append(f'<path d="M50 50 L{x1:.2f} {y1:.2f} A{p["sector_r"]} {p["sector_r"]} 0 0 1 {x2:.2f} {y2:.2f} Z" fill="{RED}"/>')
    out.append(f'<circle cx="50" cy="50" r="{p["ring_r"]}" fill="none" stroke="{INK}" stroke-width="{p["ring_w"]}"/>')
    hx, hy = p["hand"]
    out.append(f'<line x1="50" y1="50" x2="{hx:.2f}" y2="{hy:.2f}" stroke="{INK}" stroke-width="{p["hand_w"]}" stroke-linecap="round"/>')
    out.append(f'<circle cx="50" cy="50" r="{p["dot_r"]}" fill="{INK}"/>')
    return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">' + "".join(out) + "</svg>\n"


def hexrgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


def draw_icon(size, background=True):
    """Pillow で描く（svg() と同じ形）"""
    small = size <= 24
    ss = 8
    S = size * ss
    k = S / 100
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if background:
        d.rounded_rectangle([0, 0, S - 1, S - 1], radius=CORNER * S, fill=hexrgb(FOG))
    p = parts(small)
    for (a, b, w) in p["ticks"]:
        d.line([a[0] * k, a[1] * k, b[0] * k, b[1] * k], fill=hexrgb(INK), width=int(w * k))
        for x, y in (a, b):
            r = w * k / 2
            d.ellipse([x * k - r, y * k - r, x * k + r, y * k + r], fill=hexrgb(INK))
    c = 50 * k
    sr = p["sector_r"] * k
    d.pieslice([c - sr, c - sr, c + sr, c + sr], -90, -90 + SWEEP, fill=hexrgb(RED))
    rr, rw = p["ring_r"] * k, p["ring_w"] * k
    d.ellipse([c - rr - rw / 2, c - rr - rw / 2, c + rr + rw / 2, c + rr + rw / 2], outline=hexrgb(INK), width=int(rw))
    hx, hy = p["hand"]
    hw = p["hand_w"] * k
    d.line([c, c, hx * k, hy * k], fill=hexrgb(INK), width=int(hw))
    d.ellipse([hx * k - hw / 2, hy * k - hw / 2, hx * k + hw / 2, hy * k + hw / 2], fill=hexrgb(INK))
    dr = p["dot_r"] * k
    d.ellipse([c - dr, c - dr, c + dr, c + dr], fill=hexrgb(INK))
    return img.resize((size, size), Image.LANCZOS)


def android_vectors():
    """アダプティブアイコン: 108dp のうち中央 72dp が見える。100 の座標を 0.72 倍して中央に置く"""
    p = parts(False)
    m = lambda v: 18 + v * 0.72
    s = lambda v: v * 0.72
    x1, y1 = pt(50, 50, p["sector_r"], 0)
    x2, y2 = pt(50, 50, p["sector_r"], SWEEP)
    hx, hy = p["hand"]

    def circle(cx, cy, r):
        return f"M{m(cx) - s(r):.2f},{m(cy):.2f} a{s(r):.2f},{s(r):.2f} 0 1,0 {2 * s(r):.2f},0 a{s(r):.2f},{s(r):.2f} 0 1,0 {-2 * s(r):.2f},0 Z"

    def vec(ink, red, tick_alpha=""):
        paths = []
        for (a, b, w) in p["ticks"]:
            paths.append(f'''    <path android:pathData="M{m(a[0]):.2f},{m(a[1]):.2f} L{m(b[0]):.2f},{m(b[1]):.2f}" android:strokeColor="{ink}" android:strokeWidth="{s(w):.2f}" android:strokeLineCap="round" />''')
        paths.append(f'''    <path android:pathData="M{m(50)},{m(50)} L{m(x1):.2f},{m(y1):.2f} A{s(p["sector_r"]):.2f},{s(p["sector_r"]):.2f} 0 0,1 {m(x2):.2f},{m(y2):.2f} Z" android:fillColor="{red}" />''')
        paths.append(f'''    <path android:pathData="{circle(50, 50, p["ring_r"])}" android:strokeColor="{ink}" android:strokeWidth="{s(p["ring_w"]):.2f}" android:fillColor="#00000000" />''')
        paths.append(f'''    <path android:pathData="M{m(50)},{m(50)} L{m(hx):.2f},{m(hy):.2f}" android:strokeColor="{ink}" android:strokeWidth="{s(p["hand_w"]):.2f}" android:strokeLineCap="round" />''')
        paths.append(f'''    <path android:pathData="{circle(50, 50, p["dot_r"])}" android:fillColor="{ink}" />''')
        return ('<?xml version="1.0" encoding="utf-8"?>\n<!-- tools/gen_icons.py で生成 -->\n'
                '<vector xmlns:android="http://schemas.android.com/apk/res/android"\n'
                '    android:width="108dp"\n    android:height="108dp"\n    android:viewportWidth="108"\n    android:viewportHeight="108">\n'
                + "\n".join(paths) + "\n</vector>\n")

    res = ROOT / "android" / "app" / "src" / "main" / "res" / "drawable"
    (res / "ic_launcher_foreground.xml").write_text(vec(INK, RED), encoding="utf-8")
    # テーマアイコン（Android 13〜）: 単色。扇形は薄く
    (res / "ic_launcher_monochrome.xml").write_text(vec("#FFFFFFFF", "#88FFFFFF"), encoding="utf-8")
    (res / "ic_launcher_background.xml").write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android"\n'
        '    android:width="108dp"\n    android:height="108dp"\n    android:viewportWidth="108"\n    android:viewportHeight="108">\n'
        f'    <path\n        android:fillColor="{FOG}"\n        android:pathData="M0,0h108v108h-108z" />\n</vector>\n', encoding="utf-8")


def main():
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    imgs = [draw_icon(s) for s in sizes]
    imgs[-1].save(ROOT / "windows" / "icon.ico", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    (ROOT / "web" / "icon.svg").write_text(svg(), encoding="utf-8")
    # iPhone は角を自分で丸めるので、背景は四角いまま塗る
    apple = Image.new("RGBA", (180, 180), hexrgb(FOG))
    apple.alpha_composite(draw_icon(180, background=False))
    apple.convert("RGB").save(ROOT / "web" / "apple-touch-icon.png")
    android_vectors()
    print("windows/icon.ico, web/icon.svg, web/apple-touch-icon.png, Android のアイコンを書き出しました")


if __name__ == "__main__":
    main()
