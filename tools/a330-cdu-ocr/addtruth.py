import json, os, sys
HERE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data")
p = os.path.join(HERE, "big", "truth.json")
t = json.load(open(p, encoding="utf-8"))
new = json.load(open(sys.argv[1], encoding="utf-8-sig"))
for k, v in new.items():
    t[k] = {str(r): s for r, s in v.items()}
json.dump(t, open(p, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(len(t), "pages labelled")

