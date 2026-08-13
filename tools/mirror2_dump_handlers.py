"""Bulk-disassemble the 27 REAL ProcessOthGsMsg handlers with Delphi string annotation.

Delphi long-string literals in .rdata carry a -8 refcount and -4 length header;
`push <VA>` / `mov reg,<VA>` operands that satisfy that shape are decoded inline.
"""
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000

HANDLERS = [
    (202, 0x658384, "anti-cheat wrapper"),
    (203, 0x6582B0, "whisper"),
    (207, 0x658114, "single-quote scan?"),
    (209, 0x6580B8, "chat prohibition"),
    (210, 0x657FF8, "chat prohibition cancel"),
    (211, 0x657810, "change castle owner"),
    (212, 0x6577B0, "reload castle info"),
    (213, 0x657F28, "reload admin"),
    (214, 0x6579B0, "friend info"),
    (216, 0x6579D8, "divorce"),
    (217, 0x657CF0, "mentor student left"),
    (218, 0x657AC0, "mentor expel"),
    (219, 0x6581A4, "tag send"),
    (220, 0x657E08, "tag result"),
    (221, 0x6575D8, "user info"),
    (222, 0x657700, "change server receive ok"),
    (224, 0x6574B4, "market open"),
    (226, 0x657888, "lover manager delete"),
    (227, 0x657670, "reload make item list"),
    (228, 0x657BCC, "guild member recall"),
    (240, 0x657F3C, "standard tick"),
    (241, 0x655A18, "credit card clear all"),
    (243, 0x655A74, "credit card clear monthly"),
    (247, 0x65805C, "ident 247"),
    (249, 0x658094, "set nick lin fu"),
    (251, 0x658048, "glory log flush"),
]


def load():
    with open(IMG, "rb") as f:
        return f.read()


def dstr(data, va):
    """Decode a Delphi literal at VA, or None."""
    off = va - BASE
    if off < 8 or off >= len(data):
        return None
    ln = struct.unpack_from("<i", data, off - 4)[0]
    rc = struct.unpack_from("<i", data, off - 8)[0]
    if not (0 < ln < 512) or rc != -1:
        return None
    raw = data[off:off + ln]
    if any(b == 0 for b in raw):
        return None
    try:
        return raw.decode("gbk")
    except UnicodeDecodeError:
        return raw.decode("latin-1")


def emit(data, md, ea, limit, out):
    seen_end = False
    n = 0
    for ins in md.disasm(data[ea - BASE: ea - BASE + limit], ea):
        note = ""
        for tok in ins.op_str.replace(",", " ").split():
            if tok.startswith("0x") and len(tok) >= 6:
                try:
                    v = int(tok, 16)
                except ValueError:
                    continue
                s = dstr(data, v)
                if s:
                    note = "   ; '%s'" % s
        out.append("%08X  %-22s %s %s%s" % (
            ins.address, "".join("%02x" % b for b in ins.bytes),
            ins.mnemonic, ins.op_str, note))
        n += 1
        if ins.mnemonic == "ret":
            seen_end = True
            break
    if not seen_end:
        out.append("        ... (truncated at %d insns)" % n)


def main():
    data = load()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    only = set(int(a) for a in sys.argv[1:]) if len(sys.argv) > 1 else None
    out = []
    for ident, ea, name in HANDLERS:
        if only and ident not in only:
            continue
        out.append("")
        out.append("=" * 78)
        out.append("ident %d  ->  sub_%06X   (%s)" % (ident, ea, name))
        out.append("=" * 78)
        emit(data, md, ea, 0x400, out)
    print("\n".join(out))


if __name__ == "__main__":
    main()
