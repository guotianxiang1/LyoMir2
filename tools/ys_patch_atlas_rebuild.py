#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Rebuild the whole-library `patch_target_vas` from the status-label atlas.

Background
----------
`tools/ys_gui_matrix.py` annotates each GUI key with the M2Server addresses the
plugin patches for it.  Until now that annotation came from
`staging/_ysgui2/g09.json` (+ `g11.json`), which only ever enumerated the 306
call sites of the raw byte patcher `sub_10033340`.  The plugin has two more
installers -- the trampoline builders `sub_10032CC0` (71 sites) and
`sub_10032FD0` (30 sites) -- and those 101 sites were absent, so every feature
that is installed exclusively through a trampoline looked unpatched.

This script rebuilds the map from the primitive that covers all three
installers.  Every apply/revert arm ends with

    call 0x100F018C(labelObject, "<feature>(\u5df2\u542f\u52a8)" | "<feature>(\u672a\u542f\u52a8)")

so disassembling forward from an installer call site to the first status
literal attributes that site to a feature name, whichever installer it used.
This is the same primitive `tools/ys_page1_census.py` uses; the site->label
attribution is imported from there rather than re-implemented, so the two
outputs cannot drift.

Resolving the address a site patches
------------------------------------
ys-page2 took any `push imm` in 0x400000..0x800000 within the 14 instructions
before the call.  That is right for the 306 memcpy sites but clips 26 of the
101 trampoline sites, whose template-construction block sits between the
argument pushes and the call.  The builders' argument shape, read off the
prologue of sub_10032CC0 and confirmed by disassembly at e.g. 0x100cf09d:

    push  <len>                     ; arg5  template dword count
    push  <template>                ; arg4
    push  <hook end>                ; arg3
    push  <hook start>              ; arg2
    push  <hook start>              ; arg1
    push  <out object>              ; arg0
    call  0x10032cc0

so counting host-immediate pushes backwards from the call, p[-1] is the hook
start and p[-3] the hook end.  Widening the window to the previous installer
site and taking that trailing triple reproduces the conservative answer on all
67 trampoline sites where both rules fire, strictly extends it on 8 more (it
recovers the hook end the 14-instruction window had clipped), and attributes
the remaining 26.  Only the hook start is a patch target -- the bytes at
[start, end) are what the trampoline overwrites -- so `end` is carried as the
span bound, not as another target, which is also what g09.json's va/va_end
split means.

Outputs (under --out, default docs/)
------------------------------------
  ys_patch_sites_atlas.tsv   one row per installer site (407)   -- the record
  ys_patch_target_vas.tsv    one row per GUI key                -- the map
  ys_patch_atlas_diff.tsv    per-site diff against g09/g11      -- the delta

Usage:  python tools/ys_patch_atlas_rebuild.py [--repo DIR] [--out DIR]
"""

import argparse
import collections
import importlib.util
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_REPO = os.path.dirname(HERE)
DEFAULT_ATLAS_DIR = r"D:\loym2\staging\_ysgui2"


def arm_sense(literal, strip):
    """apply or revert, from the status literal the arm falls through to.

    Every suffix is either affirmative (\u5df2...) or negative (\u672a.../\u5f85...);
    \u6539\u7528\u65b0\u7248 is the one-off notice on \u6c99\u57ce\u5730\u56fe and is affirmative too.
    """
    m = strip.search(literal or "")
    if not m:
        return "?"
    suf = m.group(0)
    if suf.startswith("(\u672a") or suf.startswith("(\u5f85"):
        return "REVERT"
    return "APPLY"


def _load(repo, name):
    path = os.path.join(repo, "tools", name + ".py")
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


# ---------------------------------------------------------------------------
# incumbent map: what g09.json / g11.json know

def incumbent_sites(atlas_dir, strip):
    """site VA -> (label, target VA, byte count) for the two legacy atlases."""
    out = {}
    for fname, site_field, va_field, len_field in (
            ("g09.json", "call", "va", "len"),
            ("g11.json", "site", "target", "width")):
        path = os.path.join(atlas_dir, fname)
        if not os.path.exists(path):
            continue
        for row in json.load(open(path, encoding="utf-8")):
            label = strip.sub("", (row.get("label") or "")).strip()
            out[row[site_field]] = (label or None, row.get(va_field),
                                    row.get(len_field))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=DEFAULT_REPO)
    ap.add_argument("--atlas", default=DEFAULT_ATLAS_DIR)
    ap.add_argument("--out", default=None)
    args = ap.parse_args()
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

    repo = os.path.abspath(args.repo)
    out_dir = args.out or os.path.join(repo, "docs")
    os.makedirs(out_dir, exist_ok=True)

    census = _load(repo, "ys_page1_census")
    mx = _load(repo, "ys_gui_matrix")

    # ---- 1. the record: every installer site, attributed to a feature
    dump = census.Dump(census.DUMP_PLAIN, 0)
    labels = census.patch_labels(dump)

    sites = []
    for label, rows in labels.items():
        for addr, kind, hosts in rows:
            sites.append({"label": label, "site": addr, "kind": kind,
                          "conservative": sorted(hosts)})
    sites.sort(key=lambda r: r["site"])
    ordered = [r["site"] for r in sites]

    def status_literal(site):
        """the status string this arm falls through to, unstripped"""
        for x in census.md.disasm(dump.code(site, 0x900), site):
            if x.mnemonic == "push" and x.operands \
                    and x.operands[0].type == census.X86_OP_IMM:
                s = dump.cstr(x.operands[0].imm & 0xFFFFFFFF)
                if s and census.STATUS_SUFFIX.search(s):
                    return s
        return ""

    def arg_pushes(site):
        """every `push imm` since the previous installer site, in order"""
        prev = max([a for a in ordered if a < site], default=site - 0x800)
        return [x.operands[0].imm & 0xFFFFFFFF
                for x in dump.align(site, back=0x800)
                if x.address > prev and x.mnemonic == "push" and x.operands
                and x.operands[0].type == census.X86_OP_IMM]

    def host_pushes(site):
        return [v for v in arg_pushes(site) if 0x400000 <= v < 0x800000]

    def memcpy_len(site):
        """sub_10033340(payload, len, va, va): the count pushed after the VAs.

            push 0x6e7930 / push 0x6e7930 / push 8 / push eax / call
        """
        p = arg_pushes(site)
        last = max((i for i, v in enumerate(p) if 0x400000 <= v < 0x800000),
                   default=None)
        if last is None:
            return None
        for v in p[last + 1:]:
            if 0 < v <= 0x400:
                return v
        return None

    for r in sites:
        r["literal"] = status_literal(r["site"])
        r["sense"] = arm_sense(r["literal"], census.STATUS_SUFFIX)
        if r["kind"] == "memcpy":
            # single destination argument; the conservative window is exact here
            r["target"] = r["conservative"][0] if r["conservative"] else None
            n = memcpy_len(r["site"])
            r["end"] = (r["target"] + n) if (n and r["target"]) else None
        else:
            p = host_pushes(r["site"])
            if len(p) >= 3 and p[-1] == p[-2] and p[-3] > p[-1]:
                r["target"], r["end"] = p[-1], p[-3]
            else:                       # fall back, never invent
                r["target"] = r["conservative"][0] if r["conservative"] else None
                r["end"] = None

    by_kind = collections.Counter(r["kind"] for r in sites)
    print("installer sites: %d  %s" % (len(sites), dict(by_kind)))
    print("distinct feature labels: %d   sites with no resolved target VA: %d"
          % (len({r["label"] for r in sites}),
             sum(1 for r in sites if r["target"] is None)))
    print("kind x arm: %s" % dict(
        collections.Counter((r["kind"], r["sense"]) for r in sites)))

    path = os.path.join(out_dir, "ys_patch_sites_atlas.tsv")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("site_va\tkind\tarm\tlabel\ttarget_va\tspan_end\tspan_len\t"
                 "status_literal\tconservative_hosts\n")
        for r in sites:
            fh.write("%#010x\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n" % (
                r["site"], r["kind"], r["sense"], r["label"],
                ("%#08x" % r["target"]) if r["target"] else "-",
                ("%#08x" % r["end"]) if r["end"] else "-",
                (r["end"] - r["target"]) if (r["end"] and r["target"]) else "-",
                r["literal"],
                " ".join("%#08x" % v for v in r["conservative"]) or "-"))
    print("wrote", path)

    # ---- 2. the delta: per site, against the legacy atlases
    old = incumbent_sites(args.atlas, census.STATUS_SUFFIX)
    new_sites = [r for r in sites if r["site"] not in old]
    shared = [r for r in sites if r["site"] in old]
    disagree = [r for r in shared if old[r["site"]][0] != r["label"]]
    print("legacy atlas sites: %d   shared with rebuild: %d   "
          "rebuild-only: %d   label disagreements on shared: %d"
          % (len(old), len(shared), len(new_sites), len(disagree)))

    va_mismatch = [r for r in shared
                   if old[r["site"]][1] and r["target"] != old[r["site"]][1]]
    print("target VA disagreements on shared sites: %d" % len(va_mismatch))
    for r in va_mismatch[:10]:
        print("    %#010x %s rebuild=%s legacy=%#08x"
              % (r["site"], r["label"],
                 ("%#08x" % r["target"]) if r["target"] else "-",
                 old[r["site"]][1]))

    len_mismatch = [r for r in shared
                    if old[r["site"]][2] and r["end"] and r["target"]
                    and (r["end"] - r["target"]) != old[r["site"]][2]]
    unsized = [r for r in shared if r["end"] is None]
    print("byte-count disagreements on shared sites: %d   (unsized: %d)"
          % (len(len_mismatch), len(unsized)))
    for r in len_mismatch[:10]:
        print("    %#010x %s rebuild=%d legacy=%s"
              % (r["site"], r["label"], r["end"] - r["target"], old[r["site"]][2]))

    # The point of the exercise: do the 101 rebuild-only sites reach any address
    # the legacy atlas did not?  They are all apply arms, and every feature's
    # revert arm memcpy's the same bytes back, so the answer is expected to be no.
    mem_t = {r["target"] for r in sites if r["kind"] == "memcpy"}
    tramp_t = {r["target"] for r in sites if r["kind"] != "memcpy"}
    print("distinct memcpy targets %d | trampoline targets %d | "
          "trampoline targets no memcpy site covers: %d %s"
          % (len(mem_t), len(tramp_t), len(tramp_t - mem_t),
             sorted("%#08x" % v for v in tramp_t - mem_t)))

    path = os.path.join(out_dir, "ys_patch_atlas_diff.tsv")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("site_va\tkind\tstatus\tlabel\tlegacy_label\ttarget_va\t"
                 "legacy_target_va\tspan_end\n")
        for r in sites:
            prev = old.get(r["site"])
            if prev is None:
                status = "NEW"
            elif prev[0] != r["label"]:
                status = "RELABELLED"
            else:
                status = "SAME"
            fh.write("%#010x\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n" % (
                r["site"], r["kind"], status, r["label"],
                (prev[0] if prev else "") or "",
                ("%#08x" % r["target"]) if r["target"] else "-",
                ("%#08x" % prev[1]) if (prev and prev[1]) else "-",
                ("%#08x" % r["end"]) if r["end"] else "-"))
    print("wrote", path)

    # ---- 3. the map: feature label -> GUI key
    cfg, _ = mx.load_config(mx.DEFAULT_CONFIG)
    rebuilt = collections.defaultdict(set)         # label -> patch target VAs
    for r in sites:
        if r["target"]:
            rebuilt[r["label"]].add(r["target"])

    legacy = mx.load_atlas(args.atlas)
    extreme = mx.load_extreme_map(repo)

    def resolve(key, table):
        """the matrix's own lookup rule, so the comparison is like-for-like"""
        return table.get(key) or table.get(key.rstrip("a")) or set()

    path = os.path.join(out_dir, "ys_patch_target_vas.tsv")
    matched, gained, keys_new = 0, 0, []
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("key\tsource\tn_sites\tkinds\ttarget_vas\tlegacy_target_vas\tdelta\n")
        for key in sorted(cfg):
            hosts = resolve(key, rebuilt)
            ext = resolve(key, extreme)
            old_hosts = resolve(key, legacy) | ext
            src = []
            if hosts:
                src.append("label-atlas")
            if ext:
                src.append("extreme-map")
            hosts = hosts | ext
            if not hosts and not old_hosts:
                continue
            matched += 1
            krows = [r for r in sites
                     if r["label"] == key or r["label"] == key.rstrip("a")]
            delta = hosts - old_hosts
            if delta:
                gained += 1
            if not old_hosts:
                keys_new.append(key)
            fh.write("%s\t%s\t%d\t%s\t%s\t%s\t%s\n" % (
                key, "+".join(src) or "legacy-only", len(krows),
                " ".join(sorted({r["kind"] for r in krows})) or "-",
                " ".join("%#08x" % v for v in sorted(hosts)),
                " ".join("%#08x" % v for v in sorted(old_hosts)) or "-",
                " ".join("%#08x" % v for v in sorted(delta)) or "-"))
    print("config keys with any patch target: %d   of which gained VAs: %d   "
          "newly non-empty: %d" % (matched, gained, len(keys_new)))
    print("wrote", path)

    orphan = sorted(l for l in rebuilt if l not in cfg and l.rstrip("a") not in cfg)
    print("feature labels with no config key (plugin-internal): %d" % len(orphan))
    for l in orphan:
        print("   ", l)
    return 0


if __name__ == "__main__":
    sys.exit(main())
