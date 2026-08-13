"""Collect the opcode set already reachable in the C# client-message path.

Resolves `Grobal2` int constants, then harvests every `case <const-or-literal>:`
appearing in the files that participate in TPlayObject's CM dispatch chain.
"""
import os
import re
import sys

ROOT = r"D:\loym2\.claude\wt2\cm-2"

CONST_RE = re.compile(r"public\s+const\s+int\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(-?\w+)\s*;")
CASE_RE = re.compile(r"^\s*case\s+([^:]+?)\s*:", re.M)

# Every file on the CM path from TPlayObject.Operate/Message down to the
# sub-protocol helpers and the TBaseObject/HeroObject fallbacks.
FILES = [
    r"GameSvr\Players\TPlayObject.Message.cs",
    r"GameSvr\Players\TPlayObject.Operate.cs",
    r"GameSvr\Players\TPlayObject.Attack.cs",
    r"GameSvr\Players\TPlayObject.cs",
    r"GameSvr\Players\TPlayObject.NativeChannelProtocol.cs",
    r"GameSvr\Players\TPlayObject.NativeCorpsProtocol.cs",
    r"GameSvr\Players\TPlayObject.NativeGroupProtocol.cs",
    r"GameSvr\Players\TPlayObject.NativeGuildCoreProtocol.cs",
    r"GameSvr\Players\TPlayObject.NativeGuildRelationTailProtocol.cs",
    r"GameSvr\Players\TPlayObject.NativeRelationProtocol.cs",
    r"GameSvr\Actors\TBaseObject.Base.cs",
    r"GameSvr\Actors\HeroObject.cs",
]


def consts():
    m = {}
    for dirpath, _, names in os.walk(os.path.join(ROOT, "SystemModule")):
        for n in names:
            if not n.endswith(".cs"):
                continue
            txt = open(os.path.join(dirpath, n), encoding="utf-8", errors="replace").read()
            for name, val in CONST_RE.findall(txt):
                try:
                    m[name] = int(val, 0)
                except ValueError:
                    pass
    return m


def covered():
    cm = consts()
    seen = {}
    for rel in FILES:
        p = os.path.join(ROOT, rel)
        if not os.path.exists(p):
            continue
        txt = open(p, encoding="utf-8", errors="replace").read()
        for raw in CASE_RE.findall(txt):
            expr = raw.strip()
            val = None
            if expr.startswith("Grobal2."):
                val = cm.get(expr.split(".", 1)[1])
            else:
                try:
                    val = int(expr, 0)
                except ValueError:
                    val = cm.get(expr)
            if val is not None:
                seen.setdefault(val, []).append(rel)
    return seen


if __name__ == "__main__":
    c = covered()
    print("# C#-covered opcodes: %d" % len(c))
    for k in sorted(c):
        print("%5d  %s" % (k, os.path.basename(c[k][0])))
