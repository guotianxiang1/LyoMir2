from _dis import *
import re, json, difflib

# PascalScript runtime built-in declarations: consumed by 0x4F9320 and 0x50ED24
decls = {}
for m in re.finditer(rb"(function|procedure)[ (]", DATA):
    off = m.start()
    ln = int.from_bytes(DATA[off - 4:off], "little")
    if 4 < ln < 1000 and off + ln <= len(DATA) and DATA[off + ln] == 0:
        decls[BASE + off] = DATA[off:off + ln]

out = []
for off in range(0, len(DATA) - 5):
    if DATA[off] != 0xBA:
        continue
    imm = int.from_bytes(DATA[off + 1:off + 5], "little")
    if imm not in decls:
        continue
    for k in range(5, 20):
        if DATA[off + k] == 0xE8:
            rel = int.from_bytes(DATA[off + k + 1:off + k + 5], "little", signed=True)
            tgt = (BASE + off + k + 5 + rel) & 0xFFFFFFFF
            if tgt in (0x4F9320, 0x50ED24):
                d = decls[imm].decode("gbk", "replace")
                nm = re.match(r"\s*(function|procedure)\s+([A-Za-z_]\w*)", d)
                out.append(dict(site="%08X" % (BASE + off), helper="%08X" % tgt,
                                va="%08X" % imm, name=nm.group(2) if nm else d, decl=d))
            break

print("PascalScript runtime built-ins:", len(out))
for r in out:
    print("  %s  %-18s %s" % (r["site"], r["name"], r["decl"][:90]))
json.dump(out, open("_builtins.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
