#!/usr/bin/env python3
"""Regenerate the Thunderstore package icon.

Checked in alongside the PNG it produces, so the icon has readable
source instead of being an opaque committed binary. Run it when the
wordmark or palette changes -- Thunderstore requires exactly 256x256
PNG and rejects anything else.

    python RossQoL/tools/make-icon.py

Requires Pillow. Uses Segoe UI Bold, so it wants a Windows font directory.
"""
from PIL import Image, ImageDraw, ImageFont

SIZE = 256
WORDS = ["Ross", "QoL"]
FONT_PATH = r"C:\Windows\Fonts\segoeuib.ttf"
OUT = "RossQoL/thunderstore/icon.png"

# Teal-to-indigo backdrop with cream text; kept from the PortalTames icon.
BG_TOP, BG_BOTTOM = (34, 74, 96), (24, 30, 62)
TEXT, SHADOW = (240, 246, 250), (10, 10, 16)
RING = (150, 220, 235)

MAX_WIDTH = SIZE - 64      # widest word sets the size
SLOT = (SIZE - 72) / 2.0   # vertical room one line may occupy
MARGIN = 40


def largest_fitting_size(word):
    size = 8
    while size < 220:
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

    # A ring behind the wordmark.
    cx, cy, r = SIZE / 2.0, SIZE / 2.0, SIZE / 2.0 - 14
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], outline=RING, width=6)
    draw.ellipse([cx - r + 10, cy - r + 10, cx + r - 10, cy + r - 10],
                 outline=(90, 150, 168), width=2)

    # One size for both lines, so they read as one name rather than two
    # differently sized labels stacked on top of each other.
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
