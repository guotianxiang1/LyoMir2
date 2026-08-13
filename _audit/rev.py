import sys, capstone
BASE = 0x400000
PATH = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
data = open(PATH,"rb").read()

def off(va): return va - BASE
def rd(va,n): return data[off(va):off(va)+n]
def u32(va): return int.from_bytes(rd(va,4),"little")
def u8(va): return data[off(va)]

md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
md.detail = True

def dis(va, n=60, stop_va=None):
    code = data[off(va):off(va)+n*16]
    cnt=0
    for ins in md.disasm(code, va):
        print(f"0x{ins.address:06x}: {ins.mnemonic:8s} {ins.op_str}")
        cnt+=1
        if cnt>=n: break
        if stop_va and ins.address>=stop_va: break

GAINED_IDXMAP = 0x7418E2   # byte[107] index map, state 0..106
GAINED_JT     = 0x74194D   # dword[] jump table, indexed by index map value
LOST_JT       = 0x7426A9   # dword[93] jump table, indexed by (state-14), state 14..106
DEFAULT       = 0x742C42   # default / no-op target

def parse_tables():
    # gained index map: 107 bytes 0..106
    idxmap = [u8(GAINED_IDXMAP+i) for i in range(107)]
    maxidx = max(idxmap)
    # jump table entries: maxidx+1 dwords
    gjt = [u32(GAINED_JT+i*4) for i in range(maxidx+1)]
    gained = {}   # state -> target va
    for s in range(107):
        gained[s] = gjt[idxmap[s]]
    lost = {}      # state -> target va (state 14..106)
    for s in range(14,107):
        lost[s] = u32(LOST_JT + (s-14)*4)
    return idxmap, gjt, gained, lost

def dstr(va):
    try:
        ln=u32(va-4)
        if ln<=0 or ln>200: return None
        b=rd(va,ln)
        return b.decode("gbk","replace")
    except: return None

def analyze_handler(va, limit=40):
    """Scan a handler from va until it reaches DEFAULT jmp or another handler.
    Classify: MSG (calls [ebx+0xd4]) / OTHER (other call) / SILENT."""
    code=data[off(va):off(va)+limit*16]
    info={"cx":[], "strs":[], "bytes":[], "vcall":False, "othercall":[], "end":None}
    for ins in md.disasm(code, va):
        m=ins.mnemonic; o=ins.op_str
        if m=="mov" and o.startswith("cx,"):
            info["cx"].append(o.split(",")[1].strip())
        if m=="mov" and o.startswith("edx,"):
            val=o.split(",")[1].strip()
            if val.startswith("0x"):
                iv=int(val,16)
                s=dstr(iv)
                if s: info["strs"].append((val,s))
        if m=="mov" and "byte ptr [esi" in o:
            info["bytes"].append(o)
        if m=="call":
            if "ebx + 0xd4" in o:
                info["vcall"]=True
            elif o.startswith("0x"):
                info["othercall"].append(o)
        if m=="jmp":
            info["end"]=ins.address
            break
    info["kind"] = "MSG" if info["vcall"] else ("OTHER" if info["othercall"] else "SILENT")
    return info

if __name__=="__main__":
    cmd=sys.argv[1]
    if cmd=="dis":
        va=int(sys.argv[2],16); n=int(sys.argv[3]) if len(sys.argv)>3 else 60
        dis(va,n)
    elif cmd=="u32":
        va=int(sys.argv[2],16); n=int(sys.argv[3]) if len(sys.argv)>3 else 1
        for i in range(n):
            print(f"0x{va+i*4:06x}: 0x{u32(va+i*4):08x}")
    elif cmd=="bytes":
        va=int(sys.argv[2],16); n=int(sys.argv[3]) if len(sys.argv)>3 else 32
        b=rd(va,n)
        print(" ".join(f"{x:02x}" for x in b))
    elif cmd=="table":
        idxmap,gjt,gained,lost=parse_tables()
        print(f"# jump table entries (gained), count={len(gjt)}:")
        for i,t in enumerate(gjt):
            print(f"  gjt[{i:2d}] = 0x{t:06x}{'  <DEFAULT>' if t==DEFAULT else ''}")
        print("\n# per-state truth table (state: gained_target lost_target)")
        print(f"{'st':>3} {'gidx':>4} {'gained':>8} {'gsend':>5} | {'lost':>8} {'lsend':>5}")
        for s in range(107):
            g=gained[s]; gi=idxmap[s]; gsend = (g!=DEFAULT)
            if s in lost:
                l=lost[s]; lsend=(l!=DEFAULT)
                print(f"{s:3d} {gi:4d} 0x{g:06x} {str(gsend):>5} | 0x{l:06x} {str(lsend):>5}")
            else:
                print(f"{s:3d} {gi:4d} 0x{g:06x} {str(gsend):>5} | {'--':>8} {'--':>5}")
    elif cmd=="sendset":
        idxmap,gjt,gained,lost=parse_tables()
        gset=sorted([s for s in range(107) if gained[s]!=DEFAULT])
        lset=sorted([s for s in lost if lost[s]!=DEFAULT])
        print("GAINED should-send states:", gset)
        print("LOST   should-send states:", lset)
        # group gained states by distinct target
        from collections import defaultdict
        gg=defaultdict(list)
        for s in gset: gg[gained[s]].append(s)
        print("\nGAINED distinct targets -> states:")
        for t in sorted(gg): print(f"  0x{t:06x}: {gg[t]}")
        ll=defaultdict(list)
        for s in lset: ll[lost[s]].append(s)
        print("\nLOST distinct targets -> states:")
        for t in sorted(ll): print(f"  0x{t:06x}: {ll[t]}")
    elif cmd=="full":
        idxmap,gjt,gained,lost=parse_tables()
        gset=sorted([s for s in range(107) if gained[s]!=DEFAULT])
        lset=sorted([s for s in lost if lost[s]!=DEFAULT])
        print("==== GAINED handlers ====")
        for s in gset:
            info=analyze_handler(gained[s])
            cx=",".join(info["cx"]); strs="; ".join(f"{v}={s2}" for v,s2 in info["strs"])
            bw=" ".join(info["bytes"]); oc=",".join(info["othercall"])
            print(f"g{s:3d} 0x{gained[s]:06x} {info['kind']:6s} cx=[{cx}] byte=[{bw}] oc=[{oc}] str=[{strs}]")
        print("\n==== LOST handlers ====")
        for s in lset:
            info=analyze_handler(lost[s])
            cx=",".join(info["cx"]); strs="; ".join(f"{v}={s2}" for v,s2 in info["strs"])
            bw=" ".join(info["bytes"]); oc=",".join(info["othercall"])
            print(f"l{s:3d} 0x{lost[s]:06x} {info['kind']:6s} cx=[{cx}] byte=[{bw}] oc=[{oc}] str=[{strs}]")
    elif cmd=="msgset":
        idxmap,gjt,gained,lost=parse_tables()
        gset=[s for s in range(107) if gained[s]!=DEFAULT]
        lset=[s for s in lost if lost[s]!=DEFAULT]
        gmsg=[s for s in gset if analyze_handler(gained[s])["kind"]=="MSG"]
        lmsg=[s for s in lset if analyze_handler(lost[s])["kind"]=="MSG"]
        gnon=[(s,analyze_handler(gained[s])["kind"]) for s in gset if analyze_handler(gained[s])["kind"]!="MSG"]
        lnon=[(s,analyze_handler(lost[s])["kind"]) for s in lset if analyze_handler(lost[s])["kind"]!="MSG"]
        print("GAINED msg-send:",gmsg)
        print("GAINED non-msg :",gnon)
        print("LOST   msg-send:",lmsg)
        print("LOST   non-msg :",lnon)
