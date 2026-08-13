"""Dump the native ProcessOthGsMsg (TOtherGSMsg @0x657110) dispatch table.

Dispatcher shape proved at 0x657140..0x657159:
    movzx edx, word [ebp-2]        ; ident
    add   edx, 0xFFFFFF36          ; edx = ident - 202
    cmp   edx, 0x37                ; 55
    ja    0x6573A0                 ; default sink
    mov   dl, byte [edx + 0x657160]  ; 56-byte index table
    jmp   dword [edx*4 + 0x657198]   ; address table

So base ident = 202, span = 202..257 inclusive.
"""
import struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
IDX_TAB = 0x657160
ADDR_TAB = 0x657198
IDENT_BASE = 202
SPAN = 0x38          # 56 idents, from `cmp edx,0x37 / ja`
SINK = 0x6573A0


def rd(data, va, n):
    off = va - BASE
    return data[off:off + n]


def main():
    with open(IMG, "rb") as f:
        data = f.read()

    idx = rd(data, IDX_TAB, SPAN)
    nslots = max(idx) + 1
    addrs = list(struct.unpack("<%dI" % nslots, rd(data, ADDR_TAB, nslots * 4)))

    print("index table  0x%06X .. 0x%06X (%d bytes)" % (
        IDX_TAB, IDX_TAB + SPAN - 1, SPAN))
    print("addr  table  0x%06X .. 0x%06X (%d dwords)" % (
        ADDR_TAB, ADDR_TAB + nslots * 4 - 1, nslots))
    print("index bytes: " + " ".join("%02x" % b for b in idx))
    print()
    print("slot  target")
    for s, a in enumerate(addrs):
        print("  %2d  0x%06X%s" % (s, a, "   <-- default sink" if a == SINK else ""))
    print()

    md = Cs(CS_ARCH_X86, CS_MODE_32)
    print("%-6s %-5s %-9s %s" % ("ident", "slot", "stub", "stub body (to first ret/jmp)"))
    for i in range(SPAN):
        ident = IDENT_BASE + i
        slot = idx[i]
        ea = addrs[slot]
        if ea == SINK:
            print("%-6d %-5d 0x%06X  SINK (default error arm)" % (ident, slot, ea))
            continue
        body = []
        for ins in md.disasm(rd(data, ea, 0x40), ea):
            body.append("%s %s" % (ins.mnemonic, ins.op_str))
            if ins.mnemonic in ("ret", "jmp"):
                break
        print("%-6d %-5d 0x%06X  %s" % (ident, slot, ea, " ; ".join(body)))


if __name__ == "__main__":
    main()
