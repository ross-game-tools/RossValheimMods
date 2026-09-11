#!/usr/bin/env python3
"""Regenerate the Thunderstore package icon.

Checked in alongside the PNG it produces, for the same reason the drawer
meshes and textures are generated rather than shipped: every asset in this
mod has readable source. Run it when the wordmark or palette changes --
Thunderstore requires exactly 256x256 PNG and rejects anything else.

    python ItemDrawers/tools/make-icon.py

Requires Pillow. Uses Segoe UI Bold, so it wants a Windows font directory.
"""
from PIL import Image, ImageDraw, ImageFont

SIZE = 256
WORDS = ["Ross", "Item", "Drawers"]
FONT_PATH = r"C:\Windows\Fonts\segoeuib.ttf"
OUT = "ItemDrawers/thunderstore/icon.png"

# The in-game label palette: cream on drawer wood, so the store tile reads
# as the thing it installs rather than as generic mod art.
BG_TOP, BG_BOTTOM = (86, 61, 40), (54, 38, 25)
TEXT, SHADOW = (246, 236, 214), (12, 9, 6)

MAX_WIDTH = SIZE - 72       # "Drawers" is the widest word and sets the size
SLOT = (SIZE - 64) / 3.0    # vertical room one line may occupy
MARGIN = 28


def largest_fitting_size(word):
    size = 8
    while size < 200:
        font = ImageFont.truetype(FONT_PATH, size + 1)
        left, top, right, bottom = font.getbbox(word)
        if right - left > MAX_WIDTH or bottom - top > SLOT * 0.78:
            break
        size += 1
    return size


def main():
    img = Image.new("RGB", (SIZE, SIZE))
    draw = ImageDraw.Draw(img)

    for y in range(SIZE):
        t = y / (SIZE - 1)
        draw.line([(0, y), (SIZE, y)], fill=tuple(
            round(a + (b - a) * t) for a, b in zip(BG_TOP, BG_BOTTOM)))

    # The drawer front's recessed framing, in miniature.
    draw.rectangle([7, 7, SIZE - 8, SIZE - 8], outline=(38, 27, 18), width=3)
    draw.rectangle([10, 10, SIZE - 11, SIZE - 11], outline=(120, 88, 58), width=2)

    # One size for all three lines. Fitting each word independently would
    # let them render at different sizes, reading as three stacked labels
    # rather than as one name.
    font = ImageFont.truetype(FONT_PATH, min(largest_fitting_size(w) for w in WORDS))

    boxes = [font.getbbox(w) for w in WORDS]
    heights = [b[3] - b[1] for b in boxes]
    gap = (SIZE - 2 * MARGIN - sum(heights)) / (len(WORDS) - 1)

    y = float(MARGIN)
    for word, box, height in zip(WORDS, boxes, heights):
        # Subtract the bbox origin so lines align on their inked extents,
        # not on the font's ascent -- words of differing height would
        # otherwise sit unevenly within their slots.
        x = (SIZE - (box[2] - box[0])) / 2.0 - box[0]
        draw.text((x + 2, y - box[1] + 2), word, font=font, fill=SHADOW)
        draw.text((x, y - box[1]), word, font=font, fill=TEXT)
        y += height + gap

    img.save(OUT)
    print("wrote {} ({}x{}, font size {})".format(OUT, SIZE, SIZE, font.size))


if __name__ == "__main__":
    main()
