#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Yanshen config key -> runtime field -> consumer census.

Answers, per config key, the two questions the GUI matrix cannot:

  1. Does the plugin patch M2Server for this key?
     Every apply/revert arm finishes with
         call 0x100F018C(labelObject, "<key>(已启动)" | "<key>(未启动)")
     so walking forward from each of the 407 patch installer call sites to the
     next status label attributes the site to a feature name.  A key that never
     appears as a label installs no patch, whatever the GUI says.

  2. Does the plugin's own code ever read this key?
     The loader re-encodes a boolean switch as rand()%1000+1000 (on) or a small
     modulus (off) and every consumer tests `cmp dword [reg+OFF], 0x1F4`.  The
     field offset comes from the JSON serializer sub_10004140, where each key
     literal is pushed right after the field it serialises.  Scanning the whole
     dump for that byte pattern finds consumers regardless of how the base
     register was computed, which pointer tracking cannot do (the apply arms
     receive the object as `this`).

Both dumps are scanned.  Only the delayed one contains the 16 MB Themida region
0x10400000..0x11400000 (all zero in the other), so a claim of "no consumer"
is only sound when it is made against the delayed dump.

Usage:  python tools/ys_page1_census.py [--repo DIR] [--out DIR]
"""

import argparse
import collections
import io
import json
import os
import re
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
DELAYED_DELTA = 0x47C40000          # the delayed dump was taken at base 0x57C40000

SERIALIZER = (0x10004140, 0x1000A75D)
LOADER = (0x100D6220, 0x100D7E18)
BUILDER1 = 0x10032CC0               # trampoline builder, 71 sites
BUILDER2 = 0x10032FD0               # trampoline builder, 30 sites
MEMCPY = 0x10033340                 # raw byte patcher, 306 sites
STATUS = 0x100F018C                 # status label setter
STATUS_SUFFIX = re.compile(
    r"\((?:已启动|未启动|已启用|未启用|已关闭|未关闭|已设置|未设置|已重设|待重设|改用新版)\)$")

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = True


class Dump:
    def __init__(self, path, delta):
        self.buf = open(path, "rb").read()
        self.delta = delta          # add to a nominal VA to get this dump's operand value

    def norm(self, v):
        """map an absolute operand of this dump into nominal (0x10000000) space"""
        if self.delta and BASE + self.delta <= v < BASE + self.delta + len(self.buf):
            return v - self.delta
        return v

    def cstr(self, v, maxlen=96):
        v = self.norm(v)
        if not (0x102A0000 <= v < 0x1031D000):
            return None
        o = v - BASE
        if o <= 0 or o >= len(self.buf) or self.buf[o - 1] != 0:
            return None
        e = self.buf.find(b"\x00", o, o + maxlen)
        if e < 0 or e - o < 2:
            return None
        try:
            return self.buf[o:e].decode("gbk")
        except UnicodeDecodeError:
            return None

    def code(self, va, n):
        return self.buf[va - BASE:va - BASE + n]

    def align(self, anchor, back=0x400):
        """decode start whose instruction stream lands exactly on `anchor`"""
        for st in range(anchor - back, anchor):
            ins = list(md.disasm(self.code(st, anchor - st), st))
            if ins and ins[-1].address + ins[-1].size == anchor:
                return ins
        return []


# ---------------------------------------------------------------------------
# 1. key -> runtime config field offset, from the JSON serializer

def key_fields(dump):
    """key -> (field offset, the instruction that touched it)."""
    ins = list(md.disasm(dump.code(SERIALIZER[0], SERIALIZER[1] - SERIALIZER[0]),
                         SERIALIZER[0]))
    recent = []
    out = collections.OrderedDict()
    for x in ins:
        for op in x.operands:
            # the serialiser keeps the config object in a register and the
            # std::string scratch at [reg+0..8]; only real fields are >= 0x80
            if op.type == X86_OP_MEM and op.mem.base and not op.mem.index \
                    and op.mem.disp >= 0x80:
                recent.append((x.address, op.mem.disp, x.mnemonic))
        if x.mnemonic == "push" and x.operands and x.operands[0].type == X86_OP_IMM:
            s = dump.cstr(x.operands[0].imm & 0xFFFFFFFF)
            if s and s not in out and recent:
                addr, disp, mn = recent[-1]
                out[s] = (disp, mn, addr, x.address)
    return out


# ---------------------------------------------------------------------------
# 2. patch installer sites -> status label

def patch_labels(dump):
    """feature label -> [(site VA, builder kind, [M2Server VAs])]"""
    kinds = {BUILDER1 + dump.delta: "tramp1",
             BUILDER2 + dump.delta: "tramp2",
             MEMCPY + dump.delta: "memcpy"}
    b = dump.buf
    sites = []
    for i in range(0, len(b) - 5):
        if b[i] == 0xE8:
            t = BASE + i + 5 + struct.unpack_from("<i", b, i + 1)[0] + dump.delta
            if t in kinds:
                sites.append((BASE + i, kinds[t]))

    out = collections.defaultdict(list)
    for addr, kind in sites:
        hosts = sorted({(x.operands[0].imm & 0xFFFFFFFF)
                        for x in dump.align(addr)[-14:]
                        if x.mnemonic == "push" and x.operands
                        and x.operands[0].type == X86_OP_IMM
                        and 0x400000 <= (x.operands[0].imm & 0xFFFFFFFF) < 0x800000})
        label = None
        for x in md.disasm(dump.code(addr, 0x900), addr):
            if x.mnemonic == "push" and x.operands and x.operands[0].type == X86_OP_IMM:
                s = dump.cstr(x.operands[0].imm & 0xFFFFFFFF)
                if s and STATUS_SUFFIX.search(s):
                    label = STATUS_SUFFIX.sub("", s)
                    break
        out[label or "<unlabelled>"].append((addr, kind, hosts))
    return out


# ---------------------------------------------------------------------------
# 3. switch consumers, by byte pattern

def switch_reads(dump, offset):
    """VAs of `cmp dword [reg+offset], 0x1F4` anywhere in the dump."""
    hits = []
    for reg in range(8):
        if reg == 4:                       # esp needs a SIB byte
            pat = bytes([0x81, 0xB8 | reg, 0x24])
        else:
            pat = bytes([0x81, 0xB8 | reg])
        pat += struct.pack("<I", offset) + struct.pack("<I", 0x1F4)
        i = dump.buf.find(pat)
        while i >= 0:
            hits.append(BASE + i)
            i = dump.buf.find(pat, i + 1)
    return sorted(hits)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=DEFAULT_REPO)
    ap.add_argument("--out", default=None)
    ap.add_argument("--page", default="\u773c\u795e2(\u7b2c1\u9875)")
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
            p = panels.get(owner, (main_keys.get(owner, ""), ""))[0] or main_keys.get(owner, "")
            return p or "\u6269\u5c55/" + mx.category_for(key)
        base = key.split("_", 1)[0]
        owner = panels.get(base, (None, None))[0] or main_keys.get(base)
        if owner and "_" in key:
            return owner
        return "\u6269\u5c55/" + mx.category_for(key)

    plain = Dump(DUMP_PLAIN, 0)
    delayed = Dump(DUMP_DELAYED, DELAYED_DELTA)
    far = delayed.buf[0x400000:0x1400000]
    print("delayed dump Themida region 0x10400000..0x11400000: %d/%d nonzero bytes"
          % (sum(1 for c in far if c), len(far)))

    fields = key_fields(plain)
    labels = patch_labels(plain)
    print("keys with a resolved field: %d   patch feature labels: %d   installer sites: %d"
          % (len(fields), len(labels), sum(len(v) for v in labels.values())))

    atlas = os.path.join(out_dir, "ys_patch_label_atlas.tsv")
    with open(atlas, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("label\tsites\tinstaller_vas\tkinds\tm2server_target_vas\n")
        for label in sorted(labels):
            rows = labels[label]
            hosts = sorted({v for _, _, h in rows for v in h})
            fh.write("%s\t%d\t%s\t%s\t%s\n" % (
                label, len(rows),
                " ".join("%#010x" % a for a, _, _ in rows),
                " ".join(sorted({k for _, k, _ in rows})),
                " ".join("%#08x" % v for v in hosts)))

    census = os.path.join(out_dir, "ys_page1_census.tsv")
    rows = []
    for key in sorted(cfg):
        if page_of(key) != args.page:
            continue
        info = fields.get(key)
        disp, mn = (info[0], info[1]) if info else (None, "")
        reads_p = switch_reads(plain, disp) if disp else []
        reads_d = switch_reads(delayed, disp) if disp else []
        def outside(v):
            return not (SERIALIZER[0] <= v < SERIALIZER[1] or LOADER[0] <= v < LOADER[1])
        cons_p = [v for v in reads_p if outside(v)]
        cons_d = [v for v in reads_d if outside(v)]
        rows.append((key, cfg[key], disp, mn, labels.get(key), cons_p, cons_d))

    with open(census, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("key\tprod_value\tcfg_field\tserializer_op\tpatch_label"
                 "\tconsumers_plain\tconsumers_delayed\n")
        for key, val, disp, mn, lab, cp, cd in rows:
            fh.write("%s\t%s\t%s\t%s\t%s\t%s\t%s\n" % (
                key, json.dumps(val, ensure_ascii=False),
                ("%#05x" % disp) if disp else "?", mn,
                "yes" if lab else "no",
                " ".join("%#010x" % v for v in cp) or "-",
                " ".join("%#010x" % v for v in cd) or "-"))

    inert = [r for r in rows if not r[4] and not r[6]]
    print("page %s: %d keys, %d with a patch label, %d with a plugin-side consumer, "
          "%d inert" % (args.page, len(rows), sum(1 for r in rows if r[4]),
                        sum(1 for r in rows if r[6]), len(inert)))
    print("wrote", atlas)
    print("wrote", census)
    return 0


if __name__ == "__main__":
    sys.exit(main())
