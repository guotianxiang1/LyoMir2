#!/usr/bin/env python3
"""recount audit: minimal disassembler / byte-probe over the M2Server flat image.

Usage:
  recount_dis.py dis <VA-hex> [count]        disassemble N instrs at VA
  recount_dis.py bytes <VA-hex> [len]        hexdump N bytes at VA
  recount_dis.py find <hex-pattern>          find byte pattern (?? = wildcard), print VAs
  recount_dis.py dstr <VA-hex>               decode a Delphi string at VA (refcount/len/chars)
  recount_dis.py sfind <text> [enc]          find a literal string (enc: gbk|ascii|utf16)
"""
import sys

IMAGE = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000

_data = None


def data():
    global _data
    if _data is None:
        with open(IMAGE, "rb") as f:
            _data = f.read()
    return _data


def off(va):
    return va - BASE


def cmd_dis(va, count=40):
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = False
    buf = data()[off(va):off(va) + count * 16]
    n = 0
    for ins in md.disasm(buf, va):
        print("0x%08X  %-24s %s %s" % (ins.address, ins.bytes.hex(), ins.mnemonic, ins.op_str))
        n += 1
        if n >= count:
            break


def cmd_bytes(va, length=64):
    b = data()[off(va):off(va) + length]
    for i in range(0, len(b), 16):
        chunk = b[i:i + 16]
        print("0x%08X  %-48s %s" % (
            va + i, chunk.hex(" "),
            "".join(chr(c) if 32 <= c < 127 else "." for c in chunk)))


def cmd_find(pat):
    parts = pat.replace(" ", "")
    pairs = [parts[i:i + 2] for i in range(0, len(parts), 2)]
    d = data()
    hits = []
    n = len(pairs)
    first = pairs[0]
    if first == "??":
        print("pattern must not start with wildcard")
        return
    fb = bytes.fromhex(first)
    i = d.find(fb)
    while i != -1 and len(hits) < 400:
        ok = True
        for j, p in enumerate(pairs):
            if p == "??":
                continue
            if i + j >= len(d) or d[i + j] != int(p, 16):
                ok = False
                break
        if ok:
            hits.append(i + BASE)
        i = d.find(fb, i + 1)
    print("hits=%d" % len(hits))
    for h in hits:
        print("0x%08X" % h)


def cmd_dstr(va):
    d = data()
    o = off(va)
    ln = int.from_bytes(d[o - 4:o], "little")
    rc = int.from_bytes(d[o - 8:o - 4], "little", signed=True)
    raw = d[o:o + ln] if 0 <= ln < 4096 else b""
    try:
        txt = raw.decode("gbk")
    except Exception:
        txt = repr(raw)
    print("VA=0x%08X refcount=%d len=%d" % (va, rc, ln))
    print("raw=%s" % raw.hex(" "))
    print("gbk=%s" % txt)


def cmd_sfind(text, enc="gbk"):
    if enc == "utf16":
        needle = text.encode("utf-16-le")
    elif enc == "ascii":
        needle = text.encode("ascii", "ignore")
    else:
        needle = text.encode("gbk")
    d = data()
    hits = []
    i = d.find(needle)
    while i != -1 and len(hits) < 200:
        hits.append(i + BASE)
        i = d.find(needle, i + 1)
    print("needle=%s hits=%d" % (needle.hex(" "), len(hits)))
    for h in hits:
        print("0x%08X" % h)


if __name__ == "__main__":
    a = sys.argv[1:]
    if not a:
        print(__doc__)
        sys.exit(1)
    c = a[0]
    if c == "dis":
        cmd_dis(int(a[1], 16), int(a[2]) if len(a) > 2 else 40)
    elif c == "bytes":
        cmd_bytes(int(a[1], 16), int(a[2]) if len(a) > 2 else 64)
    elif c == "find":
        cmd_find(a[1])
    elif c == "dstr":
        cmd_dstr(int(a[1], 16))
    elif c == "sfind":
        cmd_sfind(a[1], a[2] if len(a) > 2 else "gbk")
    else:
        print(__doc__)
