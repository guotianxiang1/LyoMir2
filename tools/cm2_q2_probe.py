"""Quarter-2 (ascending 26..50 of the CM missing set) deep probe.

For each of the 25 idents this dumps the dispatch arm (bytes + disasm up to the
terminating unconditional jump) and triages every direct call target the arm
reaches, so the disposition of each leaf can be decided from evidence.
"""
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\cm-2\tools")
from cm2_arm import arm  # noqa: E402
from cm2_triage import triage, as_string  # noqa: E402
from cm2_dis import read  # noqa: E402
from capstone import Cs, CS_ARCH_X86, CS_MODE_32  # noqa: E402

MD = Cs(CS_ARCH_X86, CS_MODE_32)

# ident -> leaf VA, quarter 2 (missing[25:50]).
Q2 = [
    (1265, 0x6DA710), (1280, 0x6DA8F3), (1291, 0x6DA3CA), (1300, 0x6DAA17),
    (1301, 0x6DAA72), (1316, 0x6DAACF), (1320, 0x6DAB6A), (1350, 0x6DAC8E),
    (1351, 0x6DACA7), (1352, 0x6DACD0), (1353, 0x6DACE4), (1354, 0x6DACF6),
    (1355, 0x6DAD08), (1356, 0x6DAD21), (1357, 0x6DAD33), (1358, 0x6DAD45),
    (1359, 0x6DAD57), (1360, 0x6DAD6B), (1361, 0x6DAD7F), (1362, 0x6DAD91),
    (1363, 0x6DADA3), (1364, 0x6DADB5), (1376, 0x6DAFF3), (2815, 0x6D9B52),
    (3179, 0x6DA3F3),
]


def callees_in(va, maxlen=0x200):
    """Direct E8 call targets appearing in the arm, in order, de-duplicated."""
    data = read(va, maxlen)
    out = []
    for i in MD.disasm(data, va):
        if i.mnemonic == "call" and i.op_str.startswith("0x"):
            t = int(i.op_str, 0)
            if t not in out:
                out.append(t)
        if i.mnemonic == "jmp" and i.op_str.startswith("0x"):
            break
        if i.mnemonic == "ret":
            break
    return out


for ident, leaf in Q2:
    print("=" * 78)
    print("CM %d  leaf 0x%06X" % (ident, leaf))
    print("-" * 78)
    lines, raw = arm(leaf)
    print("\n".join(lines))
    for c in callees_in(leaf):
        size, calls, strs, gl, r = triage(c)
        print("  --> worker 0x%06X  size=%d%s" % (c, size, "" if r else " (no ret in window)"))
        uniq = []
        for cc in calls:
            k = cc if isinstance(cc, str) else "0x%06X" % cc
            if k not in uniq:
                uniq.append(k)
        if uniq:
            print("      calls  : " + ", ".join(uniq))
        if gl:
            print("      globals: " + ", ".join("0x%06X" % g for g in gl))
        for s in strs:
            print("      str    : %r" % s)
    print()
