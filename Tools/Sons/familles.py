# Résume un ou plusieurs dossiers par familles de noms (le nom sans son numéro de variante).
# Usage : familles.py catalogue.csv "motif1" ["motif2" ...]
import csv
import re
import sys
from collections import OrderedDict

cat = sys.argv[1]
pats = [a.lower() for a in sys.argv[2:]]
rows = list(csv.DictReader(open(cat, encoding="utf-8")))
sel = [r for r in rows if any(p in r["path"].lower() for p in pats)]
fam = OrderedDict()
for r in sel:
    base = re.sub(r"\.(wav|mp3|ogg)$", "", r["name"], flags=re.I)
    base = re.sub(r"[ _-]*\(?\d+\)?(v\d)?$", "", base)
    key = (r["pack"][:10] + "/" + r["category"][-38:], base)
    fam.setdefault(key, []).append(r)
last = None
for (c, base), rs in fam.items():
    if c != last:
        print(f"\n## {c}")
        last = c
    d = [float(x["dur"]) for x in rs]
    L = [float(x["len40"]) for x in rs]
    on = [int(x["onsets"]) for x in rs]
    cen = [int(x["centroid"]) for x in rs]
    print(f"{len(rs):3d}  {base[:62]:62} d={min(d):.2f}-{max(d):.2f} L~{sum(L)/len(L):.2f} on~{sum(on)/len(on):.1f} cen~{int(sum(cen)/len(cen))}")
print(f"\n{len(sel)} sons, {len(fam)} familles")
