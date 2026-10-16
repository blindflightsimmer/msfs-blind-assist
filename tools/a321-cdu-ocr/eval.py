"""Leave-one-page-out over every labelled page (scratchpad strings always in training)."""
import os, sys, collections
import numpy as np
from ocr2 import load_labelled, samples, Matcher, synthetic_templates

items = load_labelled()
data = samples(items)  # prints count / spacing mismatches
groups = collections.defaultdict(list)
for c, f, src in data:
    g = "SCRATCHPAD" if os.sep + "sp" + os.sep in src else os.path.basename(src)
    groups[g].append((c, f))
pages = [g for g in groups if g != "SCRATCHPAD"]
SYN = synthetic_templates()
total = wrong = unseen = 0
good_d, bad_d, margins = [], [], []
confusions = collections.Counter()
for held in pages:
    train = [x for g in groups if g != held for x in groups[g]]
    m = Matcher(train, SYN)
    known = set(m.labels)
    for c, f in groups[held]:
        if c not in known:
            unseen += 1
            print(f"  unseen-in-training '{c}' on {held}")
            continue
        p, d, mg = m.classify(f)
        total += 1
        if p == c:
            good_d.append(d); margins.append(mg)
        else:
            wrong += 1; bad_d.append(d); confusions[(c, p)] += 1
            print(f"  {held}: '{c}' read '{p}' d={d:.3f} margin={mg:.3f}")
counts = collections.Counter(c for c, _, _ in data)
print(f"\nLOPO: {total-wrong}/{total} ({100*(total-wrong)/total:.2f}%), unseen {unseen}")
print("confusions:", confusions.most_common(15))
gd = np.array(good_d)
print(f"correct d: median {np.median(gd):.3f}, 99th pct {np.percentile(gd,99):.3f}, max {gd.max():.3f}; margin min {min(margins):.3f}")
if bad_d:
    print(f"wrong d: min {min(bad_d):.3f}")
print("sample counts:", " ".join(f"{c}:{n}" for c, n in sorted(counts.items())))

