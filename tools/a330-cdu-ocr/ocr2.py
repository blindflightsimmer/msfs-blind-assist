"""A330 MCDU reader (the iniBuilds A300 trainer, adapted): segmentation (seg.py) + learned native-pixel templates + gap-based spacing."""
import os, sys, json
import numpy as np
import seg

HERE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data")
SHIFTS = [(dy, dx) for dy in (-1, 0, 1) for dx in (-1, 0, 1)]


BASE_EM = 15.0  # native advance of the large font
BASE_CAP = 18.3  # native letter height of the large font (A330: 55 px rows at 3x)
FS = 1.5  # feature scale relative to the native 450-px CDU (captures are taken at 3x)


def canvas_shape():
    return (int(round(26 * FS)), int(round(19 * FS)))   # taller than the A300 canvas: bigger font


def annotate(gs):
    """Per-glyph text size ('em' = character advance in px) and word baseline/top, so glyphs of every
    font size can be compared at one normalised size. Monospaced font: advance == size."""
    n = len(gs)
    if not n:
        return
    cx = [(g["x0"] + g["x1"]) / 2 for g in gs]
    hs = [g["y1"] - g["y0"] + 1 for g in gs]
    tall_row = max(hs)
    # 1. advance from a genuinely adjacent neighbour (never across a space)
    em0 = []
    for i in range(n):
        cands = []
        for j in (i - 1, i + 1):
            if 0 <= j < n:
                adv = abs(cx[j] - cx[i])
                full = hs[i] >= 0.7 * seg.PITCH_X
                if adv < 1.6 * seg.PITCH_X and (not full or adv <= 1.4 * hs[i]):
                    cands.append(adv)
        em0.append(min(cands) if cands else None)
    known = [e for e in em0 if e]
    row_em = float(np.median(known)) if known else seg.PITCH_X
    for i in range(n):
        if em0[i] is None:
            em0[i] = 0.95 * hs[i] if hs[i] >= 0.7 * seg.PITCH_X else row_em
    # 2. words = chains of adjacent glyphs
    words, cur = [], [0]
    for i in range(1, n):
        if cx[i] - cx[i - 1] < 1.5 * min(em0[i], em0[i - 1]):
            cur.append(i)
        else:
            words.append(cur); cur = [i]
    words.append(cur)
    # 3. per-word letter height ('cap') from full-height glyphs; shapes are normalised by it
    for w in words:
        wh = max(hs[i] for i in w)
        wem = float(np.median([em0[i] for i in w]))
        has_full = wh >= 0.6 * wem
        if has_full:
            ref = [j for j in w if hs[j] >= 0.75 * wh]
            cap = float(np.median([hs[j] for j in ref]))
            wbot = float(np.median([gs[j]["y1"] for j in ref]))
        else:
            cap, wbot = 1.13 * wem, None
        for i in w:
            gs[i].update(em=em0[i], cap=cap, wbot=wbot, wtall=has_full,
                         full=has_full and hs[i] >= 0.75 * wh)
        # a run of >=2 adjacent equal-height letters of another size inside the word (V APP, SID from)
        # gets its own letter height
        if has_full:
            k = 0
            while k < len(w):
                h0 = hs[w[k]]
                j = k
                while j + 1 < len(w) and abs(hs[w[j + 1]] - h0) <= 0.08 * h0:
                    j += 1
                run = w[k:j + 1]
                if (len(run) >= 2 and h0 >= 0.55 * wem and abs(h0 - cap) > 0.12 * cap):
                    rc = float(np.median([hs[i] for i in run]))
                    rb = float(np.median([gs[i]["y1"] for i in run]))
                    for i in run:
                        gs[i].update(cap=rc, wbot=rb, full=True)
                k = j + 1


def size_class(cap):
    c = cap / seg.SCALE  # native px
    return "L" if c >= 0.925 * BASE_CAP else ("M" if c >= 0.798 * BASE_CAP else ("C" if c >= 0.721 * BASE_CAP else "S"))


def feat(g, r):
    from PIL import Image
    H, W = canvas_shape()
    crop = np.clip(g["crop"], 0, 255).astype(float)
    mx = crop.max()
    crop = crop / mx if mx > 0 else crop          # colour/brightness independent
    cap = g.get("cap") or BASE_CAP * seg.SCALE
    k = FS * BASE_CAP / cap                         # normalise every font size to one letter height
    if abs(k - 1) > 1e-3:
        h0, w0 = crop.shape
        nh, nw = max(1, int(round(h0 * k))), max(1, int(round(w0 * k)))
        crop = np.asarray(Image.fromarray((crop * 255).astype(np.uint8)).resize((nw, nh), Image.BOX)).astype(float) / 255.0
    can = np.zeros((H, W))
    h, w = min(crop.shape[0], H - 2), min(crop.shape[1], W - 2)
    can[1:1 + h, 1:1 + w] = crop[:h, :w]
    u = BASE_CAP / cap                              # capture px -> normalised native px
    if g.get("wbot") is not None:
        ref = g["wbot"]
    else:
        ref = seg.Y0 + r * seg.PITCH_Y + 0.5 * cap    # approximate baseline from the grid row
    return {"c": can, "geo": np.array([(g["x1"] - g["x0"] + 1) * u, (g["y1"] - g["y0"] + 1) * u,
                                       (g["y0"] - ref) * u, (g["y1"] - ref) * u]),
            "size": size_class(cap)}


SYNTH_PENALTY = 0.06  # rendered-font templates only win where no real sample is closer


def synthetic_templates():
    """The A300 trainer rendered extra templates from the aircraft's font files; these MCDUs' fonts are
    inside their encrypted packages, so the libraries are built from real samples only."""
    return []


class Matcher:
    def __init__(self, items, synth=None):
        items = list(items)
        pen = [0.0] * len(items)
        if synth:
            items += synth
            pen += [SYNTH_PENALTY] * len(synth)
        self.pen = np.array(pen, dtype=np.float32)
        self.labels = [c for c, _ in items]
        self.C = np.stack([f["c"] for _, f in items]).astype(np.float32)
        self.G = np.stack([f["geo"] for _, f in items]).astype(np.float32)
        self.shifted = [np.roll(np.roll(self.C, dy, 1), dx, 2) for dy, dx in SHIFTS]

    def dists(self, f):
        A = f["c"][None].astype(np.float32)
        g = np.abs(self.G - f["geo"][None])
        geo = 0.04 * (g[:, 0] + g[:, 1]) + 0.02 * (g[:, 2] + g[:, 3])
        cand = np.nonzero(g[:, 1] <= 3.5)[0]          # only templates of a similar height
        if len(cand) == 0:
            cand = np.arange(len(self.labels))
        best = None
        for B in self.shifted:
            Bc = B[cand]
            d = np.abs(A - Bc).sum(axis=(1, 2)) / np.maximum(np.maximum(A, Bc).sum(axis=(1, 2)), 1e-6)
            best = d if best is None else np.minimum(best, d)
        out = np.full(len(self.labels), 9.0, dtype=np.float32)
        out[cand] = best + geo[cand] + self.pen[cand]
        return out

    def classify(self, f):
        d = self.dists(f)
        order = np.argsort(d)
        best = self.labels[order[0]]
        second = next((d[i] for i in order if self.labels[i] != best), 9.0)
        return best, float(d[order[0]]), float(second - d[order[0]])


OFFSETS = {}  # char -> centre offset within its cell, in advances (learned from the scratchpad samples)
OFFSETS_PATH = os.path.join(HERE, "big", "offsets.json")
if os.path.exists(OFFSETS_PATH):
    OFFSETS.update(json.load(open(OFFSETS_PATH, encoding="utf-8")))


def cell_centre(g, c):
    em = g.get("em", seg.PITCH_X)
    return (g["x0"] + g["x1"]) / 2 - OFFSETS.get(c, 0.0) * em


def _an(c):
    return c.isascii() and c.isalnum()


def spaces_between(a, b, ca_ch, cb_ch):
    em = min(a.get("em", seg.PITCH_X), b.get("em", seg.PITCH_X))
    n = max(0, int(round((cell_centre(b, cb_ch) - cell_centre(a, ca_ch)) / em)) - 1)
    if n and b["x0"] - a["x1"] < 0.55 * em:   # glyphs nearly touching: never a space between them
        n = 0
    # a change of letter size between two letters/digits is always a word break (SID from EGNT)
    if n == 0 and _an(ca_ch) and _an(cb_ch):
        ha, hb = a["y1"] - a["y0"] + 1, b["y1"] - b["y0"] + 1
        if abs(ha - hb) > 0.12 * max(ha, hb):
            n = 1
    return n


def learn_offsets(items):
    """Scratchpad rows are 22 contiguous cells: fit the cell grid on the letters/digits and record where
    every character's glyph centre sits inside its cell."""
    per = {}
    for src, r, text in items:
        if os.sep + "sp" + os.sep not in src:
            continue
        im, lum = seg.load(src)
        gs = seg.glyphs(im, lum)[r]
        if len(gs) != len(text.replace(" ", "")) or " " in text:
            continue
        ks = [k for k, c in enumerate(text) if _an(c)]
        xs = [(gs[k]["x0"] + gs[k]["x1"]) / 2 for k in ks]
        p, a = np.polyfit(ks, xs, 1)
        for k, c in enumerate(text):
            per.setdefault(c, []).append(((gs[k]["x0"] + gs[k]["x1"]) / 2 - (a + p * k)) / p)
    off = {c: float(np.median(v)) for c, v in per.items()}
    # untypeable symbols: from labelled pages, against an adjacent letter/digit (offset ~0) with no space
    per2 = {}
    for src, r, text in items:
        if os.sep + "sp" + os.sep in src:
            continue
        im, lum = seg.load(src)
        gs = seg.glyphs(im, lum)[r]
        chars = [c for c in text if c != " "]
        if len(gs) != len(chars):
            continue
        annotate(gs)
        # positions of each char in the spaced truth, to know which neighbours are adjacent
        idx, k = [], 0
        for pos, c in enumerate(text):
            if c != " ":
                idx.append(pos)
        for i, c in enumerate(chars):
            if c in off:
                continue
            for j, sgn in ((i + 1, 1), (i - 1, -1)):
                if 0 <= j < len(chars) and abs(idx[j] - idx[i]) == 1 and _an(chars[j]):
                    ha, hb = gs[i]["y1"] - gs[i]["y0"], gs[j]["y1"] - gs[j]["y0"]
                    em = gs[j]["em"]
                    cc_n = (gs[j]["x0"] + gs[j]["x1"]) / 2 - off.get(chars[j], 0.0) * em
                    cx = (gs[i]["x0"] + gs[i]["x1"]) / 2
                    per2.setdefault(c, []).append(sgn * (1 - sgn * (cc_n - cx) / em) if sgn > 0 else (cx - cc_n) / em - 1)
    for c, v in per2.items():
        off[c] = float(np.median(v))
    OFFSETS.clear(); OFFSETS.update(off)
    json.dump(off, open(OFFSETS_PATH, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return off


def read_page(path, matcher, flag_d=None, flag_margin=None):
    im, lum = seg.load(path)
    rows = seg.glyphs(im, lum)
    out = []
    for r, gs in enumerate(rows):
        annotate(gs)
        s, flags, prev = "", [], None
        for i, g in enumerate(gs):
            c, d, m = matcher.classify(feat(g, r))
            g["char"], g["dist"], g["margin"] = c, d, m
            if i:
                s += " " * spaces_between(gs[i - 1], g, prev, c)
            if (flag_d is not None and d > flag_d) or (flag_margin is not None and m < flag_margin):
                flags.append((len(s), c, round(d, 3), round(m, 3)))
            s += c
            prev = c
        out.append((s, flags))
    return out


# ---------- labelled data ----------
def load_labelled():
    """Returns list of (source, row, text) with source = image path."""
    items = []
    tj = os.path.join(HERE, "big", "truth.json")
    if os.path.exists(tj):
        with open(tj, encoding="utf-8") as fh:
            for page, rows in json.load(fh).items():
                for r, text in rows.items():
                    items.append((os.path.join(HERE, "big", page + ".png"), int(r), text))
    lab = os.path.join(HERE, "big", "sp", "labels.tsv")
    if os.path.exists(lab):
        for line in open(lab, encoding="utf-8"):
            n, txt = line.rstrip("\n").split("\t")
            items.append((os.path.join(HERE, "big", "sp", n + ".png"), 13, txt))
    return items


def samples(items, verbose=True):
    """Expand labelled rows into (char, feat, source) per glyph; checks counts and spacing."""
    out, by_src = [], {}
    cache = {}
    for src, r, text in items:
        if src not in cache:
            im, lum = seg.load(src)
            cache[src] = seg.glyphs(im, lum)
            for row in cache[src]:
                annotate(row)
        gs = cache[src][r]
        chars = [c for c in text if c != " "]
        if len(gs) != len(chars):
            if verbose:
                print(f"  !! {os.path.basename(src)} row {r}: {len(gs)} glyphs vs {len(chars)} chars '{text}'")
            continue
        # spacing check
        recon = ""
        for i, g in enumerate(gs):
            if i:
                recon += " " * spaces_between(gs[i - 1], g, chars[i - 1], chars[i])
            recon += chars[i]
        if " ".join(recon.split()) != " ".join(text.split()) and verbose:
            print(f"  ~~ spacing {os.path.basename(src)} row {r}: truth '{text}' got '{recon}'")
        for g, c in zip(gs, chars):
            out.append((c, feat(g, r), src))
    return out


if __name__ == "__main__":
    items = load_labelled()
    data = samples(items)
    m = Matcher([(c, f) for c, f, _ in data])
    for p in sys.argv[1:]:
        print("=====", os.path.basename(p))
        for r, (s, flags) in enumerate(read_page(p, m, flag_margin=0.05)):
            if s:
                print(f"{r:2d}|{s}|" + (f"  ?{flags}" if flags else ""))





