import io, contextlib
from ocr2 import load_labelled, samples, learn_offsets

items = load_labelled()
off = learn_offsets(items)
print("offsets:", {c: round(v, 2) for c, v in sorted(off.items()) if abs(v) > 0.05})
buf = io.StringIO()
with contextlib.redirect_stdout(buf):
    samples(items, verbose=True)
lines = [l for l in buf.getvalue().splitlines() if l.strip()]
for l in lines:
    print(l)
print("spacing mismatches:", sum("~~" in l for l in lines), " count mismatches:", sum("!!" in l for l in lines))
