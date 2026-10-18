"""Draw the app icon and pack it: -> src/RealtekMicControl.App/Assets/AppIcon.ico

    python art/make-appicon.py            # writes the .ico (and art/AppIcon.png, the master)
    python art/make-appicon.py --check    # fails if the committed .ico is out of date

A microphone in front of its cardioid pickup pattern. The art is drawn here rather than kept as a
painted master, so changing it means changing this file.

The .ico is the only icon the app has: <ApplicationIcon> embeds it in the exe, and the window icon
is read back out of the exe at runtime, because publish does not copy loose assets next to it.

Packing follows ForceAutoHDR.Net's script: resampling in linear light on premultiplied alpha (plain
sRGB averaging dims a light shape on a dark ground and drags transparent colour into the edge),
sizes up to 64 as BGRA bitmaps and the large ones as PNG.

Requires Pillow and numpy; neither is part of the build, this is run by hand when the art changes.
"""

import math
import struct
import sys
from io import BytesIO
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

REPO = Path(__file__).resolve().parent.parent
MASTER = REPO / 'art' / 'AppIcon.png'
ICON = REPO / 'src' / 'RealtekMicControl.App' / 'Assets' / 'AppIcon.ico'

MASTER_SIZE = 1024
SUPERSAMPLE = 4

BITMAP_SIZES = (16, 20, 24, 32, 40, 48, 64)
PNG_SIZES = (96, 128, 256)

# Everything that touches the background is a saturated blue, so the icon reads on a dark and on
# a light taskbar alike; the white parts only ever sit on that blue.
LOBE_TOP = (96, 168, 255)
LOBE_BOTTOM = (36, 104, 214)
LOBE_EDGE = (28, 84, 186)
WHITE = (255, 255, 255)
CAPSULE_BOTTOM = (214, 230, 255)
GRILLE = (52, 120, 228)


def vertical_gradient(size: int, box, top_colour, bottom_colour) -> Image.Image:
    left, top, right, bottom = box
    image = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    for y in range(int(top), int(bottom) + 1):
        f = (y - top) / max(bottom - top, 1)
        colour = tuple(round(c0 + (c1 - c0) * f) for c0, c1 in zip(top_colour, bottom_colour))
        draw.line([(left, y), (right, y)], fill=colour + (255,))
    return image


def draw_master() -> Image.Image:
    """Everything in units of the final 1024 px canvas, drawn SUPERSAMPLE times larger."""
    s = MASTER_SIZE * SUPERSAMPLE
    u = s / MASTER_SIZE
    image = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    # Cardioid r = a(1 + cos t), lobe up, cusp at the bottom centre where the stand meets it.
    a = 300 * u
    cx, cusp_y = 512 * u, 780 * u
    points = []
    for i in range(720):
        t = 2 * math.pi * i / 720
        r = a * (1 + math.cos(t))
        points.append((cx + r * math.sin(t), cusp_y - r * math.cos(t)))
    # The two halves of the lobe dip a quarter of a below the cusp, so the fill runs to their bottom.
    top_y = min(y for _, y in points)
    bottom_y = max(y for _, y in points)
    lobe_mask = Image.new('L', (s, s), 0)
    ImageDraw.Draw(lobe_mask).polygon(points, fill=255)
    image.paste(vertical_gradient(s, (0, top_y, s, bottom_y), LOBE_TOP, LOBE_BOTTOM), (0, 0), lobe_mask)
    draw.line(points + [points[0]], fill=LOBE_EDGE + (255,), width=round(16 * u), joint='curve')

    # Stand below the cusp, in the lobe's own blue.
    draw.line([(cx, cusp_y - 20 * u), (cx, 860 * u)], fill=LOBE_BOTTOM + (255,), width=round(40 * u))
    draw.rounded_rectangle([402 * u, 850 * u, 622 * u, 900 * u], radius=25 * u, fill=LOBE_BOTTOM + (255,))

    # Capsule: white to pale blue, with grille lines.
    left, top, right, bottom = 417 * u, 250 * u, 607 * u, 560 * u
    radius = (right - left) / 2
    capsule_mask = Image.new('L', (s, s), 0)
    ImageDraw.Draw(capsule_mask).rounded_rectangle([left, top, right, bottom], radius=radius, fill=255)
    image.paste(vertical_gradient(s, (left, top, right, bottom), WHITE, CAPSULE_BOTTOM), (0, 0), capsule_mask)
    for y in (360, 420, 480):
        draw.line([(left + 50 * u, y * u), (right - 50 * u, y * u)], fill=GRILLE + (255,), width=round(20 * u))

    # Holder: a white U under the capsule and a white stem down to the cusp.
    stroke = round(34 * u)
    draw.arc([352 * u, 380 * u, 672 * u, 680 * u], start=0, end=180, fill=WHITE + (255,), width=stroke)
    draw.line([(cx, 678 * u), (cx, cusp_y - 30 * u)], fill=WHITE + (255,), width=stroke)

    return image.resize((MASTER_SIZE, MASTER_SIZE), Image.LANCZOS)


def to_linear(srgb: np.ndarray) -> np.ndarray:
    return np.where(srgb <= 0.04045, srgb / 12.92, ((srgb + 0.055) / 1.055) ** 2.4)


def to_srgb(linear: np.ndarray) -> np.ndarray:
    return np.where(linear <= 0.0031308, linear * 12.92, 1.055 * linear ** (1 / 2.4) - 0.055)


def downscale(master: Image.Image, size: int) -> Image.Image:
    """Lanczos in linear light, on premultiplied alpha."""
    rgba = np.asarray(master, dtype=np.float64) / 255.0
    alpha = rgba[:, :, 3:4]
    premultiplied = np.concatenate([to_linear(rgba[:, :, :3]) * alpha, alpha], axis=2)

    resized = np.stack([
        np.asarray(Image.fromarray(premultiplied[:, :, c].astype(np.float32), 'F')
                   .resize((size, size), Image.LANCZOS), dtype=np.float64)
        for c in range(4)
    ], axis=2)

    out_alpha = np.clip(resized[:, :, 3:4], 0.0, 1.0)
    colour = np.divide(resized[:, :, :3], out_alpha, out=np.zeros_like(resized[:, :, :3]),
                       where=out_alpha > 1e-6)
    colour = to_srgb(np.clip(colour, 0.0, 1.0))

    pixels = np.round(np.concatenate([colour, out_alpha], axis=2) * 255.0).astype(np.uint8)
    return Image.fromarray(pixels, 'RGBA')


def as_bitmap(image: Image.Image) -> bytes:
    """A 32bpp BITMAPINFOHEADER DIB: bottom-up BGRA, then the legacy 1bpp AND mask."""
    width, height = image.size
    bgra = np.asarray(image)[::-1, :, [2, 1, 0, 3]]

    opaque = np.asarray(image)[::-1, :, 3] != 0
    mask = np.packbits(~opaque, axis=1)
    stride = ((width + 31) // 32) * 4
    mask = np.pad(mask, ((0, 0), (0, stride - mask.shape[1])))

    pixels = bgra.tobytes() + mask.tobytes()
    header = struct.pack('<IiiHHIIiiII', 40, width, height * 2, 1, 32, 0, len(pixels), 0, 0, 0, 0)
    return header + pixels


def build(master: Image.Image) -> bytes:
    images = []
    for size in sorted(BITMAP_SIZES + PNG_SIZES, reverse=True):
        scaled = downscale(master, size)
        if size in PNG_SIZES:
            buffer = BytesIO()
            scaled.save(buffer, 'PNG', optimize=True)
            images.append((size, buffer.getvalue()))
        else:
            images.append((size, as_bitmap(scaled)))

    offset = 6 + 16 * len(images)
    directory = b''
    for size, data in images:
        directory += struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)

    return struct.pack('<HHH', 0, 1, len(images)) + directory + b''.join(d for _, d in images)


if __name__ == '__main__':
    master = draw_master()
    icon = build(master)
    if '--check' in sys.argv:
        if not ICON.exists() or ICON.read_bytes() != icon:
            raise SystemExit(f'{ICON} is not what this script draws; rerun make-appicon.py')
        print(f'{ICON.relative_to(REPO)} is up to date')
    else:
        ICON.parent.mkdir(parents=True, exist_ok=True)
        master.save(MASTER, optimize=True)
        ICON.write_bytes(icon)
        print(f'{ICON.relative_to(REPO)}: {len(icon):,} bytes, {len(BITMAP_SIZES + PNG_SIZES)} sizes')
