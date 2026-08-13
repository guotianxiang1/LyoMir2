"""Print aligned pre-call context for a list of call sites.

Linear sweep from (site - window) can mis-align, so try every start offset and
keep the first that produces an instruction boundary exactly on `site`.
"""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r'D:\loym2\staging\_reunpack_work\flat_image.bin'
BASE = 0x400000
with open(IMG, 'rb') as f:
    DATA = f.read()

md = Cs(CS_ARCH_X86, CS_MODE_32)


def rd(va, n):
    o = va - BASE
    return DATA[o:o + n]


def hexs(b):
    return ' '.join('%02X' % x for x in b)


def aligned_ctx(site, window=0x50, after=0x10):
    for start in range(site - window, site):
        insns = list(md.disasm(rd(start, window + after + 16), start))
        if any(i.address == site for i in insns):
            out = []
            for i in insns:
                if i.address > site + after:
                    break
                mark = '>>' if i.address == site else '  '
                out.append('%s %08X  %-22s %s %s'
                           % (mark, i.address, hexs(i.bytes), i.mnemonic, i.op_str))
            return out
    return ['  (failed to align %08X)' % site]


if __name__ == '__main__':
    for arg in sys.argv[1:]:
        site = int(arg, 16)
        print('==================== call site %08X ====================' % site)
        print('\n'.join(aligned_ctx(site)))
        print()
