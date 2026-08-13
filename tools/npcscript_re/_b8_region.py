import sys, io
from _dis import DATA, BASE, md, dstr


def dis2(start, end):
    """disassemble [start,end) resyncing past undecodable bytes."""
    out = []
    va = start
    while va < end:
        n = end - va
        got = 0
        for ins in md.disasm(DATA[va - BASE:end - BASE], va):
            out.append("%08X  %-22s %s %s" % (
                ins.address, ins.bytes.hex(), ins.mnemonic, ins.op_str))
            va = ins.address + ins.size
            got += 1
        if got == 0:
            out.append("%08X  %-22s (db)" % (va, DATA[va - BASE:va - BASE + 1].hex()))
            va += 1
    return "\n".join(out)


if __name__ == "__main__":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
    a = int(sys.argv[1], 16)
    b = int(sys.argv[2], 16)
    print(dis2(a, b))
