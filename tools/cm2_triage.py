"""Triage a callee: size, call targets, and referenced Delphi string literals.

Delphi 7 puts constant AnsiStrings in the data image as
    [A-8]=refcount(-1)  [A-4]=length  A..A+len-1=chars  A+len=NUL
so any immediate that satisfies that shape is reported as a string.
"""
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\cm-2\tools")
from cm2_dis import read, img, BASE  # noqa: E402
from capstone import Cs, CS_ARCH_X86, CS_MODE_32  # noqa: E402

MD = Cs(CS_ARCH_X86, CS_MODE_32)


def u32(va):
    return int.from_bytes(read(va, 4), "little")


def as_string(va):
    if va < BASE + 0x1000 or va - BASE + 4 > len(img()):
        return None
    try:
        rc = u32(va - 8)
        ln = u32(va - 4)
    except Exception:
        return None
    if rc not in (0xFFFFFFFF, 1) or not (0 < ln < 400):
        return None
    data = read(va, ln + 1)
    if len(data) < ln + 1 or data[ln] != 0:
        return None
    try:
        return data[:ln].decode("gbk")
    except Exception:
        return data[:ln].decode("latin1")


def triage(va, maxlen=0x1000):
    data = read(va, maxlen)
    calls, strs, globals_ = [], [], []
    size = 0
    seen_ret = False
    for i in MD.disasm(data, va):
        size = i.address + i.size - va
        if i.mnemonic == "call" and i.op_str.startswith("0x"):
            calls.append(int(i.op_str, 0))
        if i.mnemonic == "call" and "ptr [e" in i.op_str:
            calls.append(i.op_str)
        for tok in i.op_str.replace(",", " ").replace("[", " ").replace("]", " ").split():
            if tok.startswith("0x") and len(tok) >= 7:
                v = int(tok, 0)
                s = as_string(v)
                if s and s not in strs:
                    strs.append(s)
                elif 0x7D0000 <= v <= 0x7E0000 and v not in globals_:
                    globals_.append(v)
        if i.mnemonic == "ret":
            seen_ret = True
            break
    return size, calls, strs, globals_, seen_ret


if __name__ == "__main__":
    for a in sys.argv[1:]:
        va = int(a, 0)
        size, calls, strs, gl, r = triage(va)
        print("=== 0x%06X  size=%d%s" % (va, size, "" if r else "  (no ret in window)"))
        uniq = []
        for c in calls:
            k = c if isinstance(c, str) else "0x%06X" % c
            if k not in uniq:
                uniq.append(k)
        print("  calls  : " + ", ".join(uniq))
        if gl:
            print("  globals: " + ", ".join("0x%06X" % g for g in gl))
        for s in strs:
            print("  str    : %r" % s)
        print()
