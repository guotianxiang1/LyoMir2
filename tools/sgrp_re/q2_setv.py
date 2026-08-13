"""SetV / keyed-upsert zero semantics + GroupSetV name census."""
import io
import os

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

IMAGE = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000

with open(IMAGE, "rb") as fh:
    DATA = fh.read()

MD = Cs(CS_ARCH_X86, CS_MODE_32)


def dump(va, limit, out):
    off = va - BASE
    print("=== 0x%06X ===" % va, file=out)
    for n, insn in enumerate(MD.disasm(DATA[off:off + limit * 8], va)):
        if n >= limit:
            break
        print("0x%06X  %-22s %s %s" % (insn.address, insn.bytes.hex().upper(),
                                       insn.mnemonic, insn.op_str), file=out)
    print("", file=out)


def census(name, out):
    hits = []
    for enc, label in (("ascii", "ascii"), ("gbk", "gbk"), ("utf-16-le", "u16")):
        try:
            pat = name.encode("ascii" if enc == "ascii" else enc)
        except Exception:
            continue
        start = 0
        while True:
            i = DATA.find(pat, start)
            if i < 0:
                break
            hits.append((label, BASE + i))
            start = i + 1
            if len(hits) > 40:
                break
    # case-insensitive ascii
    low = DATA.lower()
    pat = name.lower().encode("ascii")
    start = 0
    ci = []
    while True:
        i = low.find(pat, start)
        if i < 0:
            break
        ci.append(BASE + i)
        start = i + 1
        if len(ci) > 40:
            break
    print("NAME %-16s exact=%d  ci-ascii=%d" % (name, len(hits), len(ci)), file=out)
    for va in ci[:20]:
        # Delphi ShortString: length byte right before
        lb = DATA[va - BASE - 1]
        print("    0x%06X  prevbyte=%d  ctx=%r" % (
            va, lb, DATA[va - BASE - 2:va - BASE + len(pat) + 4]), file=out)
    print("", file=out)


def main():
    buf = io.StringIO()
    # SetV = sub_6DF288 ; SetS = sub_6DF240 ; GetV = sub_6DF1E4 ; GetS = sub_6DF1B4
    for va, n in [(0x6DF1B4, 40), (0x6DF1E4, 40), (0x6DF240, 40), (0x6DF288, 40)]:
        dump(va, n, buf)
    # keyed upsert / lookup helpers
    for va, n in [(0x6E4140, 110)]:
        dump(va, n, buf)
    for nm in ("GroupSetV", "GroupSetS", "GROUPSETV", "SetV", "GetV"):
        census(nm, buf)
    dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "q2_setv.txt")
    with open(dst, "w", encoding="utf-8") as fh:
        fh.write(buf.getvalue())
    print(dst)


if __name__ == "__main__":
    main()
