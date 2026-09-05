"""Generate the app icon set for both apps: a stopwatch + sound-pulse mark.

    python tools/gen_icons.py

Writes the MSIX asset PNGs into each app's Assets folder and an app.ico for the
overlay's tray icon. Pure Pillow, no external assets.
"""
import math
import os
import struct

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APPS = ["IntervalTimerOverlay", "IntervalTimerWidget"]

BG_TOP = (99, 102, 241)      # indigo-500
BG_BOT = (124, 58, 237)      # violet-600
FG = (255, 255, 255, 255)

SS = 4  # supersample factor


def _lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def _rounded_mask(size, radius):
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return m


def _gradient(size):
    g = Image.new("RGB", (size, size))
    px = g.load()
    for y in range(size):
        c = _lerp(BG_TOP, BG_BOT, y / max(1, size - 1))
        for x in range(size):
            px[x, y] = c
    return g


def draw_icon(size, plated=True, margin_ratio=0.0):
    """Return an RGBA image of the icon at `size`."""
    S = size * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    if plated:
        grad = _gradient(S).convert("RGBA")
        grad.putalpha(_rounded_mask(S, int(S * 0.18)))
        img.alpha_composite(grad)

    d = ImageDraw.Draw(img)

    m = margin_ratio * S
    cx, cy = S / 2, S * 0.545
    r = (S / 2 - m) * (0.60 if plated else 0.78)
    lw = max(SS, int(r * 0.14))

    stroke = FG if plated else (255, 255, 255, 255)
    detailed = size >= 28  # below that, just a bold clock face + hand

    if detailed:
        # top button + stem
        d.rounded_rectangle(
            [cx - r * 0.20, cy - r - r * 0.55, cx + r * 0.20, cy - r - r * 0.12],
            radius=lw, fill=stroke,
        )
        d.line([cx, cy - r - r * 0.12, cx, cy - r + r * 0.10], fill=stroke, width=lw)

        # side lugs
        for ang in (-38, 38):
            a = math.radians(ang - 90)
            ox, oy = math.cos(a), math.sin(a)
            d.line(
                [cx + ox * (r + lw * 0.2), cy + oy * (r + lw * 0.2),
                 cx + ox * (r + lw * 1.7), cy + oy * (r + lw * 1.7)],
                fill=stroke, width=lw,
            )

    # dial
    dial_w = lw if detailed else max(SS, int(lw * 1.25))
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=stroke, width=dial_w)

    # hand -> ~1 o'clock
    ha = math.radians(38 - 90)
    d.line([cx, cy, cx + math.cos(ha) * r * 0.62, cy + math.sin(ha) * r * 0.62],
           fill=stroke, width=dial_w)
    d.ellipse([cx - lw * 0.9, cy - lw * 0.9, cx + lw * 0.9, cy + lw * 0.9], fill=stroke)

    # sound pulse arcs on the right
    if size >= 40:
        for i, rr in enumerate((1.35, 1.75, 2.15)):
            bb = [cx - r * rr, cy - r * rr, cx + r * rr, cy + r * rr]
            d.arc(bb, start=-32, end=32, fill=stroke, width=max(SS, int(lw * (0.9 - i * 0.12))))

    return img.resize((size, size), Image.LANCZOS)


def compose(canvas_w, canvas_h, icon_frac, transparent=False):
    """Icon centred on a plate (or transparent) canvas — for tiles / splash / wide."""
    if transparent:
        bg = Image.new("RGBA", (canvas_w, canvas_h), (0, 0, 0, 0))
    else:
        g = _gradient(max(canvas_w, canvas_h)).convert("RGBA").crop((0, 0, canvas_w, canvas_h))
        bg = g
    s = int(min(canvas_w, canvas_h) * icon_frac)
    ic = draw_icon(s, plated=False)
    bg.alpha_composite(ic, ((canvas_w - s) // 2, (canvas_h - s) // 2))
    return bg


# filename -> (writer producing an RGBA image)
def build_set():
    return {
        "Square44x44Logo.scale-200.png":       lambda: draw_icon(88),
        "Square44x44Logo.targetsize-24_altform-unplated.png": lambda: draw_icon(24, plated=False),
        "Square150x150Logo.scale-200.png":     lambda: compose(300, 300, 0.62),
        "Wide310x150Logo.scale-200.png":       lambda: compose(620, 300, 0.72),
        "SplashScreen.scale-200.png":          lambda: compose(1240, 600, 0.34),
        "LockScreenLogo.scale-200.png":        lambda: draw_icon(48, plated=False),
        "StoreLogo.png":                       lambda: draw_icon(50),
    }


def write_ico(path, sizes=(256, 128, 64, 48, 32, 24, 16)):
    # One purpose-drawn frame per size (small ones are simplified in draw_icon), all
    # plated so the white glyph stays visible on light *and* dark backgrounds.
    imgs = [draw_icon(s, plated=True) for s in sizes]
    imgs[0].save(path, format="ICO", append_images=imgs[1:])
    # sanity check
    from PIL import Image as _I
    got = sorted(_I.open(path).info.get("sizes", []))
    if len(got) < len(sizes):
        raise RuntimeError(f"ICO only has {got}; expected {sizes}")


def main():
    for app in APPS:
        out = os.path.join(ROOT, app, "Assets")
        os.makedirs(out, exist_ok=True)
        for name, make in build_set().items():
            p = os.path.join(out, name)
            make().save(p, format="PNG")
            print("wrote", os.path.relpath(p, ROOT))

    ico = os.path.join(ROOT, "IntervalTimerOverlay", "Assets", "app.ico")
    write_ico(ico)
    print("wrote", os.path.relpath(ico, ROOT))


if __name__ == "__main__":
    main()
