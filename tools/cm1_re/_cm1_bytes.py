import io, sys, os, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _cm1_probe as P

# (cm, handler VA, handler byte length up to and including the jmp to 0x6DBC2C)
BATCH = [
    (1054, 0x6D942F), (1055, 0x6D9492), (1056, 0x6D953A), (1057, 0x6D9547),
    (1059, 0x6D9554), (1061, 0x6D9579), (1068, 0x6D959B), (1080, 0x6D95D6),
    (1084, 0x6D95C9), (1090, 0x6D9732), (1200, 0x6DA21F), (1210, 0x6DA418),
    (1211, 0x6DA45D), (1212, 0x6DA49B), (1213, 0x6DA4BF), (1214, 0x6DA529),
    (1217, 0x6DA372), (1248, 0x6DA58E), (1250, 0x6DA5A1), (1251, 0x6DA66A),
    (1254, 0x6DA69F), (1255, 0x6DA6B1), (1258, 0x6DA6C3), (1259, 0x6DA6EF),
    (1260, 0x6DA6FC),
]

out = io.open(r"D:\loym2\.claude\wt2\cm-1\tools\cm1_re\_cm1_bytes.txt", "w", encoding="utf-8")
for cm, va in BATCH:
    body = P.walk(va)
    end = max(body) + body[max(body)].size
    n = end - va
    raw = P.rd(va, n).hex().upper()
    grouped = " ".join(raw[i:i + 2] for i in range(0, len(raw), 2))
    out.write("CM %d  0x%08X  len=%d\n  %s\n\n" % (cm, va, n, grouped))
out.close()
print("ok")
