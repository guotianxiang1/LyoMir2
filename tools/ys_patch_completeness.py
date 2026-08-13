#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Re-score every Yanshen GUI key against the rebuilt patch atlas.

`tools/ys_gui_matrix.py` decides a key's state purely from this repository's C#
sources: MISSING means no .cs file mentions the key, LABEL_ONLY means only an
options/DTO class does.  That says nothing about whether the original plugin
actually changes M2Server behaviour for the key, so a MISSING key can be either
a real gap or a knob that was inert to begin with.  This script supplies the
second axis from the binary and crosses the two.

Two independent binary-side signals, both whole-library:

  native patch   the key appears as a feature label in the rebuilt site atlas
                 (docs/ys_patch_sites_atlas.tsv), i.e. some apply arm writes
                 M2Server .text for it.  ys_extreme_map.tsv covers the 96
                 random-superior knobs whose apply arm stores with a plain
                 `mov [absolute], eax` and so has no installer call at all.

  plugin read    the loader re-encodes a boolean as rand()%1000+1000 (on) or a
                 small modulus (off) and every consumer tests
                 `cmp dword [reg+OFF], 0x1F4`.  One pass over the dump collects
                 every such compare and buckets it by OFF, which the JSON
                 serializer sub_10004140 maps back to a key.  Reads inside the
                 serializer and the loader are excluded -- those are the write
                 and parse sides, not consumers.

Only the delayed dump has the 16 MB Themida region 0x10400000..0x11400000
resolved, so "no consumer" is only asserted against that dump.

Verdicts
--------
  NATIVE_GAP            plugin patches M2Server, this repo has no engine-tier
                        consumer -> a real hole, ranked by patch surface
  NATIVE_OK             plugin patches M2Server, repo implements it
  PARAM_OF_PATCHED      no patch of its own, but the key is `<feature>_<param>`
                        and <feature> is patched -- these are the numbers baked
                        into that feature's payload, so they belong to it and
                        are not independently portable
  PLUGIN_SIDE_ONLY      no M2Server patch, but plugin code reads the key
  EQUIVALENT_BY_ABSENCE no patch and no read anywhere in 45 MB -> nothing to port

Usage:  python tools/ys_patch_completeness.py [--repo DIR] [--out DIR]
"""

import argparse
import collections
import csv
import importlib.util
import io
import json
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_REPO = os.path.dirname(HERE)
SWITCH_ON = 0x1F4


def _load(repo, name):
    spec = importlib.util.spec_from_file_location(
        name, os.path.join(repo, "tools", name + ".py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def switch_compares(dump):
    """offset -> [VA] for every `cmp dword [reg+off32], 0x1F4` in the dump.

    One linear pass instead of one 45 MB scan per key.  ModRM 0xB8..0xBF is
    opcode-extension /7 (cmp) with a disp32; reg 4 (esp) additionally needs a
    SIB byte, which shifts the two immediates along by one.
    """
    buf = dump.buf
    out = collections.defaultdict(list)
    i = buf.find(b"\x81")
    while i >= 0:
        if i + 11 <= len(buf):
            modrm = buf[i + 1]
            if 0xB8 <= modrm <= 0xBF:
                j = i + 3 if (modrm & 7) == 4 else i + 2
                if j + 8 <= len(buf) and \
                        struct.unpack_from("<I", buf, j + 4)[0] == SWITCH_ON:
                    out[struct.unpack_from("<I", buf, j)[0]].append(
                        dump.norm(0x10000000 + i))
        i = buf.find(b"\x81", i + 1)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=DEFAULT_REPO)
    ap.add_argument("--out", default=None)
    args = ap.parse_args()
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
    repo = os.path.abspath(args.repo)
    out_dir = args.out or os.path.join(repo, "docs")
    os.makedirs(out_dir, exist_ok=True)

    census = _load(repo, "ys_page1_census")
    mx = _load(repo, "ys_gui_matrix")

    # ---- binary side
    plain = census.Dump(census.DUMP_PLAIN, 0)
    delayed = census.Dump(census.DUMP_DELAYED, census.DELAYED_DELTA)
    fields = census.key_fields(plain)
    cmps = {"plain": switch_compares(plain), "delayed": switch_compares(delayed)}

    def outside(v):
        return not (census.SERIALIZER[0] <= v < census.SERIALIZER[1]
                    or census.LOADER[0] <= v < census.LOADER[1])

    # ---- the rebuilt atlas
    sites = list(csv.DictReader(
        open(os.path.join(repo, "docs", "ys_patch_sites_atlas.tsv"), encoding="utf-8"),
        delimiter="\t"))
    by_label = collections.defaultdict(list)
    for r in sites:
        by_label[r["label"]].append(r)
    extreme = mx.load_extreme_map(repo)
    # the authoritative union (label atlas + extreme map + g11 immediate writes);
    # g11's `mov [abs], imm` patches have no installer call, so the label atlas
    # cannot reach them and they must be carried over rather than dropped.
    union = {r["key"] for r in csv.DictReader(
        open(os.path.join(repo, "docs", "ys_patch_target_vas.tsv"), encoding="utf-8"),
        delimiter="\t")}

    # ---- the C#-side verdict already computed by the matrix
    matrix = {r["key"]: r for r in csv.DictReader(
        open(os.path.join(repo, "docs", "ys_gui_matrix.tsv"), encoding="utf-8"),
        delimiter="\t")}

    cfg, _ = mx.load_config(mx.DEFAULT_CONFIG)

    def patched(key):
        return key in union or key.rstrip("a") in union

    rows = []
    for key in sorted(cfg):
        m = matrix.get(key, {})
        srows = by_label.get(key) or by_label.get(key.rstrip("a")) or []
        ext = extreme.get(key) or extreme.get(key.rstrip("a")) or set()
        info = fields.get(key)
        off = info[0] if info else None
        cons = {}
        for tag, table in cmps.items():
            cons[tag] = [v for v in table.get(off, ()) if outside(v)] if off else []

        state = m.get("state", "?")
        owner = key.split("_", 1)[0]
        if srows or ext or patched(key):
            verdict = "NATIVE_OK" if state == "IMPLEMENTED" else "NATIVE_GAP"
        elif cons["delayed"]:
            verdict = "PLUGIN_SIDE_ONLY"
        elif owner != key and patched(owner):
            verdict = "PARAM_OF_PATCHED"
        else:
            verdict = "EQUIVALENT_BY_ABSENCE"

        rows.append({
            "key": key,
            "page": m.get("page", "?"),
            "prod_on": m.get("prod_on", "?"),
            "value": json.dumps(cfg[key], ensure_ascii=False),
            "matrix_state": state,
            "verdict": verdict,
            "apply_sites": sum(1 for r in srows if r["arm"] == "APPLY"),
            "revert_sites": sum(1 for r in srows if r["arm"] == "REVERT"),
            "patch_bytes": sum(int(r["span_len"]) for r in srows
                               if r["arm"] == "APPLY" and r["span_len"] != "-"),
            "target_vas": sorted({r["target_va"] for r in srows if r["target_va"] != "-"}
                                 | {"%#08x" % v for v in ext}),
            "cfg_field": ("%#05x" % off) if off else "?",
            "consumers": cons["delayed"],
            "owner": owner if verdict == "PARAM_OF_PATCHED" else "",
            "patch_src": "+".join(
                s for s, ok in (("label-atlas", bool(srows)),
                                ("extreme-map", bool(ext)),
                                ("g11-immediate", not srows and not ext
                                 and patched(key))) if ok),
        })

    path = os.path.join(out_dir, "ys_patch_completeness.tsv")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("key\tpage\tprod_on\tvalue\tmatrix_state\tverdict\towner\t"
                 "patch_source\tapply_sites\trevert_sites\tpatch_bytes\t"
                 "cfg_field\tn_consumers\ttarget_vas\n")
        for r in rows:
            fh.write("%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%d\t%d\t%d\t%s\t%d\t%s\n" % (
                r["key"], r["page"], r["prod_on"], r["value"], r["matrix_state"],
                r["verdict"], r["owner"], r["patch_src"] or "-",
                r["apply_sites"], r["revert_sites"],
                r["patch_bytes"], r["cfg_field"], len(r["consumers"]),
                " ".join(r["target_vas"])))
    print("wrote", path)

    print("\nverdict x matrix_state")
    tab = collections.Counter((r["verdict"], r["matrix_state"]) for r in rows)
    for k in sorted(tab):
        print("   %-22s %-12s %d" % (k[0], k[1], tab[k]))
    print("\ntotals: %s" % dict(collections.Counter(r["verdict"] for r in rows)))

    # Porting queue: a knob the reference deployment actually turns on outranks
    # a bigger patch that ships off, then patched byte count as the proxy for
    # how much native behaviour is missing.
    gaps = sorted((r for r in rows if r["verdict"] == "NATIVE_GAP"),
                  key=lambda r: (r["prod_on"] != "1", -r["patch_bytes"],
                                 -r["apply_sites"], r["key"]))
    print("\nNATIVE_GAP porting queue: %d keys, %d ON in production config"
          % (len(gaps), sum(1 for r in gaps if r["prod_on"] == "1")))
    print("   %-3s %-26s %-11s %-4s %-5s %-6s %s" %
          ("#", "key", "state", "on", "apply", "bytes", "page"))
    for i, r in enumerate(gaps, 1):
        print("   %-3d %-26s %-11s %-4s %-5d %-6d %s" % (
            i, r["key"], r["matrix_state"], r["prod_on"], r["apply_sites"],
            r["patch_bytes"], r["page"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
