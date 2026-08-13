import re, io, sys
ROOT=r"D:\loym2\.claude\wt2\state-consolidate\GameSvr\Actors"
def read(f): return open(ROOT+"\\"+f,encoding="utf-8").read()

def method_body(src, sig):
    i=src.index(sig)
    # find first { after sig
    j=src.index("{", i)
    depth=0; k=j
    while k<len(src):
        if src[k]=="{": depth+=1
        elif src[k]=="}":
            depth-=1
            if depth==0: break
        k+=1
    return src[j:k]

def cases(body):
    return sorted(set(int(m.group(1)) for m in re.finditer(r"case\s+(\d+)\s*:", body)))

nsa=read("TBaseObject.NativeStateArms.cs")
tsd=read("TBaseObject.TimedAbilityStateDispatch.cs")
sac=read("TBaseObject.StateArms_C.cs")
tab=read("TBaseObject.TimedAbility.cs")

A_g=cases(method_body(nsa,"DispatchNativeStateGainedArm(byte"))
A_l=cases(method_body(nsa,"DispatchNativeStateLostArm(byte"))
B_g=cases(method_body(tsd,"OnNativeTimedStateGained(byte"))
Cg=cases(method_body(sac,"DispatchNativeStateGainedTextBatchC(byte"))
Cl=cases(method_body(sac,"DispatchNativeStateLostTextBatchC(byte"))
L_flags=cases(method_body(tab,"OnNativeTimedStateLost(byte"))
inline75=[75]

print("A  DispatchNativeStateGainedArm      :",A_g)
print("B  OnNativeTimedStateGained          :",B_g)
print("C  GainedTextBatchC                  :",Cg)
print("AL DispatchNativeStateLostArm        :",A_l)
print("CL LostTextBatchC                    :",Cl)
print("OnNativeTimedStateLost(flags,no msg) :",L_flags)
print("inline-75 in SendTimedAbilityState   :",inline75)

# Gained sender count map (all of A,B,C run for non-75; 75 only inline)
from collections import Counter
gc=Counter()
for s in A_g: gc[s]+=1
for s in B_g: gc[s]+=1
for s in Cg: gc[s]+=1
for s in inline75: gc[s]+=1
lc=Counter()
for s in A_l: lc[s]+=1
for s in Cl: lc[s]+=1
for s in inline75: lc[s]+=1

# native msg-send sets (from rev.py msgset)
NAT_G=[1,21,22,26,29,30,31,32,33,34,35,36,37,38,39,40,41,44,45,49,53,56,62,63,71,75,76,77,78,79,80,81,82,83,84,85,86,87,88,90,91,92,93,94,96,97,98,99,100,101,102,103,104,106]
NAT_L=[21,22,32,33,34,35,36,37,38,39,40,41,43,45,49,56,62,63,71,75,76,77,78,79,80,81,82,83,84,85,86,87,88,90,91,92,93,94,96,97,98,99,100,101,102,103,104,106]

def report(title, cnt, nat):
    print("\n==== %s ===="%title)
    natset=set(nat); csset=set(cnt)
    dbl=sorted([s for s in cnt if cnt[s]>=2])
    miss=sorted([s for s in natset if cnt.get(s,0)==0])
    extra=sorted([s for s in csset if s not in natset])
    print("native should-send:",len(nat),"states")
    print("C# union          :",len(csset),"states")
    print("DOUBLE (>=2 sites):",dbl)
    print("MISSING (nat but C#=0):",miss)
    print("EXTRA  (C# but not nat):",extra)
    # per-state where mismatch
    for s in sorted(natset|csset):
        c=cnt.get(s,0); innat=s in natset
        if c!=1 or not innat:
            tag=""
            if c>=2: tag="DOUBLE"
            elif c==0 and innat: tag="MISSING"
            elif not innat: tag="EXTRA"
            print(f"  state {s:3d}: C#={c} native={'Y' if innat else 'N'}  {tag}")

report("GAINED", gc, NAT_G)
report("LOST", lc, NAT_L)
