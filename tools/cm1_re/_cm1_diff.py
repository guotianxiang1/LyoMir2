"""Diff the native 311-entry CM dispatch map against every ident the C# side cases on."""
import re, json, os, sys

ROOT = r"D:\loym2\.claude\wt2\cm-1"
MAP = json.load(open(os.path.join(ROOT, "tools", "_cm1_map.json")))
native = {int(k): v for k, v in MAP.items()}

# ---- collect every integer constant declared anywhere in the solution ----
consts = {}
for dirpath, dirnames, filenames in os.walk(ROOT):
    if any(p in dirpath for p in (".git", "obj", "bin")):
        continue
    for fn in filenames:
        if not fn.endswith(".cs"):
            continue
        p = os.path.join(dirpath, fn)
        try:
            src = open(p, encoding="utf-8-sig", errors="ignore").read()
        except OSError:
            continue
        for m in re.finditer(
                r"const\s+(?:int|ushort|short|byte|uint)\s+(\w+)\s*=\s*(0[xX][0-9a-fA-F]+|\d+)\s*;", src):
            consts.setdefault(m.group(1), int(m.group(2), 0))

# ---- collect every `case ...:` ident the GameSvr player/user dispatch cases on ----
handled = {}
SCAN = [os.path.join(ROOT, "GameSvr")]
for base in SCAN:
    for dirpath, dirnames, filenames in os.walk(base):
        if any(p in dirpath for p in (".git", "obj", "bin")):
            continue
        for fn in filenames:
            if not fn.endswith(".cs"):
                continue
            p = os.path.join(dirpath, fn)
            src = open(p, encoding="utf-8-sig", errors="ignore").read()
            for m in re.finditer(r"case\s+(?:Grobal2\.)?([A-Za-z_]\w*)\s*:", src):
                n = m.group(1)
                if n in consts:
                    handled.setdefault(consts[n], set()).add(
                        os.path.relpath(p, ROOT) + ":" + n)
            for m in re.finditer(r"case\s+(0[xX][0-9a-fA-F]+|\d+)\s*:", src):
                handled.setdefault(int(m.group(1), 0), set()).add(
                    os.path.relpath(p, ROOT) + ":<literal>")
            # explicit `Ident == N` / `wIdent == Grobal2.CM_X` style routing
            for m in re.finditer(r"[Ii]dent\s*==\s*(?:Grobal2\.)?([A-Za-z_]\w*)", src):
                n = m.group(1)
                if n in consts:
                    handled.setdefault(consts[n], set()).add(
                        os.path.relpath(p, ROOT) + ":" + n + "(==)")

names = {}
for n, v in consts.items():
    if n.startswith("CM_"):
        names.setdefault(v, []).append(n)

missing = [op for op in sorted(native) if op not in handled]
print("native handlers: %d" % len(native))
print("missing in C#:   %d" % len(missing))
print()
q = len(missing) // 4 + (1 if len(missing) % 4 else 0)
for idx, op in enumerate(missing):
    mark = "  <== BATCH1" if idx < q else ""
    print("%5d  0x%04X  %-8s  %s%s" % (op, op, native[op],
                                       ",".join(names.get(op, ["-"])), mark))
print()
print("batch1 count = %d (indices 0..%d)" % (q, q - 1))
