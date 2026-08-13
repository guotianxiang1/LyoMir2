import io, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _cm1_probe as P

W = [
    ("1061", 0x6CBDD4), ("1068", 0x6D1780), ("1080", 0x6CF49C), ("1084", 0x6D1AB8),
    ("1090/1200", 0x6BD674), ("1210", 0x6E3974), ("1211", 0x6E39C8), ("1212", 0x6E3A34),
    ("1213", 0x6E3A4C), ("1213b", 0x6151CC), ("1213c", 0x6152B8), ("1214", 0x6E3A88),
    ("1217", 0x6C53B8), ("1248", 0x6E5384), ("1250", 0x6E1CEC), ("1251", 0x6E7E0C),
    ("1254", 0x6F9538), ("1255", 0x6E8350), ("1258", 0x6E82F4), ("1259", 0x6E8454),
    ("1260", 0x6E84BC),
]
out = io.open(r"D:\loym2\.claude\wt2\cm-1\tools\cm1_re\_cm1_workers.txt", "w", encoding="utf-8")
old = sys.stdout
sys.stdout = out
for tag, va in W:
    print("=" * 78)
    print("CM %s  worker 0x%08X" % (tag, va))
    print("=" * 78)
    P.dump(va)
    print()
sys.stdout = old
out.close()
print("ok")
