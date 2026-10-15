#!/usr/bin/env python3
"""Generate the app icon (PNG sizes, .ico, .icns) into src/Collaterale.App/Assets. Deterministic: rerun after tweaking colors."""
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Collaterale.App", "Assets")
os.makedirs(OUT, exist_ok=True)
S = 1024


def render() -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    grad = Image.new("RGBA", (S, S))
    top, bottom = (36, 44, 66), (20, 26, 42)  # deep slate
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        gd.line([(0, y), (S, y)], fill=tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([40, 40, S - 40, S - 40], radius=230, fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img, "RGBA")
    pane = (236, 240, 248, 255)
    # two document panes
    d.rounded_rectangle([150, 210, 470, 814], radius=46, fill=pane)
    d.rounded_rectangle([554, 210, 874, 814], radius=46, fill=pane)
    # text lines: same (grey), removed (red) on the left, added (green) on the right
    grey, red, green = (160, 170, 190, 255), (240, 98, 98, 255), (72, 200, 130, 255)
    rows = [(300, grey, grey), (390, red, green), (480, red, green), (570, grey, grey), (660, grey, grey), (740, grey, grey)]
    for y, left, right in rows:
        d.rounded_rectangle([196, y, 424, y + 34], radius=17, fill=left)
        d.rounded_rectangle([600, y, 828, y + 34], radius=17, fill=right)
    # connector band between the panes over the changed rows
    d.polygon([(470, 380), (554, 380), (554, 520), (470, 520)], fill=(250, 190, 70, 255))
    return img


if __name__ == "__main__":
    big = render()
    for s in [16, 24, 32, 48, 64, 128, 256, 512]:
        big.resize((s, s), Image.LANCZOS).save(os.path.join(OUT, f"icon-{s}.png"))
    big.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "icon.png"))
    big.save(os.path.join(OUT, "icon.ico"), sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
    big.resize((1024, 1024), Image.LANCZOS).save(os.path.join(OUT, "icon.icns"))
    print("icons written to", os.path.abspath(OUT))
