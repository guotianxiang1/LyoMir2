#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Yanshen page2 + extension keys: field offset, consumer class (logic/gui/zero)."""

import argparse
import collections
import io
import json
import os
import struct
import sys

from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM, X86_OP_MEM

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_REPO = os.path.dirname(HERE)
STAGING = r"D:\loym2\staging"
DUMP_PLAIN = os.path.join(STAGING, "yanshen208_strparam_runtime_dump_20260719",
                          "yanshen2_0_8_dll.memory.bin")
DUMP_DELAYED = os.path.join(STAGING, "yanshen208_strparam_runtime_dump_delayed_20260719",
                            "yanshen2_0_8_dll.memory.bin")
BASE = 0x10000000
DELAYED_DELTA = 0x47C40000
SERIALIZER = (0x10004140, 0x1000A75D)
LOADER = (0x100D6220, 0x100D7E18)
STATUS = 0x100F018C
TARGET_PAGES = (
    "\u773c\u795e2(\u7b2c2\u9875)",
    "\u6269\u5c55/\u6280\u80fd\u76f8\u5173",
    "\u6269\u5c55/\u7269\u54c1\u76f8\u5173",
    "\u6269\u5c55/\u811a\u672c\u76f8\u5173",
    "\u6269\u5c55/\u89d2\u8272\u76f8\u5173",
)

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True


class Dump:
    def __init__(self, path, delta):
        self.buf = open(path, "rb").read()
        self.delta = delta

    def code(self, va, n):
        return self.buf[va - BASE:va - BASE + n]


def key_fields(dump):
    ins = list(md.disasm(dump.code(SERIALIZER[0], SERIALIZER[1] - SERIALIZER[0]),
                         SERIALIZER[0]))
    recent = []
    out = collections.OrderedDict()
    for x in ins:
        for op in x.operands:
            if op.type == X86_OP_MEM and op.mem.base and not op.mem.index \
                    and op.mem.disp >= 0x80:
                recent.append((x.address, op.mem.disp, x.mnemonic))
        if x.mnemonic == "push" and x.operands and x.operands[0].type == X86_OP_IMM:
            s = dump.cstr(x.operands[0].imm & 0xFFFFFFFF) if hasattr(dump, "cstr") else None
            if not hasattr(dump, "cstr"):
                o = (x.operands[0].imm & 0xFFFFFFFF) - BASE
                if 0 <= o < len(dump.buf):
                    e = dump.buf.find(b"\x00", o, o + 96)
                    s = dump.buf[o:e].decode("gbk", "replace") if e > o else None
            if s and s not in out and recent:
                addr, disp, mn = recent[-1]
                out[s] = (disp, mn, addr, x.address)
    return out


def dump_cstr(self, v, maxlen=96):
    o = v - BASE
    if o <= 0 or o >= len(self.buf):
        return None
    e = self.buf.find(b"\x00", o, o + maxlen)
    if e <= o:
        return None
    try:
        return self.buf[o:e].decode("gbk")
    except UnicodeDecodeError:
        return None


Dump.cstr = dump_cstr


def switch_reads(dump, offset):
    hits = []
    for reg in range(8):
        if reg == 4:
            pat = bytes([0x81, 0xB8 | reg, 0x24])
        else:
            pat = bytes([0x81, 0xB8 | reg])
        pat += struct.pack("<I", offset) + struct.pack("<I", 0x1F4)
        i = dump.buf.find(pat)
        while i >= 0:
            hits.append(BASE + i)
            i = dump.buf.find(pat, i + 1)
    return sorted(hits)


def outside_loader_serializer(va):
    return not (SERIALIZER[0] <= va < SERIALIZER[1] or LOADER[0] <= va < LOADER[1])


def classify_site(dump, va):
    chunk = dump.code(va, 0x40)
    ins = list(md.disasm(chunk, va))[:8]
    for x in ins[1:8]:
        if x.mnemonic == "call" and x.op_str.endswith("0x100f018c"):
            return "gui_label"
    if b"\xff\x53\x48" in dump.code(va, 0x180):
        return "logic_dispatch48"
    if va == 0x10067DB3:
        return "logic_themida"
    if any(x.mnemonic == "call" and "0x10056040" in x.op_str for x in ins[1:8]):
        return "logic_gets"
    return "logic"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=DEFAULT_REPO)
    ap.add_argument("--out", default=None)
    args = ap.parse_args()
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
    repo = os.path.abspath(args.repo)
    out_dir = args.out or os.path.join(repo, "docs")

    sys.path.insert(0, os.path.join(repo, "tools"))
    import importlib.util
    spec = importlib.util.spec_from_file_location(
        "mx", os.path.join(repo, "tools", "ys_gui_matrix.py"))
    mx = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mx)
    cfg, _ = mx.load_config(mx.DEFAULT_CONFIG)
    panels = mx.panel_pages(repo)
    main_keys, explicit = mx.catalog_pages(repo)

    def page_of(key):
        if mx.is_equipment_param(key):
            return "\u76d8\u53e44"
        if key in panels:
            return panels[key][0]
        if key in main_keys:
            return main_keys[key]
        if key in explicit:
            owner = explicit[key]
            return panels.get(owner, (main_keys.get(owner, ""), ""))[0] or main_keys.get(owner, "")
        base = key.split("_", 1)[0]
        owner = panels.get(base, (None, None))[0] or main_keys.get(base)
        if owner and "_" in key:
            return owner
        return "\u6269\u5c55/" + mx.category_for(key)

    plain = Dump(DUMP_PLAIN, 0)
    delayed = Dump(DUMP_DELAYED, DELAYED_DELTA)
    fields = key_fields(plain)

    rows = []
    for key in sorted(cfg):
        pg = page_of(key)
        if pg not in TARGET_PAGES:
            continue
        info = fields.get(key)
        disp = info[0] if info else None
        sites_d = []
        if disp is not None:
            sites_d = [v for v in switch_reads(delayed, disp) if outside_loader_serializer(v)]
        classes = [classify_site(delayed, v) for v in sites_d]
        logic = [v for v, c in zip(sites_d, classes) if c.startswith("logic")]
        verdict = "zero"
        if logic:
            verdict = "blocked" if any("themida" in c for c in classes) else "logic"
        elif sites_d:
            verdict = "gui_only"
        rows.append((key, pg, cfg[key], disp, sites_d, classes, verdict))

    path = os.path.join(out_dir, "ys_page2ext_census.tsv")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("key\tpage\tprod_value\tcfg_field\tconsumers_delayed\tclasses\tverdict\n")
        for key, pg, val, disp, sites, classes, verdict in rows:
            fh.write("%s\t%s\t%s\t%s\t%s\t%s\t%s\n" % (
                key, pg, json.dumps(val, ensure_ascii=False),
                ("%#05x" % disp) if disp else "?",
                " ".join("%#010x" % v for v in sites) or "-",
                " ".join(classes) or "-",
                verdict))

    counts = collections.Counter(r[-1] for r in rows)
    print("page2+ext keys:", len(rows), dict(counts))
    print("wrote", path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
