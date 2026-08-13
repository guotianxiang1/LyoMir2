import re, json, collections

t = open("_reg_walk.txt", encoding="utf-8").read().splitlines()
blocks = []
cur_block = None
cur_class = None
recs = []
for L in t:
    if L.startswith("# BLOCK"):
        cur_class = "(global)"
        continue
    m = re.match(r"=== AddClassN\s+@(\w+)\s+name='(\w+)'", L)
    if m:
        cur_class = m.group(2)
        continue
    m = re.match(r"^  ([MPGV]) ([0-9A-F]{8})  (.*)$", L)
    if not m:
        continue
    kind, va, rest = m.group(1), m.group(2), m.group(3)
    if kind == "M":
        # Delphi declaration strings are not consistently cased: TPsNpc's
        # GetCelebName (0x73495C) starts with a capital `Function`. A
        # case-sensitive keyword match parses the whole decl as the name and
        # reports the API as MISSING.
        mm = re.match(r"\s*(function|procedure|constructor|destructor)\s+([A-Za-z_]\w*)",
                      rest, re.IGNORECASE)
        name = mm.group(2) if mm else rest.strip()
        recs.append(dict(kind="method", cls=cur_class, va=va, name=name, decl=rest.strip()))
    elif kind == "P":
        mm = re.match(r"(\S+)\s+:\s+(\S+)\s+acc=(\S+)", rest)
        recs.append(dict(kind="prop", cls=cur_class, va=va,
                         name=mm.group(1) if mm else rest.split()[0],
                         type=mm.group(2) if mm else "?",
                         acc=mm.group(3) if mm else "?", decl=rest.strip()))
    elif kind == "G":
        mm = re.match(r"ptr=(\w+)\s+(.*)$", rest)
        decl = mm.group(2) if mm else rest
        nn = re.match(r"\s*(function|procedure)\s+([A-Za-z_]\w*)", decl)
        recs.append(dict(kind="global", cls="(global)", va=va, ptr=mm.group(1) if mm else "",
                         name=nn.group(2) if nn else decl, decl=decl.strip()))

json.dump(recs, open("_native_registry.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
c = collections.Counter((r["cls"], r["kind"]) for r in recs)
for k, v in sorted(c.items()):
    print(k, v)
print("TOTAL", len(recs))

with open("_native_registry.txt", "w", encoding="utf-8") as f:
    for cls in ["(global)", "TOBJECT", "TCreature", "TBaseGroup", "TPlayer", "TBaseItem", "TMySQLDB", "TPsNpc", "TAnimal"]:
        for kind in ("global", "method", "prop"):
            sub = [r for r in recs if r["cls"] == cls and r["kind"] == kind]
            if not sub:
                continue
            f.write("\n### %s / %s  (%d)\n" % (cls, kind, len(sub)))
            for r in sub:
                f.write("%s  %-34s %s\n" % (r["va"], r["name"], r["decl"]))
print("wrote _native_registry.txt / .json")
