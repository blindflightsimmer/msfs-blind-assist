"""Glyph segmentation for the A330 MCDU pop-out (adapted from the iniBuilds A300 trainer; grid measured 2026-10-16): rows are on a fixed vertical grid, but text can be
placed off the 24-column grid horizontally (centred titles), so glyphs are found by ink runs."""
import numpy as np
from PIL import Image

TITLE_H = 31
ROWS = 14
BASE_Y0, BASE_PY, BASE_PX = 30.2, 23.8, 15.0
Y0, PITCH_Y, PITCH_X = BASE_Y0, BASE_PY, BASE_PX
THR = 40   # the A330 draws unavailable options in GREY (peak 77, background ~15)
SCALE = 1.0


def load(path):
    global Y0, PITCH_Y, PITCH_X, SCALE
    im = np.asarray(Image.open(path).convert("RGB")).astype(float)
    SCALE = im.shape[1] / 450.0
    Y0, PITCH_Y, PITCH_X = BASE_Y0 * SCALE, BASE_PY * SCALE, BASE_PX * SCALE
    im = im[TITLE_H:]
    return im, im.max(axis=2)


def runs(mask1d):
    out, s = [], None
    for i, v in enumerate(mask1d):
        if v and s is None:
            s = i
        elif not v and s is not None:
            out.append((s, i - 1)); s = None
    if s is not None:
        out.append((s, len(mask1d) - 1))
    return out


def glyphs(im, lum):
    """Yield per row: list of glyph dicts (x0,x1,y0,y1 absolute in cropped image, crop lum, rgb)."""
    rows = [[] for _ in range(ROWS)]
    # text lines come from the image's own row projection, then snap to the nearest grid row
    lines = []
    for a, b in runs((lum > THR).any(axis=1)):
        if b - a + 1 < 2:
            continue
        lines.append((a, b))
    for a, b in lines:
        r = int(round(((a + b) / 2 - Y0) / PITCH_Y))
        if not 0 <= r < ROWS:
            continue
        y0, y1 = a, b + 1
        band = lum[y0:y1]
        on = band > THR
        segs = runs(on.any(axis=0))
        out = []
        for a, b in segs:
            w = b - a + 1
            # split over-wide runs (touching glyphs) by pitch
            n = max(1, int(round(w / PITCH_X))) if w > PITCH_X * 1.35 else 1
            for k in range(n):
                xa = a + int(round(k * w / n)); xb = a + int(round((k + 1) * w / n)) - 1
                sub = on[:, xa:xb + 1]
                ys = np.nonzero(sub.any(axis=1))[0]
                if len(ys) == 0:
                    continue
                ga, gb = y0 + ys[0], y0 + ys[-1]
                crop = lum[ga:gb + 1, xa:xb + 1]
                m = crop > THR
                rgb = tuple(float(im[ga:gb + 1, xa:xb + 1, c][m].mean()) for c in range(3))
                out.append({"x0": xa, "x1": xb, "y0": ga, "y1": gb, "crop": crop, "rgb": rgb,
                            "line_y0": y0, "line_y1": y1 - 1})
        rows[r].extend(out)
    for r in range(ROWS):
        rows[r].sort(key=lambda g: g["x0"])
    return rows


def layout(row_glyphs):
    """Map glyph centres to column indices so spaces can be inserted. Phase from the glyphs themselves."""
    if not row_glyphs:
        return []
    cx = np.array([(g["x0"] + g["x1"]) / 2 for g in row_glyphs])
    ph = np.angle(np.mean(np.exp(2j * np.pi * cx / PITCH_X))) / (2 * np.pi) * PITCH_X
    return [int(round((x - ph) / PITCH_X)) for x in cx]


if __name__ == "__main__":
    import sys
    im, lum = load(sys.argv[1])
    for r, gs in enumerate(glyphs(im, lum)):
        cols = layout(gs)
        print(r, len(gs), cols[:30])
