import sys, os
from ocr2 import load_labelled, samples, Matcher, read_page, HERE

items = load_labelled()
data = samples(items, verbose=False)
m = Matcher([(c, f) for c, f, _ in data])
for p in sys.argv[1:]:
    print("=====", p)
    for r, (s, fl) in enumerate(read_page(os.path.join(HERE, "big", p + ".png"), m)):
        if s:
            print(f"{r:2d}|{s}|")
