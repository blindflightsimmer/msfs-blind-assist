"""Read every capture in a folder and print the non-empty lines (word breaks collapsed)."""
import os, sys
from ocr2 import load_labelled, samples, Matcher, synthetic_templates, read_page

m = Matcher([(c, f) for c, f, _ in samples(load_labelled(), verbose=False)], synthetic_templates())
d = sys.argv[1]
for p in sorted(os.listdir(d)):
    if not p.endswith(".png"):
        continue
    print("=====", p)
    for r, (t, _) in enumerate(read_page(os.path.join(d, p), m)):
        if t.strip():
            print(f"{r:2d}: {' '.join(t.split())}")
