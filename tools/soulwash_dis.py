"""Soul-wash subsystem disassembler: CM 4126/4127/4128 workers + helpers.

Linear disassembly with Delphi long-string annotation and struct-offset notes.
Usage: python soulwash_dis.py 0xADDR [n_bytes]
       python soulwash_dis.py all   -> dump the pinned worker set
"""
import sys
import io
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

IMG = r"D:/loym2/staging/_reunpack_work/flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()
md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True

CODE_LO, CODE_HI = 0x401000, 0x7A10D0


def rd32(va):
    return struct.unpack("<I", data[va - BASE:va - BASE + 4])[0]


def delphi_str(va, n=96):
    """If va points into image, try to read a Delphi long string (len at ptr-4)."""
    if not (BASE <= va < BASE + len(data)):
        return None
    try:
        ln = struct.unpack("<i", data[va - BASE - 4:va - BASE])[0]
    except Exception:
        return None
    if 0 < ln < 200:
        raw = data[va - BASE:va - BASE + ln]
        try:
            return raw.decode("gbk", errors="replace")
        except Exception:
            return repr(raw)
    return None


def annotate(ins):
    notes = []
    # struct offset displacement (esi/edi/eax + 0x...)
    for op in ins.operands:
        if op.type == 3:  # X86_OP_MEM
            disp = op.mem.disp
            if 0x100 <= disp <= 0x2000:
                notes.append("[+0x%X]" % disp)
    # immediate that points to a Delphi string
    for op in ins.operands:
        if op.type == 2:  # imm
            s = delphi_str(op.imm)
            if s:
                notes.append('"%s"' % s)
    # call/mov to absolute mem holding a string ptr
    if ins.mnemonic in ("mov", "lea", "push") and "0x" in ins.op_str:
        pass
    return "  ; " + " ".join(notes) if notes else ""


def dis(va, nbytes=0x200, stop_on_ret=True, label=""):
    print("\n=== %s %08X (%d bytes) ===" % (label, va, nbytes))
    off = va - BASE
    depth_ret = 0
    for i in md.disasm(data[off:off + nbytes], va):
        line = "%08X  %-20s %-7s %s" % (i.address, i.bytes.hex().upper(), i.mnemonic, i.op_str)
        print(line + annotate(i))
        if stop_on_ret and i.mnemonic == "ret":
            depth_ret += 1
            if depth_ret >= 1:
                # keep going a little to catch tail routines? no, stop at first ret past min size
                if i.address - va > 0x30:
                    break


WORKERS = {
    "4126_apply 0x6BF75C": (0x6BF75C, 0x260),
    "4126_bare_reply 0x6BF8E9": (0x6BF8E9, 0x40),
    "4127_recompute 0x747CF4": (0x747CF4, 0x1C0),
    "4127_send 0x74730C": (0x74730C, 0x140),
    "4128_worker 0x6B7184": (0x6B7184, 0xC0),
    "4128_validator 0x76C9D4": (0x76C9D4, 0xB0),
    "bitpop 0x4C7A34": (0x4C7A34, 0x60),
}

if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "all":
        for label, (va, n) in WORKERS.items():
            dis(va, n, label=label)
    elif len(sys.argv) >= 2:
        va = int(sys.argv[1], 0)
        n = int(sys.argv[2], 0) if len(sys.argv) >= 3 else 0x200
        dis(va, n, label="dis")
    else:
        print(__doc__)
