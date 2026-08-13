"""Decode Delphi string literals + SM reply ids for the Q2 workers, UTF-8 to file."""
import sys
sys.path.insert(0, r"D:\loym2\.claude\wt2\cm-2\tools")
from cm2_dis import read  # noqa: E402
from cm2_triage import as_string  # noqa: E402
from capstone import Cs, CS_ARCH_X86, CS_MODE_32  # noqa: E402

MD = Cs(CS_ARCH_X86, CS_MODE_32)


def dump(va, maxlen=0x400):
    """Disasm with string annotations + flag vmt/sm sends, stop at first ret."""
    data = read(va, maxlen)
    out = []
    last_dx = None
    for i in MD.disasm(data, va):
        line = "%08X  %s %s" % (i.address, i.mnemonic, i.op_str)
        ann = []
        # track mov dx/cx imm (candidate SM id)
        if i.mnemonic == "mov" and i.op_str.startswith(("dx,", "cx,")) and "0x" in i.op_str:
            try:
                last_dx = int(i.op_str.split(",")[1].strip(), 0)
            except ValueError:
                pass
        for tok in i.op_str.replace(",", " ").replace("[", " ").replace("]", " ").replace("+", " ").split():
            if tok.startswith("0x") and len(tok) >= 7:
                v = int(tok, 0)
                s = as_string(v)
                if s:
                    ann.append("STR=%r" % s)
        if "ptr [e" in i.op_str and i.mnemonic == "call":
            ann.append("SEND dx=0x%X" % (last_dx or 0))
        if ann:
            line += "    ; " + " ".join(ann)
        out.append(line)
        if i.mnemonic == "ret":
            break
    return "\n".join(out)


if __name__ == "__main__":
    targets = [int(a, 0) for a in sys.argv[1:]]
    for va in targets:
        print("################ 0x%06X" % va)
        print(dump(va))
        print()
