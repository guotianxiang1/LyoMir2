"""眼神脱壳转储 + M2Server 平坦镜像 的只读反汇编助手。

眼神:    base 0x10000000, staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin
M2Server: base 0x00400000, staging/_reunpack_work/flat_image.bin
"""
import os

from capstone import Cs, CS_ARCH_X86, CS_MODE_32

YS_PATH = (r"D:\loym2\staging\yanshen208_strparam_runtime_dump_20260719"
           r"\yanshen2_0_8_dll.memory.bin")
YS_BASE = 0x10000000
M2_PATH = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
M2_BASE = 0x00400000

_md = Cs(CS_ARCH_X86, CS_MODE_32)
_md.detail = False

_cache = {}


def blob(path):
    if path not in _cache:
        with open(path, "rb") as fh:
            _cache[path] = fh.read()
    return _cache[path]


def ys():
    return blob(YS_PATH)


def m2():
    return blob(M2_PATH)


def read(data, base, va, n):
    off = va - base
    if off < 0 or off + n > len(data):
        raise ValueError("VA 0x%08X out of range" % va)
    return data[off:off + n]


def hexb(bs):
    return " ".join("%02X" % b for b in bs)


def dis(data, base, va, count=1, maxbytes=None):
    """返回 [(va, bytes_hex, mnemonic, op_str)]"""
    n = maxbytes if maxbytes else count * 16 + 16
    n = min(n, len(data) - (va - base))
    code = read(data, base, va, n)
    out = []
    for i, ins in enumerate(_md.disasm(code, va)):
        if maxbytes is None and i >= count:
            break
        out.append((ins.address, hexb(ins.bytes), ins.mnemonic, ins.op_str))
    return out


def show(data, base, va, count=1, maxbytes=None, indent=""):
    lines = []
    for a, b, m, o in dis(data, base, va, count, maxbytes):
        lines.append("%s%08X  %-26s %-8s %s" % (indent, a, b, m, o))
    return "\n".join(lines)


def show_ys(va, count=1, maxbytes=None, indent=""):
    return show(ys(), YS_BASE, va, count, maxbytes, indent)


def show_m2(va, count=1, maxbytes=None, indent=""):
    return show(m2(), M2_BASE, va, count, maxbytes, indent)


def bytes_ys(va, n):
    return read(ys(), YS_BASE, va, n)


def bytes_m2(va, n):
    return read(m2(), M2_BASE, va, n)


def cstr(data, base, va, limit=256):
    off = va - base
    end = data.find(b"\x00", off, off + limit)
    if end < 0:
        end = off + limit
    raw = data[off:end]
    try:
        return raw.decode("gbk")
    except UnicodeDecodeError:
        return repr(raw)


def cstr_ys(va, limit=256):
    return cstr(ys(), YS_BASE, va, limit)


def findall(data, pat, start=0):
    out = []
    i = data.find(pat, start)
    while i >= 0:
        out.append(i)
        i = data.find(pat, i + 1)
    return out
