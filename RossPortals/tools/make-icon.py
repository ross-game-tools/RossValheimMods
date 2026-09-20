#!/usr/bin/env python3
"""Regenerate the Thunderstore package icon.

Checked in alongside the PNG it produces, so the icon has readable source
instead of being an opaque committed binary. Thunderstore requires exactly
256x256 PNG and rejects anything else.

    python RossPortals/tools/make-icon.py

Every Ross mod uses this same layout so the packages read as a family in a
Thunderstore listing: a letterspaced ROSS eyebrow, a rule, then the product
name. Only ACCENT and the name lines differ between mods -- the hue is what
tells them apart at thumbnail size, where text is barely legible.

The layout code is duplicated in each mod's copy of this script rather than
shared from one place. That is deliberate: the repo's rule is that a mod can
be lifted out of it without untangling anything, and a shared generator would
break that for the sake of forty lines.

Requires Pillow. Uses Segoe UI, so it wants a Windows font directory.
"""
from PIL import Image, ImageDraw, ImageFont

# ---- per-mod configuration -------------------------------------------------
NAME_LINES = ["Portals"]
ACCENT = (80, 170, 224)
BASE = (30, 44, 68)
OUT = "RossPortals/thunderstore/icon.png"
# ---------------------------------------------------------------------------

SIZE = 256
EYEBROW = "ROSS"
TEXT = (245, 241, 232)
BOLD = r"C:\Windows\Fonts\segoeuib.ttf"
SEMI = r"C:\Windows\Fonts\seguisb.ttf"

MARGIN = 22
EYEBROW_SIZE = 26
EYEBROW_TRACKING = 6      # PIL has no letter-spacing, so glyphs are placed one by one
RULE_WIDTH = 3

# A one-word mod would otherwise fill the whole panel while a two-word mod
# sits at half that, and the pair stops looking like one family at the size
# these are actually seen. The cap keeps the wordmarks within sight of each
# other; a long name still shrinks below it to fit.
MAX_NAME_SIZE = 76


def mix(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def draw_background(draw):
    # Vertical gradient from the mod's base tone to near-black, so the accent
    # and the cream text both keep contrast wherever they land.
    bottom = mix(BASE, (8, 8, 10), 0.72)
    for y in range(SIZE):
        draw.line([(0, y), (SIZE, y)], fill=mix(BASE, bottom, y / (SIZE - 1)))

    # Two inset borders: a dark one for separation against a pale page, and an
    # accent one that carries the mod's identity right to the edge.
    draw.rectangle([6, 6, SIZE - 7, SIZE - 7], outline=mix(BASE, (0, 0, 0), 0.55), width=3)
    draw.rectangle([10, 10, SIZE - 11, SIZE - 11], outline=ACCENT, width=2)


def text_width(font, text, tracking=0):
    box = font.getbbox(text)
    return (box[2] - box[0]) + tracking * max(len(text) - 1, 0)


def draw_tracked(draw, font, text, centre_x, y, fill, tracking):
    x = centre_x - text_width(font, text, tracking) / 2.0
    for ch in text:
        box = font.getbbox(ch)
        draw.text((x - box[0], y), ch, font=font, fill=fill)
        x += (box[2] - box[0]) + tracking


def largest_fitting_size(words, max_width, max_height):
    """One size for every line: differing sizes read as unrelated labels."""
    size = 8
    while size < 220:
        font = ImageFont.truetype(BOLD, size + 1)
        if any(text_width(font, w) > max_width for w in words):
            break
        if max(font.getbbox(w)[3] - font.getbbox(w)[1] for w in words) > max_height:
            break
        size += 1
    return min(size, MAX_NAME_SIZE)


def main():
    img = Image.new("RGB", (SIZE, SIZE))
    draw = ImageDraw.Draw(img)
    draw_background(draw)

    centre = SIZE / 2.0
    eyebrow_font = ImageFont.truetype(SEMI, EYEBROW_SIZE)
    eb = eyebrow_font.getbbox(EYEBROW)
    eyebrow_y = MARGIN + 8

    draw_tracked(draw, eyebrow_font, EYEBROW, centre, eyebrow_y - eb[1], ACCENT, EYEBROW_TRACKING)

    rule_y = eyebrow_y + (eb[3] - eb[1]) + 12
    rule_half = text_width(eyebrow_font, EYEBROW, EYEBROW_TRACKING) / 2.0 + 10
    draw.rectangle([centre - rule_half, rule_y, centre + rule_half, rule_y + RULE_WIDTH],
                   fill=mix(ACCENT, BASE, 0.35))

    # The name gets whatever vertical room is left below the rule.
    top = rule_y + RULE_WIDTH + 14
    available = SIZE - MARGIN - top
    slot = available / len(NAME_LINES)
    font = ImageFont.truetype(BOLD, largest_fitting_size(NAME_LINES, SIZE - 2 * MARGIN - 12, slot * 0.86))

    boxes = [font.getbbox(w) for w in NAME_LINES]
    heights = [b[3] - b[1] for b in boxes]
    line_gap = font.size * 0.22
    block = sum(heights) + line_gap * (len(NAME_LINES) - 1)

    # Centred in the space below the rule. Distributing the lines across all
    # of it instead leaves a capped single line sitting low in the panel.
    y = top + (available - block) / 2.0
    gap = line_gap
    for word, box, height in zip(NAME_LINES, boxes, heights):
        x = centre - (box[2] - box[0]) / 2.0 - box[0]
        draw.text((x + 2, y - box[1] + 2), word, font=font, fill=(0, 0, 0))
        draw.text((x, y - box[1]), word, font=font, fill=TEXT)
        y += height + gap

    img.save(OUT)
    print(f"wrote {OUT} ({SIZE}x{SIZE}, name size {font.size})")


if __name__ == "__main__":
    main()
