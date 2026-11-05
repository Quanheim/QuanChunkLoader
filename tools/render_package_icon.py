"""Thunderstore icon (Package/icon.png, 256 x 256): the Chunk Observer's rune column, glowing blue, standing on the
3 x 3 block of zones it keeps loaded. Drawn flat in the Quan mods' frame (dark tile, orange rim), like QuanPieces'
and QuanDupeLightsources' icons. Run: python tools/render_package_icon.py"""
import os

from PIL import Image, ImageDraw, ImageFilter

SS = 4                      # supersampling: draw at 1024 px, shrink to 256
N = 256 * SS
here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(here, "..", "Package", "icon.png")

RIM = (222, 149, 50, 255)
TILE = (28, 25, 22, 255)
STONE = (112, 116, 118, 255)
STONE_DARK = (78, 82, 85, 255)
STONE_LIGHT = (146, 150, 150, 255)
PANEL = (22, 34, 40, 255)
RUNE = (150, 245, 255, 255)
GLOW = (60, 200, 230)
GRID = (60, 200, 230)


def s(v):
    return int(v * SS)


img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# Frame: orange rim, dark tile.
d.rounded_rectangle([s(4), s(4), s(252), s(252)], radius=s(30), fill=RIM)
d.rounded_rectangle([s(13), s(13), s(243), s(243)], radius=s(22), fill=TILE)

# The 3 x 3 zone grid, seen at an angle on the ground: a flattened diamond under the column.
cx, cy = 128, 196
half_w, half_h = 96, 34


def ground(u, v):
    """u, v in -1.5..1.5 zone units -> pixel position on the flattened diamond."""
    x = cx + (u - v) * half_w / 3
    y = cy + (u + v) * half_h / 3
    return s(x), s(y)


grid = Image.new("RGBA", (N, N), (0, 0, 0, 0))
g = ImageDraw.Draw(grid)
g.polygon([ground(-1.5, -1.5), ground(1.5, -1.5), ground(1.5, 1.5), ground(-1.5, 1.5)], fill=GRID + (38,))
for k in (-1.5, -0.5, 0.5, 1.5):
    g.line([ground(k, -1.5), ground(k, 1.5)], fill=GRID + (150,), width=s(2))
    g.line([ground(-1.5, k), ground(1.5, k)], fill=GRID + (150,), width=s(2))
img.alpha_composite(grid)

# Blue glow around the column.
glow = Image.new("RGBA", (N, N), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.ellipse([s(70), s(40), s(186), s(210)], fill=GLOW + (120,))
glow = glow.filter(ImageFilter.GaussianBlur(s(22)))
img.alpha_composite(glow)

# The column: base steps, shaft, capital (Frogger's shape, simplified).
d = ImageDraw.Draw(img)
d.polygon([(s(94), s(204)), (s(162), s(204)), (s(156), s(190)), (s(100), s(190))], fill=STONE_DARK)  # lower step
d.rectangle([s(100), s(180), s(156), s(191)], fill=STONE)                                           # upper step
d.rectangle([s(100), s(180), s(156), s(183)], fill=STONE_LIGHT)
d.rectangle([s(107), s(60), s(149), s(180)], fill=STONE)                                            # shaft
d.rectangle([s(140), s(60), s(149), s(180)], fill=STONE_DARK)                                       # shaded side
d.rectangle([s(100), s(44), s(156), s(60)], fill=STONE)                                             # capital
d.rectangle([s(100), s(44), s(156), s(48)], fill=STONE_LIGHT)
d.rectangle([s(147), s(48), s(156), s(60)], fill=STONE_DARK)

# Rune panel and glowing runes.
d.rectangle([s(112), s(66), s(137), s(170)], fill=PANEL)
runes = Image.new("RGBA", (N, N), (0, 0, 0, 0))
r = ImageDraw.Draw(runes)
w = s(2.2)
for row in range(6):
    y0 = 71 + row * 16.5
    y1 = y0 + 11
    # Two simple rune-like marks per row, varied by row.
    for col, x in enumerate((116.5, 127.5)):
        r.line([(s(x), s(y0)), (s(x), s(y1))], fill=RUNE, width=w)
        kind = (row + col) % 3
        if kind == 0:
            r.line([(s(x), s(y0 + 2)), (s(x + 6), s(y0 + 6))], fill=RUNE, width=w)
        elif kind == 1:
            r.line([(s(x), s(y0 + 5)), (s(x + 6), s(y0 + 1))], fill=RUNE, width=w)
            r.line([(s(x), s(y0 + 5)), (s(x + 6), s(y0 + 9))], fill=RUNE, width=w)
        else:
            r.line([(s(x + 6), s(y0)), (s(x + 6), s(y1))], fill=RUNE, width=w)
            r.line([(s(x), s(y0 + 3)), (s(x + 6), s(y0 + 8))], fill=RUNE, width=w)
halo = runes.filter(ImageFilter.GaussianBlur(s(3)))
img.alpha_composite(halo)
img.alpha_composite(halo)
img.alpha_composite(runes)

img = img.resize((256, 256), Image.LANCZOS)
img.save(out)
print(os.path.abspath(out))
