"""Resolve which CM idents the C# dispatcher actually handles.

Reads Grobal2.cs for `public const int CM_xxx = N;` then scans the switch in
TPlayObject.Message.cs (plus any other file with `case Grobal2.CM_`) for case
labels, resolving symbolic labels to numbers. Diffs against the native walk.

Writes staging/cmbe_b/csidents.txt
"""
import json
import os
import re
import sys

CSROOT = r"D:/loym2/.claude/wt2/cmbe-b"
OUTDIR = r"D:/loym2/staging/cmbe_b"

g = open(os.path.join(CSROOT, "SystemModule", "Grobal2.cs"), encoding="utf-8",
         errors="replace").read()
const = {}
for m in re.finditer(r"const\s+(?:int|ushort|short|uint)\s+(\w+)\s*=\s*(-?\d+)\s*;", g):
    const[m.group(1)] = int(m.group(2))

handled = {}
for root, dirs, files in os.walk(os.path.join(CSROOT, "GameSvr")):
    dirs[:] = [d for d in dirs if d not in ("bin", "obj")]
    for f in files:
        if not f.endswith(".cs"):
            continue
        p = os.path.join(root, f)
        txt = open(p, encoding="utf-8", errors="replace").read()
        if "Grobal2.CM_" not in txt and not re.search(r"case\s+\d{2,4}\s*:", txt):
            continue
        for m in re.finditer(r"case\s+(?:Grobal2\.)?(\w+)\s*:", txt):
            tok = m.group(1)
            if tok.isdigit():
                v = int(tok)
            elif tok in const:
                v = const[tok]
            else:
                continue
            ln = txt.count("\n", 0, m.start()) + 1
            handled.setdefault(v, []).append("%s:%d" % (os.path.relpath(p, CSROOT), ln))

census = json.load(open(os.path.join(OUTDIR, "census.json")))
native = {int(k): v for k, v in census["real"].items()}

miss = sorted(i for i in native if i not in handled)
extra = sorted(i for i in handled if i not in native and 80 <= i <= 5000)

out = ["native idents=%d ; C# case labels=%d ; native-not-in-C#=%d"
       % (len(native), len(handled), len(miss)), ""]
out.append("=== NATIVE IDENTS WITH NO C# CASE LABEL ===")
for i in miss:
    out.append("%5d 0x%04X handler %s" % (i, i, native[i]))
out += ["", "=== C# CASE LABELS NOT IN NATIVE CM TREE (80..5000) ==="]
for i in extra:
    out.append("%5d  %s" % (i, handled[i][0]))
open(os.path.join(OUTDIR, "csidents.txt"), "w", encoding="utf-8").write("\n".join(out))
json.dump({"handled": {str(k): v for k, v in handled.items()}, "missing": miss},
          open(os.path.join(OUTDIR, "csidents.json"), "w"), indent=0)
sys.stdout.reconfigure(encoding="utf-8")
print(out[0])
print("missing:", miss)
