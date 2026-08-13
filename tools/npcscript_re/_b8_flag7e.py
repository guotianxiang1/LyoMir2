import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
from _dis import DATA, BASE, dstr, xref_imm
from _b8_region import dis2

print("### map-flag parser arms that set [map+0x7E]")
print(dis2(0x775660, 0x7756B0))
print()
print(dis2(0x7766D8, 0x776712))
print()
print("### the @OnReLive caller function around 0x74393B")
print(dis2(0x7438C0, 0x743990))
print()
print("### who creates TEnvironment (0x77477C) / TDynEnvir (0x5FB264) / TArenaRoom (0x612C70)")
for vmt, nm in ((0x77477C, "TEnvironment"), (0x5FB264, "TDynEnvir"),
                (0x612C70, "TArenaRoom"), (0x5F7B58, "TDynSuperForceMapEnvir"),
                (0x5F9934, "TFoxBossDungeonDynEnvir")):
    xs = xref_imm(vmt)
    print("  %-24s VMT=0x%06X  dword xrefs: %s" % (
        nm, vmt, ", ".join("0x%06X" % x for x in xs)))
