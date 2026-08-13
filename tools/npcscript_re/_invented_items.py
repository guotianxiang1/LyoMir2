"""Re-run the four-way zero-hit proof for the 8 item/money script APIs that have
live C# implementations but no native registration, before flipping them to
fail-closed.

Four probes, all must be zero:
  1. not in the 654-entry native registration walk
  2. flat_image.bin raw ASCII, case-insensitive
  3. flat_image.bin UTF-16LE, case-insensitive
  4. production script tree D:\\光头卧龙
"""
import sys, io, os, re
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _dis import DATA

NAMES = [
    "PsShopBuyGoods", "PsShopGetGoodsList", "TakeHeroBagExItem",
    "TakeFromHeroBagEx", "GetHeroBagExItemCount", "GetHeroBagExItemCountEx",
    "CheckGameGold", "GetStorageItemCount", "OpenStorageMax",
]

REG = r"D:\loym2\.claude\wt2\m-npcscript\docs\m_npcscript_native_registry_20260813.txt"
regnames = set()
for l in open(REG, encoding="utf-8"):
    m = re.match(r"[0-9A-F]{8}  (\S+)", l)
    if m:
        regnames.add(m.group(1).lower())

PROD = r"D:\光头卧龙"
prod_files = []
for root, _dirs, files in os.walk(PROD):
    for f in files:
        if os.path.splitext(f)[1].lower() in (".pas", ".inc", ".txt", ".ini"):
            prod_files.append(os.path.join(root, f))
prod_blobs = []
for p in prod_files:
    try:
        prod_blobs.append(open(p, "rb").read().lower())
    except OSError:
        pass
print("production files scanned: %d (under %s)" % (len(prod_blobs), PROD))
print("native registry entries: %d" % len(regnames))
print()
print("%-26s %-8s %-8s %-8s %-8s" % ("name", "reg", "ascii", "utf16", "prod"))
print("-" * 62)
for n in NAMES:
    inreg = "HIT" if n.lower() in regnames else "0"
    a = n.encode("ascii").lower()
    na = len(re.findall(re.escape(a), DATA.lower()))
    u = n.encode("utf-16-le").lower()
    nu = len(re.findall(re.escape(u), DATA.lower()))
    npd = sum(b.count(a) for b in prod_blobs)
    print("%-26s %-8s %-8d %-8d %-8d" % (n, inreg, na, nu, npd))
