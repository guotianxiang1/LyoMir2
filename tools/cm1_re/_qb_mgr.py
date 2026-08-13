import struct, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
from capstone import *

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000
DATA = open(IMG, "rb").read()

def rd(va, n):
    off = va - BASE
    if off < 0 or off + n > len(DATA):
        return None
    return DATA[off:off + n]

def u32(va):
    b = rd(va, 4)
    return struct.unpack("<I", b)[0] if b else None

def gstr(va, maxlen=120):
    b = rd(va, maxlen)
    if b is None:
        return None
    end = b.find(b"\x00")
    if end >= 0:
        b = b[:end]
    if len(b) < 1:
        return None
    try:
        return b.decode("gbk")
    except Exception:
        return b.decode("latin1", "replace")

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True

def find_all(needle):
    out = []
    start = 0
    while True:
        i = DATA.find(needle, start)
        if i < 0:
            break
        out.append(i + BASE)
        start = i + 1
    return out

print("=== writers/readers of slot 0x7D62DC ===")
needle = struct.pack("<I", 0x7D62DC)
for va in find_all(needle):
    # disasm the instruction covering va
    for back in range(1, 12):
        start = va - back
        code = DATA[start - BASE: start - BASE + 16]
        try:
            insn = next(md.disasm(code, start))
        except StopIteration:
            continue
        if insn.address <= va < insn.address + insn.size and ("0x7d62dc" in insn.op_str):
            print("  %08X  %-20s %s %s" % (insn.address, insn.bytes.hex().upper(), insn.mnemonic, insn.op_str))
            break

print()
print("=== resting value & RTTI probe ===")
p = u32(0x7D62DC)
print("  [0x7D62DC] = 0x%08X" % p)
# In Delphi, class instance: [obj+0] = vtable ptr. vtable-0x4C? Actually classname via VMT.
# VMT layout: vmt+0xEC(? ) Let me try TObject VMT: className at vmt-0x?  Delphi VMT: vmtClassName = -0x2C? Use known: vmtClassName = -0x28? 
# Simpler: obj -> vtbl; vtbl is a VA; className pointer often at vtbl+0? We'll dump obj header and vtbl area.
if p and BASE <= p < BASE + len(DATA):
    vt = u32(p)
    print("  obj@0x%08X  vtbl=0x%08X" % (p, vt or 0))
    if vt and BASE <= vt < BASE + len(DATA):
        # Delphi VMT className: at vmt + vmtClassName. vmtClassName offset = -0x2C in D7? We'll scan vmt-0x60..vmt+0x8 for a ptr to a short string.
        for d in range(-0x60, 0x10, 4):
            ptr = u32(vt + d)
            if ptr and BASE <= ptr < BASE + len(DATA):
                ln = rd(ptr, 1)
                if ln and 1 <= ln[0] <= 40:
                    s = rd(ptr + 1, ln[0])
                    try:
                        txt = s.decode("latin1")
                        if all(32 <= c < 127 for c in s):
                            print("    vmt%+#x -> shortstr '%s'" % (d, txt))
                    except Exception:
                        pass

def find_all2(needle):
    out=[]; s=0
    while True:
        i=DATA.find(needle,s)
        if i<0: break
        out.append(i+BASE); s=i+1
    return out

print()
print("=== does quiz POSER region (0x6D6400..0x6D6700) reference [0x7D62DC] or call 0x71315C? ===")
for va in find_all2(struct.pack("<I",0x7D62DC)):
    if 0x6D6400<=va<=0x6D6700: print("  ref 0x7D62DC near %08X"%va)
for va in find_all2(bytes.fromhex("E8")):
    pass
# scan poser range for call rel32 to 0x71315C
for base in range(0x6D6400,0x6D6700):
    b=rd(base,5)
    if b and b[0]==0xE8:
        tgt=(base+5+struct.unpack("<i",b[1:5])[0])&0xFFFFFFFF
        if tgt in (0x71315C,0x713094,0x713CBC):
            print("  %08X call 0x%X"%(base,tgt))

print()
print("=== quiz POSER  0x6D6600..0x6D6660 (sets 0x7c3=1) ===")
for i in md.disasm(rd(0x6D6600, 0x70), 0x6D6600):
    cmt=""
    for op in i.operands:
        if op.type==CS_OP_IMM:
            v=op.imm&0xFFFFFFFF
            if 0x6C0000<=v<=0x7E0000:
                s=gstr(v)
                if s and sum(1 for c in s if c.isprintable())>=max(2,len(s)*0.7):
                    cmt="  ; '%s'"%s
    print("  %08X  %-20s %s %s%s"%(i.address,i.bytes.hex().upper(),i.mnemonic,i.op_str,cmt))

print()
print("=== answer-window driver 0x6DCF00..0x6DCF60 (sets 0x7c4=1) ===")
for i in md.disasm(rd(0x6DCF00, 0x60), 0x6DCF00):
    cmt=""
    for op in i.operands:
        if op.type==CS_OP_IMM:
            v=op.imm&0xFFFFFFFF
            if 0x6C0000<=v<=0x7E0000:
                s=gstr(v)
                if s and sum(1 for c in s if c.isprintable())>=max(2,len(s)*0.7):
                    cmt="  ; '%s'"%s
    print("  %08X  %-20s %s %s%s"%(i.address,i.bytes.hex().upper(),i.mnemonic,i.op_str,cmt))
