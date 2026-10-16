"""Export the trained glyph library for the C# reader.

Format (little-endian):
  magic 'A3CG', u16 version=1, u16 H, u16 W, u32 count
  per template: u8 labelLen, label UTF-8, f32 penalty, 4 x f32 geo, H*W x u8 canvas (0..255)
  then: u16 offsetCount, per offset: u8 labelLen, label, f32 offset
Real samples are de-duplicated per label (a template within DEDUP of one already kept is dropped).
"""
import os, struct, sys
import numpy as np
from ocr2 import load_labelled, samples, synthetic_templates, Matcher, OFFSETS, canvas_shape, learn_offsets, SYNTH_PENALTY

DEDUP = 0.02
items = load_labelled()
learn_offsets(items)
data = samples(items, verbose=False)
kept = []  # (label, feat)
by_label = {}
for c, f, _ in data:
    lst = by_label.setdefault(c, [])
    if lst:
        m = Matcher(lst)
        if float(m.dists(f).min()) < DEDUP:
            continue
    lst.append((c, f))
for c, lst in by_label.items():
    kept += [(c, f, 0.0) for _, f in lst]
syn = synthetic_templates()
kept += [(c, f, SYNTH_PENALTY) for c, f in syn]
H, W = canvas_shape()
out = sys.argv[1]
with open(out, "wb") as fh:
    fh.write(b"A3CG"); fh.write(struct.pack("<HHHI", 1, H, W, len(kept)))
    for c, f, pen in kept:
        lab = c.encode("utf-8")
        fh.write(struct.pack("<B", len(lab))); fh.write(lab)
        fh.write(struct.pack("<f", pen)); fh.write(struct.pack("<4f", *[float(x) for x in f["geo"]]))
        fh.write(np.clip(np.round(f["c"] * 255), 0, 255).astype(np.uint8).tobytes())
    fh.write(struct.pack("<H", len(OFFSETS)))
    for c, v in OFFSETS.items():
        lab = c.encode("utf-8")
        fh.write(struct.pack("<B", len(lab))); fh.write(lab); fh.write(struct.pack("<f", v))
print(f"real {sum(len(v) for v in by_label.values())} (from {len(data)}), synthetic {len(syn)}, canvas {H}x{W}, bytes {os.path.getsize(out)}")
