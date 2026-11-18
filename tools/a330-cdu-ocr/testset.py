"""Final check: train on every labelled page, read pages never used for training, score per line."""
import os, sys, json
from ocr2 import load_labelled, samples, Matcher, synthetic_templates, read_page, HERE

tdir = os.path.join(HERE, "big", sys.argv[1] if len(sys.argv) > 1 else "test")
truth = json.load(open(os.path.join(tdir, "truth.json"), encoding="utf-8-sig"))
data = samples(load_labelled(), verbose=False)
m = Matcher([(c, f) for c, f, _ in data], synthetic_templates())
chars = cw = lines = lw = 0
for page, rows in truth.items():
    got = read_page(os.path.join(tdir, page + ".png"), m)
    for r in range(len(got)):
        g = " ".join(got[r][0].split())
        t = " ".join(rows.get(str(r), "").split())
        if not g and not t:
            continue
        lines += 1
        gc, tc = g.replace(" ", ""), t.replace(" ", "")
        chars += max(len(gc), len(tc))
        bad = sum(1 for a, b in zip(gc, tc) if a != b) + abs(len(gc) - len(tc))
        cw += bad
        if g != t:
            lw += 1
            print(f"  {page} row {r}: truth '{t}'\n  {' ' * len(page)}        read  '{g}'")
print(f"\nunseen pages: chars {chars - cw}/{chars} correct ({100 * (chars - cw) / max(chars, 1):.2f}%), "
      f"lines exactly right incl. word breaks {lines - lw}/{lines}")

