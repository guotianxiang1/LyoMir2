"""CmMiscTail agent: linear disasm + annotations for CM 1248/4102/4204/4205 workers.

Evidence base: flat_image.bin @ ImageBase 0x400000, capstone x86-32.
Annotations:
  * push offset X / mov ...,X where X is a Delphi AnsiString  -> string literal
  * mov dx,imm / mov edx,imm                                  -> candidate SM ident
  * call [reg+0x250] / [reg+0x254]                            -> unicast SM reply slot
  * direct call 0xNNNNNN                                      -> callee + its first bytes
  * refs to [0x7Dxxxx]/[0x7Cxxxx] globals and [reg+0xNNN] fields

Usage:
  python tools\\cmmisctail_re.py 0x6E5384 [count]
  python tools\\cmmisctail_re.py all [count]     # dump the four workers to files
"""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMAGE = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000

with open(IMAGE, "rb") as f:
    IMG = f.read()

MD = Cs(CS_ARCH_X86, CS_MODE_32)
MD.detail = True


def rd(va, n):
    o = va - BASE
    if o < 0 or o + n > len(IMG):
        return b""
    return IMG[o:o + n]


def u32(va):
    b = rd(va, 4)
    return int.from_bytes(b, "little") if len(b) == 4 else None


def bhex(b):
    return " ".join("%02X" % x for x in b)


def as_string(va):
    """Delphi 7 AnsiString: [va-8]=refcount(-1/1) [va-4]=len chars.. NUL."""
    if va is None or va < BASE + 0x1000 or va - BASE + 4 > len(IMG):
        return None
    rc = u32(va - 8)
    ln = u32(va - 4)
    if rc is None or ln is None:
        return None
    if rc not in (0xFFFFFFFF, 1) or not (0 < ln < 400):
        return None
    data = rd(va, ln + 1)
    if len(data) < ln + 1 or data[ln] != 0:
        return None
    try:
        return data[:ln].decode("gbk")
    except Exception:
        return data[:ln].decode("latin1")


def annot(i):
    m, ops = i.mnemonic, i.op_str
    notes = []
    # string / global / callee immediates
    for tok in ops.replace(",", " ").replace("[", " ").replace("]", " ").split():
        if tok.startswith("0x") and len(tok) >= 7:
            v = int(tok, 0)
            s = as_string(v)
            if s is not None:
                notes.append("str=%r" % s)
            elif 0x7C0000 <= v <= 0x7E0000:
                notes.append("<global 0x%06X>" % v)
    if m == "call" and ops.startswith("0x"):
        t = int(ops, 0)
        notes.append("-> 0x%06X %s" % (t, bhex(rd(t, 8))))
    if m == "call" and ("+ 0x250]" in ops or "+ 0x254]" in ops):
        slot = "0x250" if "0x250" in ops else "0x254"
        notes.append("*** SM SEND via vmt+%s ***" % slot)
    return "   ; " + " ".join(notes) if notes else ""


def dump(va, count=260):
    print("WORKER 0x%06X  first bytes %s" % (va, bhex(rd(va, 8))))
    cur = va
    seen_ret = False
    for _ in range(count):
        ins = list(MD.disasm(rd(cur, 16), cur))
        if not ins:
            print("  %06X  <decode fail>" % cur)
            break
        i = ins[0]
        if seen_ret and bhex(i.bytes).startswith("55 8B EC"):
            print("  ---- (next function prologue) ----")
            break
        print("  %06X  %-26s %s %s%s"
              % (i.address, bhex(i.bytes), i.mnemonic, i.op_str, annot(i)))
        if i.mnemonic in ("ret", "retn"):
            print("  ---- ret ----")
            seen_ret = True
        cur += i.size


WORKERS = [
    (1248, 0x6E5384, "piece-up @AckPieceUp"),
    (4102, 0x6B7BCC, "trade/market field write"),
    (4204, 0x6F03E8, "SMS auth-code verify"),
    (4205, 0x6F01E4, "SMS auth-code issue"),
]

if __name__ == "__main__":
    if sys.argv[1] == "all":
        cnt = int(sys.argv[2]) if len(sys.argv) > 2 else 260
        for ident, va, tag in WORKERS:
            print("\n########## CM %d  worker 0x%06X  (%s) ##########" % (ident, va, tag))
            dump(va, cnt)
    else:
        va = int(sys.argv[1], 0)
        cnt = int(sys.argv[2]) if len(sys.argv) > 2 else 260
        dump(va, cnt)
