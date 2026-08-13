import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMAGE = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
CODE_LO = 0x401000
CODE_HI = 0x7A10D0

with open(IMAGE, "rb") as f:
    DATA = f.read()

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False

def off(va):
    return va - BASE

def va_of(o):
    return o + BASE

def disasm_window(start_va, nbytes):
    o = off(start_va)
    code = DATA[o:o+nbytes]
    return list(md.disasm(code, start_va))

def find_bytes(pattern, lo=CODE_LO, hi=CODE_HI):
    """find all offsets (VA) of a byte pattern within code range"""
    res = []
    start = off(lo)
    end = off(hi)
    i = start
    plen = len(pattern)
    while True:
        j = DATA.find(pattern, i, end)
        if j < 0:
            break
        res.append(va_of(j))
        i = j + 1
    return res

def is_slot_call(insn):
    # call dword ptr [reg + 0x250] / [reg + 0x254]
    if insn.mnemonic != "call":
        return None
    op = insn.op_str
    if "+ 0x250]" in op:
        return 0x250
    if "+ 0x254]" in op:
        return 0x254
    return None

def scan_ident(ident):
    """find mov dx, ident sites that reach a slot call within 0x48 bytes"""
    lo = ident & 0xFF
    hi = (ident >> 8) & 0xFF
    pat = bytes([0x66, 0xBA, lo, hi])  # mov dx, imm16
    hits = find_bytes(pat)
    out = []
    for va in hits:
        insns = disasm_window(va, 0x60)
        # confirm first insn is mov dx, ident
        if not insns:
            continue
        first = insns[0]
        if not (first.mnemonic == "mov" and first.op_str.replace(" ", "").startswith("dx,")):
            continue
        slot = None
        callva = None
        seq = []
        for ins in insns:
            seq.append(ins)
            s = is_slot_call(ins)
            if s is not None:
                slot = s
                callva = ins.address
                break
            if ins.address - va > 0x48:
                break
        if slot is not None:
            out.append((va, callva, slot, seq))
    return out

def dump_context(callva, back=0x40, fwd=6):
    """linear disasm from callva-back to callva+fwd, aligned to land on callva"""
    # try alignments so an instruction boundary hits callva
    for delta in range(0, back):
        start = callva - back + delta
        insns = disasm_window(start, back - delta + fwd)
        addrs = {i.address: i for i in insns}
        if callva in addrs:
            # good alignment
            return [i for i in insns if i.address <= callva]
    return []

def find_ident_loads(ident):
    """search all ways the ident immediate can be loaded into dx/cx/edx and show context"""
    lo = ident & 0xFF
    hi = (ident >> 8) & 0xFF
    pats = {
        "mov dx,imm16":  bytes([0x66, 0xBA, lo, hi]),
        "mov cx,imm16":  bytes([0x66, 0xB9, lo, hi]),
        "mov edx,imm32": bytes([0xBA, lo, hi, 0x00, 0x00]),
        "mov ecx,imm32": bytes([0xB9, lo, hi, 0x00, 0x00]),
    }
    for label, pat in pats.items():
        hits = find_bytes(pat)
        for va in hits:
            print(f"  [{label}] @0x{va:06X}")
            ctx = disasm_window(va, 0x40)
            for ins in ctx[:14]:
                b = " ".join(f"{x:02X}" for x in ins.bytes)
                print(f"     0x{ins.address:06X}  {b:<20} {ins.mnemonic} {ins.op_str}")


if __name__ == "__main__":
    args = sys.argv[1:]
    summary = False
    back = 0x40
    if args and args[0] == "--find":
        for ident in [int(x, 0) for x in args[1:]]:
            print(f"=== find loads of SM {ident} (0x{ident:X}) ===")
            find_ident_loads(ident)
            print()
        sys.exit(0)
    if args and args[0] == "--summary":
        summary = True
        args = args[1:]
    if args and args[0].startswith("--back="):
        back = int(args[0].split("=")[1], 0)
        args = args[1:]
    idents = [int(x, 0) for x in args]
    if not idents:
        idents = [3003]
    for ident in idents:
        res = scan_ident(ident)
        if summary:
            sites = ", ".join(f"0x{cv:06X}/[{slot:X}]" for (_, cv, slot, _) in res)
            print(f"SM {ident} (0x{ident:X}): {len(res)} site(s): {sites}")
            continue
        print(f"=== SM {ident} (0x{ident:X}) : {len(res)} send-site(s) ===")
        for (va, callva, slot, seq) in res:
            print(f"  --- send @0x{callva:06X} slot [+0x{slot:X}] (mov dx @0x{va:06X}) ---")
            ctx = dump_context(callva, back=back, fwd=8)
            for ins in ctx:
                b = " ".join(f"{x:02X}" for x in ins.bytes)
                print(f"     0x{ins.address:06X}  {b:<22} {ins.mnemonic} {ins.op_str}")
        print()
