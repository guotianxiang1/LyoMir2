#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Recover the 随机极品 (random-superior) key -> host immediate map.

The 盘古4 GUI page carries 96 numeric knobs (6 equipment classes x 5 attributes
x {最高点数, 点数几率, 属性几率}, plus one 最随机性_极品 per class).  A previous
pass located six blocks of host immediates that the plugin overwrites but could
not say which knob drives which immediate, so the whole page stayed LABEL_ONLY.

This script closes that gap from the unpacked plugin dump (base 0x10000000):

  1. config loader  0x100C6DE0..0x100CB800
       push "<key>" ... mov eax,[0x1031C5BC] / add eax,OFF
     gives  key -> singleton string-field offset.

  2. apply function sub_100BF430, an unrolled

       for i in 0..95:
           v = atoi(text[i])
           if v > 0 and v != cache[i]:  patch(host[i], v)   # mov [hostVA], eax
           cache[i] = v                                     # mov [esi+CACHE], eax
           singleton.str[i].assign(text[i])                 # add eax,OFF ; assign

     so the host store and the cache store sit *before* the singleton offset of
     the slot they belong to.  Two independent checks pin that phase down: the
     cache offsets must come out as the strict grid 0x2EC+4i, and each of the
     six equipment classes must land in one contiguous ~0xCA-byte host block.

Joining the two gives key -> host VA, and disassembling one instruction back
from the host VA gives the instruction whose immediate is being replaced, i.e.
the exact semantics of the knob.

Usage:  python tools/ys_extreme_map.py [--dump PATH] [--out FILE]
"""

import argparse
import json
import os
import re
import struct
import sys

BASE = 0x10000000
SINGLETON = 0x1031C5BC          # the 0x19B8-byte config singleton
APPLY_LO, APPLY_HI = 0x100BF430, 0x100C6600
LOADER_LO, LOADER_HI = 0x100C0000, 0x100D0000
TEXT_RVA, TEXT_SIZE = 0x1000, 0x27BC40

DEFAULT_DUMP = ("D:\\loym2\\staging\\yanshen208_strparam_runtime_dump_20260719"
                "\\yanshen2_0_8_dll.memory.bin")
DEFAULT_M2 = "D:\\loym2\\staging\\_reunpack_work\\flat_image.bin"
M2_BASE = 0x400000
DEFAULT_CONFIG = "D:\\\u5149\u5934\u5367\u9f99\\mud2.0\\Mir200\\Gs1\\config.json"

EQUIP = ["武器", "衣服", "头盔", "项链", "手镯", "戒指"]
ATTR = ["攻击", "魔法", "道术", "攻速", "准确"]
FAM = ["最高点数", "点数几率", "属性几率"]


# --------------------------------------------------------------------------

class Img:
    def __init__(self, path):
        with open(path, "rb") as fh:
            self.buf = fh.read()
        self._idx = None

    def at(self, va, n):
        return self.buf[va - BASE:va - BASE + n]

    def find_str(self, text):
        needle = text.encode("gbk")
        out, i = [], self.buf.find(needle)
        while i >= 0:
            if i > 0 and self.buf[i - 1] == 0:
                out.append(BASE + i)
            i = self.buf.find(needle, i + 1)
        return out

    def dword_index(self):
        if self._idx is None:
            idx = {}
            b = self.buf
            for i in range(TEXT_RVA, TEXT_RVA + TEXT_SIZE - 4):
                v = struct.unpack_from("<I", b, i)[0]
                if 0x10001000 <= v < 0x1031D000:
                    idx.setdefault(v, []).append(BASE + i)
            self._idx = idx
        return self._idx


_md = None


def md():
    global _md
    if _md is None:
        from capstone import Cs, CS_ARCH_X86, CS_MODE_32
        _md = Cs(CS_ARCH_X86, CS_MODE_32)
    return _md


def dis(img, va, n):
    out = []
    for ins in md().disasm(img.at(va, n * 8 + 32), va):
        out.append(ins)
        if len(out) >= n:
            break
    return out


def txt(ins):
    return ins.mnemonic + " " + ins.op_str


# --------------------------------------------------------------------------
# grid: the 96 knobs occupy one contiguous run of 24-byte std::string slots

def build_grid():
    grid = {}
    for ei, e in enumerate(EQUIP):
        base = 0xF10 + ei * 0x180
        for ai, a in enumerate(ATTR):
            for fi, f in enumerate(FAM):
                grid[base + ai * 0x48 + fi * 0x18] = "%s%s_%s_值" % (e, f, a)
        grid[base + 0x168] = "%s最随机性_极品_值" % e
    return grid


def loader_offset(img, key):
    """key -> singleton offset, read straight out of the config loader."""
    vas = img.find_str(key)
    if len(vas) != 1:
        return None, None
    idx = img.dword_index()
    for site in sorted(x for x in idx.get(vas[0], []) if LOADER_LO <= x < LOADER_HI):
        pend = None
        for ins in dis(img, site, 90):
            t = txt(ins)
            if "[0x%x]" % SINGLETON in t:
                pend = ins.address
                continue
            if pend is None:
                continue
            m = re.match(r"add \w+, (0x[0-9a-f]+)$", t) or \
                re.match(r"lea \w+, \[\w+ \+ (0x[0-9a-f]+)\]$", t)
            if m:
                return int(m.group(1), 16), site
            if ins.address - pend > 32:
                pend = None
    return None, None


def apply_pairs(img, grid):
    """singleton offset -> (host VA, cache offset) from sub_100BF430."""
    seq, pend = [], None
    for ins in dis(img, APPLY_LO, 60000):
        if ins.address >= APPLY_HI:
            break
        t = txt(ins)
        if "[0x%x]" % SINGLETON in t:
            pend = ins.address
            continue
        if pend is not None:
            m = re.match(r"(?:add|lea) \w+,? ?(?:\[?\w+ \+ )?(0x[0-9a-f]+)\]?$", t)
            if m:
                v = int(m.group(1), 16)
                if v in grid:
                    seq.append(("SING", v, ins.address))
                pend = None
                continue
            if ins.address - pend > 32:
                pend = None
        m = re.match(r"mov dword ptr \[(0x[0-9a-f]+)\], eax$", t)
        if m and 0x401000 <= int(m.group(1), 16) < 0xB00000:
            seq.append(("HOST", int(m.group(1), 16), ins.address))
            continue
        m = re.match(r"mov dword ptr \[esi \+ (0x[0-9a-f]+)\], eax$", t)
        if m:
            seq.append(("CACHE", int(m.group(1), 16), ins.address))

    out = {}
    for n, (kind, v, addr) in enumerate(seq):
        if kind != "SING":
            continue
        host = cache = site = None
        for kind2, v2, a2 in reversed(seq[:n]):
            if kind2 == "SING":
                break
            if kind2 == "HOST" and host is None:
                host, site = v2, a2
            elif kind2 == "CACHE" and cache is None:
                cache = v2
        out[v] = (host, cache, site)
    return out


def host_instruction(m2, host_va):
    """The M2Server instruction whose 4-byte immediate lives at host_va."""
    if m2 is None:
        return ""
    for back in (1, 2, 3, 5, 6):
        start = host_va - back
        code = m2[start - M2_BASE:start - M2_BASE + 16]
        ins = list(md().disasm(code, start))
        if ins and ins[0].size == back + 4:
            raw = " ".join("%02X" % c for c in ins[0].bytes)
            return "0x%x %s %s" % (start, raw, txt(ins[0]))
    return "?"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", default=DEFAULT_DUMP)
    ap.add_argument("--m2", default=DEFAULT_M2)
    ap.add_argument("--config", default=DEFAULT_CONFIG)
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    img = Img(args.dump)
    m2 = None
    if os.path.exists(args.m2):
        with open(args.m2, "rb") as fh:
            m2 = fh.read()
    grid = build_grid()

    cfg = {}
    if os.path.exists(args.config):
        with open(args.config, "rb") as fh:
            cfg = json.loads(fh.read().decode("gbk"))

    # verify the grid against the loader rather than trusting the arithmetic
    verified = 0
    for off, key in sorted(grid.items()):
        got, _ = loader_offset(img, key)
        if got == off:
            verified += 1
        elif got is not None:
            print("MISMATCH %s grid=0x%x loader=0x%x" % (key, off, got))
    print("grid slots confirmed by the config loader: %d/96" % verified)

    pairs = apply_pairs(img, grid)
    print("slots paired with a host store: %d/96" % len(pairs))

    rows = []
    for off in sorted(grid):
        key = grid[off]
        host, cache, site = pairs.get(off, (None, None, None))
        rows.append({
            "key": key,
            "sing_off": off,
            "host_va": host,
            "cache_off": cache,
            "apply_site": site,
            "host_insn": host_instruction(m2, host) if host else "",
            "prod_value": cfg.get(key, ""),
        })

    hosts = [r["host_va"] for r in rows if r["host_va"]]
    dup = sorted({h for h in hosts if hosts.count(h) > 1})
    if dup:
        print("duplicate host VAs: %s" % [hex(h) for h in dup])
        for r in rows:
            if r["host_va"] in dup:
                print("   0x%x  %s  (sing 0x%x)" % (r["host_va"], r["key"], r["sing_off"]))
    print("distinct host VAs: %d" % len(set(hosts)))

    # phase check 1: the applied-value caches must be the strict grid 0x2EC+4i
    caches = [r["cache_off"] for r in rows]
    want = list(range(0x2EC, 0x2EC + 96 * 4, 4))
    print("cache grid 0x2EC+4i: %s" % ("OK" if caches == want else "BROKEN %s" % caches[:6]))

    # phase check 2: each equipment class must occupy one contiguous host block
    for e in EQUIP:
        hv = [r["host_va"] for r in rows if r["key"].startswith(e) and r["host_va"]]
        print("  %s  n=%2d  0x%x..0x%x  width=0x%x" %
              (e, len(hv), min(hv), max(hv), max(hv) - min(hv)))

    out = args.out or os.path.join(os.path.dirname(os.path.dirname(
        os.path.abspath(__file__))), "docs", "ys_extreme_map.tsv")
    with open(out, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("key\tprod_value\tsing_off\thost_va\tcache_off\tapply_site\thost_insn\n")
        for r in rows:
            fh.write("%s\t%s\t0x%x\t%s\t%s\t%s\t%s\n" % (
                r["key"], r["prod_value"], r["sing_off"],
                "0x%x" % r["host_va"] if r["host_va"] else "",
                "0x%x" % r["cache_off"] if r["cache_off"] else "",
                "0x%x" % r["apply_site"] if r["apply_site"] else "",
                r["host_insn"]))
    print("wrote", out)


if __name__ == "__main__":
    main()
