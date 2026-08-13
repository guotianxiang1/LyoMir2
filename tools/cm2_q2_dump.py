"""Write clean (UTF-8) disasm + Delphi-string + SM-id annotations for chosen VAs."""
import sys
sys.path.insert(0, r"D:\loym2\.claude\wt2\cm-2\tools")
from cm2_dis import read  # noqa: E402
from cm2_triage import as_string  # noqa: E402
from capstone import Cs, CS_ARCH_X86, CS_MODE_32  # noqa: E402

MD = Cs(CS_ARCH_X86, CS_MODE_32)


def dump(va, maxlen=0x400):
    data = read(va, maxlen)
    out = []
    last_id = None
    for i in MD.disasm(data, va):
        line = "%08X  %s %s" % (i.address, i.mnemonic, i.op_str)
        ann = []
        if i.mnemonic == "mov" and i.op_str.startswith(("dx,", "cx,")) and "0x" in i.op_str:
            try:
                last_id = int(i.op_str.split(",")[1].strip(), 0)
            except ValueError:
                pass
        for tok in i.op_str.replace(",", " ").replace("[", " ").replace("]", " ").replace("+", " ").split():
            if tok.startswith("0x") and len(tok) >= 7:
                s = as_string(int(tok, 0))
                if s:
                    ann.append("STR=%r" % s)
        if i.mnemonic == "call" and "ptr [e" in i.op_str:
            ann.append("SEND id=0x%X(%d)" % (last_id or 0, last_id or 0))
        if ann:
            line += "    ; " + " ".join(ann)
        out.append(line)
        if i.mnemonic == "ret":
            break
    return "\n".join(out)


TARGETS = [int(a, 0) for a in sys.argv[2:]]
with open(sys.argv[1], "w", encoding="utf-8") as f:
    for va in TARGETS:
        f.write("################ 0x%06X\n" % va)
        f.write(dump(va) + "\n\n")
