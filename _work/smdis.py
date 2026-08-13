import sys, capstone

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
data = open(IMG, "rb").read()

def off(va):
    return va - BASE

md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
md.detail = False

def hexb(b):
    return " ".join("%02X" % x for x in b)

def dump(send_va, ident=None, back=0x80, fwd=0x0C):
    """Disassemble a window ending at send_va (+fwd), anchored so an instruction
    boundary lands on send_va. Prints bytes + mnemonics."""
    # find an alignment where send_va is an instruction boundary and the decode
    # is clean all the way to send_va.
    chosen = None
    for delta in range(back, 6, -1):
        start = send_va - delta
        code = data[off(start):off(send_va) + fwd]
        insns = list(md.disasm(code, start))
        addrs = {i.address for i in insns}
        if send_va in addrs:
            # require decode to actually reach send_va with no gap
            last = start
            ok = True
            for i in insns:
                if i.address > send_va:
                    break
                last = i.address + i.size
            if send_va in addrs:
                chosen = (start, insns)
                break
    if not chosen:
        print("  !! could not align send_va 0x%06x" % send_va)
        return
    start, insns = chosen
    for i in insns:
        if i.address > send_va + fwd:
            break
        mark = ""
        if i.address == send_va:
            mark = "   <-- SEND"
        # flag the mov dx/cx ident
        b = i.bytes
        if ident is not None and len(b) == 4 and b[0] == 0x66 and b[1] in (0xBA, 0xB9):
            iv = b[2] | (b[3] << 8)
            if iv == ident:
                mark = "   <-- mov %s,0x%X (ident)" % ("dx" if b[1] == 0xBA else "cx", iv)
        print("0x%06X  %-22s %s %s%s" % (i.address, hexb(b), i.mnemonic, i.op_str, mark))

if __name__ == "__main__":
    # args: pairs of  ident:va  (va hex).  If ident is '-', not checked.
    for a in sys.argv[1:]:
        parts = a.split(":")
        ident_s, va_s = parts[0], parts[1]
        back = int(parts[2], 0) if len(parts) > 2 else 0x80
        ident = None if ident_s == "-" else int(ident_s)
        va = int(va_s, 16)
        print("==== ident %s  send=0x%06X ====" % (ident_s, va))
        dump(va, ident, back=back)
        print()
