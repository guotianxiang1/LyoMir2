import re, json, glob, os, collections

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ROOT = os.path.join(REPO, "GameSvr", "ScriptSystem", "PasEngine")


def methods_and_cases(path):
    lines = open(path, encoding="utf-8").read().splitlines()
    res = collections.defaultdict(set)
    stack = []
    cur = None
    depth = 0
    for i, L in enumerate(lines):
        m = re.match(r"\s*(?:public|private|internal|protected|static|\s)+[\w<>\[\],\? ]+\s+(\w+)\s*\(", L)
        if m and "=" not in L.split("(")[0] and depth <= 2:
            cur = m.group(1)
        for cm in re.finditer(r'case\s+"([^"]*)"', L):
            res[cur or "?"].add(cm.group(1))
        depth += L.count("{") - L.count("}")
    return res


allc = {}
for p in glob.glob(os.path.join(ROOT, "Pas*.cs")):
    r = methods_and_cases(p)
    for k, v in r.items():
        allc.setdefault(os.path.basename(p) + "::" + k, set()).update(v)

for k in sorted(allc, key=lambda k: -len(allc[k])):
    if len(allc[k]) >= 5:
        print("%-58s %4d" % (k, len(allc[k])))

json.dump({k: sorted(v) for k, v in allc.items()}, open("_cs_cases2.json", "w", encoding="utf-8"),
          ensure_ascii=False, indent=1)
print("wrote _cs_cases2.json")
