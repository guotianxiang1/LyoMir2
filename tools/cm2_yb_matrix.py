"""Extract the (gate, YBDB subcode, failure SM, Recog) matrix for the CM 1350..1364
handler cluster at 0x6F09C4..0x6F120C.

Every member has the same shape:
    if gate_6F0A24(self) then exit;
    <argument validity tests>
    ok := submit_6D3694(self, dx=subcode, ecx=p1, len, ptr, extra);
    self.[0x18C8] := ok;
    if not ok then SendSocket(self, dx=failSM, ecx=recog, 0,0,0,0);
"""
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\cm-2\tools")
from cm2_dis import read  # noqa: E402
from capstone import Cs, CS_ARCH_X86, CS_MODE_32  # noqa: E402

MD = Cs(CS_ARCH_X86, CS_MODE_32)
SUBMIT = 0x6D3694
GATE = 0x6F0A24

CASES = [
    (1350, 0x6DAC8E, 0x6F09C4), (1351, 0x6DACA7, 0x6F0A98),
    (1352, 0x6DACD0, 0x6F0B84), (1353, 0x6DACE4, 0x6F0E0C),
    (1354, 0x6DACF6, 0x6F0E64), (1355, 0x6DAD08, 0x6F0EBC),
    (1356, 0x6DAD21, 0x6F0F28), (1357, 0x6DAD33, 0x6F0F80),
    (1358, 0x6DAD45, 0x6F0FD8), (1359, 0x6DAD57, 0x6F1028),
    (1360, 0x6DAD6B, 0x6F1028), (1361, 0x6DAD7F, 0x6F110C),
    (1362, 0x6DAD91, 0x6F1164), (1363, 0x6DADA3, 0x6F11BC),
    (1364, 0x6DADB5, 0x6F120C),
]


def scan(va, maxlen=0x300):
    subcodes, fails, gate = [], [], False
    dx = ecx = None
    for i in MD.disasm(read(va, maxlen), va):
        op = i.op_str
        if i.mnemonic == "mov" and op.startswith("dx, 0x"):
            dx = int(op.split(", ")[1], 0)
        if i.mnemonic == "mov" and op.startswith("ecx, 0x"):
            ecx = int(op.split(", ")[1], 0)
        if i.mnemonic == "xor" and op == "ecx, ecx":
            ecx = 0
        if i.mnemonic == "or" and op == "ecx, 0xffffffff":
            ecx = -1
        if i.mnemonic == "call":
            if op == "0x%x" % GATE:
                gate = True
            elif op == "0x%x" % SUBMIT:
                subcodes.append((dx, ecx))
            elif "0x250]" in op:
                fails.append((dx, ecx))
        if i.mnemonic == "ret":
            break
    return gate, subcodes, fails


if __name__ == "__main__":
    print("%-6s %-9s %-9s %-5s %-22s %s" %
          ("CM", "arm", "callee", "gate", "submit(dx=sub,ecx)", "fail-reply(dx=SM,ecx)"))
    for cm, armva, callee in CASES:
        gate, subs, fails = scan(callee)
        s = ", ".join("%d(0x%X)/ecx=%s" % (d, d, c) for d, c in subs)
        f = ", ".join("%d(0x%X)/ecx=%s" % (d, d, c) for d, c in fails)
        print("%-6d 0x%06X 0x%06X %-5s %-22s %s" % (cm, armva, callee, gate, s, f))
