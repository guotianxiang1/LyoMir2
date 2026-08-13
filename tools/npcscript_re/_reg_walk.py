from _dis import *
import io, collections

HELPERS = {
    0x510F00: "RegisterMethod",
    0x510FFC: "RegisterProperty",
    0x50F0E4: "AddClassN",
    0x50F1C4: "FindClass",
    0x513A7C: "AddFunction",
    0x514170: "SetVarToInstance",
}

# known-good call-site set to validate alignment
known = set()
for off in range(0, len(DATA) - 5):
    if DATA[off] != 0xE8:
        continue
    rel = int.from_bytes(DATA[off + 1:off + 5], "little", signed=True)
    va = BASE + off
    if ((va + 5 + rel) & 0xFFFFFFFF) in HELPERS:
        known.add(va)


def try_start(start, end):
    """disassemble linearly; return (n_hit, n_missed) of known call sites"""
    code = rd(start, end - start)
    seen = set()
    for ins in md.disasm(code, start):
        if ins.mnemonic == "call":
            seen.add(ins.address)
    inrange = {v for v in known if start <= v < end}
    return len(seen & inrange), len(inrange - seen)


RANGES = [(0x72A900, 0x72BE10), (0x729700, 0x729AE0), (0x734570, 0x7350C0), (0x73AEA0, 0x73AF60)]

out = io.StringIO()
for lo, hi in RANGES:
    best = None
    for s in range(lo - 0x60, lo + 0x60):
        h, m = try_start(s, hi)
        if best is None or (h, -m) > (best[1], -best[2]):
            best = (s, h, m)
    start, h, m = best
    out.write("\n" + "#" * 100 + "\n# BLOCK start=%08X end=%08X  hit=%d missed=%d\n" % (start, hi, h, m) + "#" * 100 + "\n")

    code = rd(start, hi - start)
    imm = {}
    lastpush = None
    lastcall_result = None
    for ins in md.disasm(code, start):
        mn, o = ins.mnemonic, ins.op_str
        if mn == "mov" and ", 0x" in o:
            d, s2 = o.split(", ", 1)
            try:
                imm[d] = int(s2, 16)
            except ValueError:
                pass
        elif mn == "mov" and o == "ebx, eax":
            out.write("      >>> ebx <- %s\n" % (lastcall_result or "?"))
        elif mn == "push":
            try:
                lastpush = int(o, 16) if o.startswith("0x") else int(o)
            except ValueError:
                pass
        elif mn == "call" and o.startswith("0x"):
            t = int(o, 16)
            if t in HELPERS:
                nm = HELPERS[t]
                dv = imm.get("edx"); cv = imm.get("ecx")
                ds = dstr(dv) if dv else None
                cs = dstr(cv) if cv else None
                dt = ds.decode("gbk", "replace") if ds else ("<%08X>" % dv if dv else "-")
                ct = cs.decode("gbk", "replace") if cs else ("<%08X>" % cv if cv else "-")
                if nm == "AddClassN":
                    out.write("\n=== AddClassN  @%08X   name='%s'  inherits=%s\n" % (ins.address, ct, dt))
                    lastcall_result = "class '%s'" % ct
                elif nm == "FindClass":
                    out.write("\n=== FindClass  @%08X   '%s'\n" % (ins.address, dt))
                    lastcall_result = "class '%s'" % dt
                elif nm == "RegisterMethod":
                    out.write("  M %08X  %s\n" % (ins.address, dt))
                elif nm == "RegisterProperty":
                    out.write("  P %08X  %-30s : %-14s acc=%s\n" % (ins.address, dt, ct, lastpush))
                elif nm == "AddFunction":
                    out.write("  G %08X  ptr=%08X  %s\n" % (ins.address, dv or 0, ct))
                elif nm == "SetVarToInstance":
                    out.write("  V %08X  var='%s'\n" % (ins.address, dt))
                imm.pop("edx", None); imm.pop("ecx", None)
            else:
                lastcall_result = "call %08X" % t

open("_reg_walk.txt", "w", encoding="utf-8").write(out.getvalue())
print(out.getvalue()[:5000])
print("\n... full output in _reg_walk.txt")
