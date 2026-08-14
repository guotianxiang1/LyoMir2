#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Yanshen 2.0.8 GUI feature matrix: config key -> GUI page -> C# status.

Authoritative inputs (nothing here is invented):

  * production config  D:/光头卧龙/mud2.0/Mir200/Gs1/config.json  (GBK, md5 3369e2f9...)
    -- the only source of truth for which GUI knobs exist.  The plugin's .rsrc has
    no dialog templates, so the key list cannot come from the PE resources.
  * this repository's C# sources -- every occurrence of a key as a string literal.
  * optional: D:/loym2/staging/_ysgui2/g09.json + g11.json -- the memcpy / immediate
    patch-site atlas recovered from the unpacked plugin dump (base 0x10000000).
    Used only to annotate "the plugin really patches M2Server for this feature".

Outputs (written next to the repo's docs/):
  ys_gui_matrix.tsv   one row per key, machine readable
  ys_gui_matrix.md    grouped by GUI page, human readable

Usage:  python tools/ys_gui_matrix.py [--repo DIR] [--config PATH] [--out DIR]
"""

import argparse
import collections
import json
import os
import re
import sys

# --------------------------------------------------------------------------
# paths

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_REPO = os.path.dirname(HERE)
DEFAULT_CONFIG = "D:\\\u5149\u5934\u5367\u9f99\\mud2.0\\Mir200\\Gs1\\config.json"
DEFAULT_ATLAS = "D:\\loym2\\staging\\_ysgui2"
DEFAULT_DUMP = ("D:\\loym2\\staging\\yanshen208_strparam_runtime_dump_20260719"
                "\\yanshen2_0_8_dll.memory.bin")
DUMP_BASE = 0x10000000

# --------------------------------------------------------------------------
# C# file role classification.
#
# A key that only ever shows up in a GUI/catalog/audit file has a *label* but no
# behaviour.  A key referenced from ordinary game code has behaviour.  YanshenApi
# and friends sit in between: they expose the switch to scripts, which only turns
# into behaviour if some game-code path calls that member.

GUI_FILES = {
    "GameSvr/Plugins/YanshenReplicaConfigForm.cs",
    "GameSvr/Plugins/YanshenConfigForm.cs",
    "GameSvr/Plugins/YanshenFixedReplicaPanels.cs",
    "GameSvr/Plugins/YanshenLegacy23ReplicaPanels.cs",
    "GameSvr/Plugins/YanshenConfig12ReplicaPanels.cs",
    "GameSvr/Plugins/YanshenReplicaSpecialPanels.cs",
    "GameSvr/Plugins/PluginConfigPanel.cs",
    "GameSvr/Plugins/PluginManagerForm.cs",
    "GameSvr/Plugins/PluginManager.cs",
    "GameSvr/Plugins/PluginHttpServer.cs",   # admin web UI: Categorize() only labels
    "GameSvr/MainForm.cs",
    "GameGate-CS/Forms/GgAcExactFeatureSettingsPage.cs",
}
# YanshenApi.cs is split: the _keyMap block is pure alias plumbing (it names all
# 379 keys and proves nothing), everything else is a real script-API gate.
API_FILES = {"GameSvr/Plugins/YanshenApi.cs"}
AUDIT_PREFIXES = ("AuditTools/", "GameSvr.Tests/", "ProtocolRegressionCheck/", "tools/", "docs/")


def role_of(rel):
    if rel.endswith("#_keyMap"):
        return "PLUMBING"
    if rel in GUI_FILES:
        return "GUI"
    if rel in API_FILES:
        return "API"
    if rel.startswith(AUDIT_PREFIXES):
        return "AUDIT"
    return "BEHAVIOR"


KEYMAP_OWNER = "GameSvr/Plugins/YanshenApi.cs"

# Consumers that are the yanshen script surface rather than engine code.  A key
# gated only here means "the plugin's script function honours the switch", which
# is a real implementation for script-driven features but not an engine change.
SCRIPT_CONSUMERS = {
    "GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs",
    "GameSvr/Plugins/YanshenCommands.cs",
}


def consumer_tier(rel):
    if rel in SCRIPT_CONSUMERS:
        return "SCRIPT"
    role = role_of(rel)
    return "ENGINE" if role == "BEHAVIOR" else role


def keymap_span(repo):
    """Byte range of YanshenApi._keyMap, whose entries carry no behaviour."""
    path = os.path.join(repo, KEYMAP_OWNER.replace("/", os.sep))
    if not os.path.exists(path):
        return None
    text = open(path, encoding="utf-8-sig").read()
    i = text.find("_keyMap = new(")
    if i < 0:
        return None
    j = text.find("\n        };", i)
    return (i, j if j > 0 else len(text))


# --------------------------------------------------------------------------
# 1. production config

def load_config(path):
    raw = open(path, "rb").read()
    for enc in ("gbk", "utf-8-sig", "utf-8"):
        try:
            return json.loads(raw.decode(enc)), enc
        except (UnicodeDecodeError, json.JSONDecodeError):
            continue
    raise SystemExit("cannot decode " + path)


def is_on(value):
    """Observable contract of a yanshen switch: the json value is non-zero.

    The loader re-encodes 0/1 into rand()%1000+1000 (on) / a small modulus (off)
    and every consumer tests `> 500`; 0x1F4 is the midpoint of that encoding,
    not a threshold.  So at the json layer the only thing that matters is != 0.
    """
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return value != 0
    if isinstance(value, str):
        s = value.strip()
        if not s:
            return False
        try:
            return float(s) != 0
        except ValueError:
            return True
    return value is not None


# --------------------------------------------------------------------------
# 2. GUI page assignment
#
# Two independent sources, both read straight out of the C# so the table cannot
# drift from the code:
#   (a) the pixel-accurate replica panels -- AddToggle("k", ...) / AddValue("k", ...)
#       inside a panel class whose page name is known.
#   (b) YanshenPageCatalog.PageMainKeys / ExplicitParameters -- the logical catalog.
# (a) wins when both are present.

PANEL_CLASS_PAGE = {
    "LegacyOneReplicaPanel": "\u76d8\u53e41",
    "Legacy2ReplicaPanel": "\u76d8\u53e42",
    "Legacy3ReplicaPanel": "\u76d8\u53e43",
    "EquipmentReplicaPanel": "\u76d8\u53e44",
    "Config1ReplicaPanel": "\u914d\u7f6e1",
    "Config2ReplicaPanel": "\u914d\u7f6e2",
    "SeasonOneReplicaPanel": "\u773c\u795e2(\u7b2c1\u9875)",
    "SeasonTwoReplicaPanel": "\u773c\u795e2(\u7b2c2\u9875)",
    "BackpackReplicaPanel": "\u80cc\u5305",
}

PANEL_FILES = [
    "GameSvr/Plugins/YanshenFixedReplicaPanels.cs",
    "GameSvr/Plugins/YanshenLegacy23ReplicaPanels.cs",
    "GameSvr/Plugins/YanshenConfig12ReplicaPanels.cs",
    "GameSvr/Plugins/YanshenReplicaSpecialPanels.cs",
]

CLASS_RE = re.compile(r"^\s*internal (?:sealed )?(?:abstract )?class (\w+)", re.M)
WIDGET_RE = re.compile(
    r"\b(AddToggle|AddValue|AddParameter|PaintToggle|PaintParameter)\s*\("
    r"(?:[^()\"]*?,\s*)?\"([^\"]+)\"")


EQUIPMENT = ("\u6b66\u5668", "\u8863\u670d", "\u5934\u76d4", "\u9879\u94fe", "\u624b\u956f", "\u6212\u6307")
EQUIP_KIND = ("\u6700\u9ad8\u70b9\u6570", "\u70b9\u6570\u51e0\u7387", "\u5c5e\u6027\u51e0\u7387", "\u6700\u968f\u673a\u6027")


def is_equipment_param(key):
    """EquipmentReplicaPanel (page 盘古4) draws every non-toggle row whose key
    starts with one of the six equipment names -- a loop, not literal calls,
    so it has to be reproduced rather than scraped."""
    return (any(key.startswith(e) for e in EQUIPMENT)
            and any(k in key for k in EQUIP_KIND))


def panel_pages(repo):
    """key -> (page, widget kind) taken from the replica panel sources."""
    out = {}
    for rel in PANEL_FILES:
        path = os.path.join(repo, rel.replace("/", os.sep))
        if not os.path.exists(path):
            continue
        text = open(path, encoding="utf-8-sig").read()
        bounds = [(m.start(), m.group(1)) for m in CLASS_RE.finditer(text)]
        bounds.append((len(text), None))
        for i in range(len(bounds) - 1):
            cls = bounds[i][1]
            page = PANEL_CLASS_PAGE.get(cls)
            if not page:
                continue
            body = text[bounds[i][0]:bounds[i + 1][0]]
            for m in WIDGET_RE.finditer(body):
                kind = "toggle" if "Toggle" in m.group(1) else "value"
                out.setdefault(m.group(2), (page, kind))
    return out


BLOCK_RE = re.compile(
    r"\[\"([^\"]+)\"\]\s*=\s*new\[\]\s*\{(.*?)\}", re.S)
LIT_RE = re.compile(r"\"([^\"]+)\"")


def catalog_pages(repo):
    """PageMainKeys and ExplicitParameters, parsed from YanshenReplicaConfigForm.cs."""
    path = os.path.join(repo, "GameSvr", "Plugins", "YanshenReplicaConfigForm.cs")
    text = open(path, encoding="utf-8-sig").read()

    def section(name):
        i = text.index(name)
        j = text.index("};", i)
        return text[i:j]

    main = {}
    for m in BLOCK_RE.finditer(section("PageMainKeys")):
        page = m.group(1)
        for k in LIT_RE.findall(m.group(2)):
            main.setdefault(k, page)

    params = {}
    for m in BLOCK_RE.finditer(section("ExplicitParameters")):
        owner = m.group(1)
        for k in LIT_RE.findall(m.group(2)):
            params.setdefault(k, owner)
    return main, params


def category_for(key):
    """Mirror of YanshenPageCatalog.CategoryFor -- the fallback bucket used by
    the extension pages for keys the fixed pages do not place."""
    groups = [
        ("\u7269\u54c1\u76f8\u5173", "\u7269\u54c1 \u88c5\u5907 \u80cc\u5305 \u62fe\u53d6 \u6361\u7269 \u4ed3\u5e93 \u7ed1\u5b9a \u6295\u4fdd \u6781\u54c1 \u91d1\u5e01 \u6b66\u5668 \u8863\u670d \u5934\u76d4 \u9879\u94fe \u624b\u956f \u6212\u6307"),
        ("\u89d2\u8272\u76f8\u5173", "\u4eba\u7269 \u89d2\u8272 \u82f1\u96c4 \u5b9d\u5b9d \u5ba0\u7269 \u884c\u4f1a \u6218\u961f \u6446\u644a \u540d\u5b57 \u79f0\u53f7 \u7b49\u7ea7 \u4e0b\u7ebf \u804c\u4e1a \u9635\u8425 \u6c99\u57ce"),
        ("\u6280\u80fd\u76f8\u5173", "\u6280\u80fd \u5251 \u5200 \u706b \u96f7 \u6bd2 \u672f \u653b\u51fb \u4f24\u5bb3 \u5207\u5272 \u9ebb\u75f9 \u5438\u8840 \u53cd\u4f24 \u9b54\u6cd5 \u53ec\u5524 \u9ab7\u9ac5 \u795e\u517d \u76fe \u683c\u6321 \u5408\u51fb \u51b0\u5486\u54ee \u6fc0\u5149"),
        ("\u7206\u7387\u76f8\u5173", "\u7206\u7387 \u7206\u7269 \u5168\u670d\u51fb\u6740\u63d0\u793a"),
    ]
    for name, frags in groups:
        for frag in frags.split():
            if frag in key:
                return name
    return "\u811a\u672c\u76f8\u5173"


# --------------------------------------------------------------------------
# 3. C# literal index

STR_RE = re.compile(r"\"((?:[^\"\\\n]|\\.)*)\"")
CJK_RE = re.compile(r"[\u4e00-\u9fff]")
MEMBER_RE = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)?(?:public|private|internal|protected|static|sealed|override|virtual|async|partial|\s)*"
    r"[\w<>\[\],\.\?]+\s+(\w+)\s*(?:\(|=>)", re.M)


def scan_sources(repo, keys):
    """key -> list of (relpath, line, enclosing member).  Only exact literals."""
    hits = collections.defaultdict(list)
    keyset = set(keys)
    km = keymap_span(repo)
    for root, dirs, files in os.walk(repo):
        dirs[:] = [d for d in dirs if d not in (".git", "bin", "obj", "node_modules", ".vs")]
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(root, name)
            rel = os.path.relpath(path, repo).replace(os.sep, "/")
            try:
                text = open(path, encoding="utf-8-sig").read()
            except (UnicodeDecodeError, OSError):
                continue
            if not CJK_RE.search(text):
                continue
            members = [(m.start(), m.group(1)) for m in MEMBER_RE.finditer(text)]
            starts = [m[0] for m in members]
            import bisect
            for m in STR_RE.finditer(text):
                lit = m.group(1)
                if lit not in keyset:
                    continue
                if rel == KEYMAP_OWNER and km and km[0] <= m.start() < km[1]:
                    hits[lit].append((rel + "#_keyMap", 0, ""))
                    continue
                line = text.count("\n", 0, m.start()) + 1
                i = bisect.bisect_right(starts, m.start()) - 1
                member = members[i][1] if i >= 0 else ""
                hits[lit].append((rel, line, member))
    return hits


IDENT_RE = re.compile(r"\b([A-Za-z_]\w{2,})\b")

PROP_RE = re.compile(
    r"^[ \t]*(?:(?:public|private|internal|protected|static|readonly|override"
    r"|virtual|sealed|abstract|partial|new)\s+)+[\w<>\[\],?\.]+\s+(\w+)\s*(?:=>|\{)",
    re.M)


def all_api_members(repo):
    """Every method/property declared in YanshenApi, including relays.

    Relays such as TryGetModifyShenShou hold no key literal but are named by
    engine code; seeding only from key-bearing members mis-reports their
    accessors as LABEL_ONLY (see tools/ys_key_reachability.py).
    """
    declared = set()
    for owner in API_FILES:
        path = os.path.join(repo, owner.replace("/", os.sep))
        if not os.path.exists(path):
            continue
        text = open(path, encoding="utf-8-sig").read()
        for d in MEMBER_DECL_RE.finditer(text):
            if d.group(1) not in NON_MEMBER_NAMES:
                declared.add(d.group(1))
        for d in PROP_RE.finditer(text):
            if d.group(1) not in NON_MEMBER_NAMES:
                declared.add(d.group(1))
    return declared


def accessor_consumers(repo, hits):
    """member name -> set of files that name it, outside its declaring file.

    YanshenApi.cs declares one `IsXxx` / `XxxEnabled` accessor per config key.
    Most are never called; an accessor nobody calls is not behaviour, it is a
    differently-shaped label.  This resolves which ones are live.

    Seeding uses every declared YanshenApi member referenced from engine or
    script code, not only members that literally contain a key string.
    """
    members = all_api_members(repo)
    if not members:
        return {}
    use = collections.defaultdict(set)
    for root, dirs, files in os.walk(repo):
        dirs[:] = [d for d in dirs if d not in (".git", "bin", "obj", "node_modules", ".vs")]
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(root, name)
            rel = os.path.relpath(path, repo).replace(os.sep, "/")
            if rel in API_FILES:
                continue
            if consumer_tier(rel) not in ("ENGINE", "SCRIPT"):
                continue
            try:
                text = open(path, encoding="utf-8-sig").read()
            except (UnicodeDecodeError, OSError):
                continue
            for m in IDENT_RE.finditer(text):
                w = m.group(1)
                if w in members:
                    use[w].add(rel)
    propagate_within_api(repo, members, use)
    return use


# Members in YanshenApi are written with and without an access modifier
# (`int GetParamInt(...)` sits next to `public bool IsOn(...)`), so the modifier
# run has to be optional -- otherwise the unmarked ones vanish from the graph
# and any chain that runs through them breaks.
MEMBER_DECL_RE = re.compile(
    r"^[ \t]*(?:(?:public|private|internal|protected|static|readonly|override"
    r"|virtual|sealed|abstract|async|unsafe|extern|partial|new)\s+)*"
    r"[\w<>\[\],?\.]+\s+(\w+)\s*\(", re.M)
NON_MEMBER_NAMES = frozenset((
    "if", "for", "foreach", "while", "switch", "catch", "using", "return",
    "lock", "fixed", "do", "else", "throw", "yield", "get", "set"))


def propagate_within_api(repo, members, use):
    """Carry liveness across one accessor calling another inside YanshenApi.

    An accessor reached only through a private helper in the same file still
    ends up driving whatever calls that helper, so treating it as dead reports
    a live switch as a label.  The 随机极品 master toggle is the case that made
    this show up: 96 live accessors read it through ExtremeParamInt.
    """
    for owner in API_FILES:
        path = os.path.join(repo, owner.replace("/", os.sep))
        if not os.path.exists(path):
            continue
        text = open(path, encoding="utf-8-sig").read()
        decls = [d for d in MEMBER_DECL_RE.finditer(text)
                 if d.group(1) not in NON_MEMBER_NAMES]
        # Relays need to be in the graph too: the helper that reads a toggle on
        # an accessor's behalf holds no key literal of its own, so restricting
        # the graph to key-bearing members would break the chain at the relay.
        declared = {d.group(1) for d in decls}
        calls = collections.defaultdict(set)
        for n, d in enumerate(decls):
            end = decls[n + 1].start() if n + 1 < len(decls) else len(text)
            body = text[d.end():end]
            for m in IDENT_RE.finditer(body):
                if m.group(1) in declared and m.group(1) != d.group(1):
                    calls[d.group(1)].add(m.group(1))
        for _ in range(len(declared) + 1):
            grew = False
            for caller, callees in calls.items():
                if not use.get(caller):
                    continue
                for callee in callees:
                    before = len(use[callee])
                    use[callee] |= use[caller]
                    grew = grew or len(use[callee]) != before
            if not grew:
                break


# --------------------------------------------------------------------------
# 4. INVENTED detection: keys the C# looks up that production config does not have

LOOKUP_RE = re.compile(
    r"\b(?:Enabled|IsOn|Param|ParamF|ParamS|GetParam|GetParamInt|RawParam"
    r"|GetNativeConfigValue|SetNativeConfigValue|AddToggle|AddValue|AddParameter"
    r"|PaintToggle)\s*\(\s*\"([^\"]+)\"")


def scan_lookups(repo):
    out = collections.defaultdict(list)
    for root, dirs, files in os.walk(repo):
        dirs[:] = [d for d in dirs if d not in (".git", "bin", "obj", "node_modules", ".vs")]
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(root, name)
            rel = os.path.relpath(path, repo).replace(os.sep, "/")
            try:
                text = open(path, encoding="utf-8-sig").read()
            except (UnicodeDecodeError, OSError):
                continue
            for m in LOOKUP_RE.finditer(text):
                lit = m.group(1)
                if CJK_RE.search(lit):
                    out[lit].append((rel, text.count("\n", 0, m.start()) + 1))
    return out


# --------------------------------------------------------------------------
# 5. patch atlas (optional annotation)

STATUS_SUFFIX = re.compile(
    r"\((\u5df2\u542f\u52a8|\u672a\u542f\u52a8|\u5df2\u542f\u7528|\u672a\u542f\u7528|\u5df2\u5173\u95ed|\u672a\u5173\u95ed"
    r"|\u5df2\u8bbe\u7f6e|\u672a\u8bbe\u7f6e|\u5df2\u91cd\u8bbe|\u5f85\u91cd\u8bbe|\u6539\u7528\u65b0\u7248)\)$")


def load_keystrings(atlas_dir):
    """key -> (rdata VA of the GBK key string in the plugin dump, xref count).

    Parsed from g01.txt, produced by _ysgui2/g01_keymap.py over
    yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin
    at base 0x10000000.  A key with an .rdata string and >=1 xref is one the
    plugin genuinely reads -- the anti-fabrication check demanded by the rules.
    """
    path = os.path.join(atlas_dir, "g01.txt")
    if not os.path.exists(path):
        return {}
    out = {}
    key = None
    for line in open(path, encoding="utf-8"):
        line = line.rstrip("\n")
        if not line or line.startswith("keys="):
            continue
        if not line.startswith("    "):
            key = line
            out[key] = (None, 0)
            continue
        if key is None:
            continue
        body = line.strip()
        if body.startswith("str "):
            toks = [t for t in body[4:].split() if t.startswith("0x")]
            out[key] = (int(toks[0], 16) if toks else None, out[key][1])
        elif body.startswith("ref "):
            m = re.search(r"\((\d+)\)$", body)
            out[key] = (out[key][0], int(m.group(1)) if m else 0)
    return out


def dump_strings(dump_path, candidates):
    """Which of `candidates` occur as NUL-delimited GBK strings in the plugin dump.

    Used to tell a genuine plugin-internal key (present in the binary but absent
    from this deployment's config.json) from a fabricated one.
    """
    if not candidates or not os.path.exists(dump_path):
        return {}
    buf = open(dump_path, "rb").read()
    out = {}
    for key in candidates:
        try:
            needle = key.encode("gbk")
        except UnicodeEncodeError:
            continue
        i = buf.find(b"\x00" + needle + b"\x00")
        if i >= 0:
            out[key] = DUMP_BASE + i + 1
    return out


def load_extreme_map(repo):
    """key -> host VA, for the 96 random-superior knobs.

    These never show up in the memcpy/immediate atlas: the apply arm stores into
    M2Server .text with a plain `mov [absolute], eax`, so a scan that only looks
    for the patch helpers misses all 96.  See docs/ys_gui_extreme_20260813.md.
    """
    path = os.path.join(repo, "docs", "ys_extreme_map.tsv")
    if not os.path.exists(path):
        return {}
    out = {}
    with open(path, encoding="utf-8") as fh:
        next(fh, None)
        for line in fh:
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 4 and parts[3]:
                out[parts[0]] = {int(parts[3], 16)}
    return out


def load_atlas(atlas_dir):
    """feature label -> set of M2Server target VAs the plugin writes."""
    out = collections.defaultdict(set)
    for fname, field in (("g09.json", "va"), ("g11.json", "target")):
        path = os.path.join(atlas_dir, fname)
        if not os.path.exists(path):
            continue
        for row in json.load(open(path, encoding="utf-8")):
            label = row.get("label") or ""
            label = STATUS_SUFFIX.sub("", label).strip()
            va = row.get(field)
            if label and va:
                out[label].add(va)
    return out


# --------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=DEFAULT_REPO)
    ap.add_argument("--config", default=DEFAULT_CONFIG)
    ap.add_argument("--atlas", default=DEFAULT_ATLAS)
    ap.add_argument("--dump", default=DEFAULT_DUMP)
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    repo = os.path.abspath(args.repo)
    out_dir = args.out or os.path.join(repo, "docs")
    os.makedirs(out_dir, exist_ok=True)

    cfg, enc = load_config(args.config)
    keys = list(cfg.keys())

    panels = panel_pages(repo)
    main_keys, explicit_params = catalog_pages(repo)
    hits = scan_sources(repo, keys)
    consumers = accessor_consumers(repo, hits)
    lookups = scan_lookups(repo)
    atlas = load_atlas(args.atlas)
    atlas.update(load_extreme_map(repo))
    keystrings = load_keystrings(args.atlas)

    rows = []
    for key in sorted(keys):
        value = cfg[key]
        on = is_on(value)

        if is_equipment_param(key):
            page, widget, page_src = "\u76d8\u53e44", "value", "equip-loop"
        elif key in panels:
            page, widget = panels[key]
            page_src = "panel"
        elif key in main_keys:
            page, widget, page_src = main_keys[key], "toggle", "catalog"
        elif key in explicit_params:
            owner = explicit_params[key]
            page = panels.get(owner, (main_keys.get(owner, ""), ""))[0] or main_keys.get(owner, "")
            page = page or "\u6269\u5c55/" + category_for(key)
            widget, page_src = "value", "catalog-param"
        else:
            base = key.split("_", 1)[0]
            owner_page = panels.get(base, (None, None))[0] or main_keys.get(base)
            if owner_page and "_" in key:
                page, widget, page_src = owner_page, "value", "suffix"
            else:
                page = "\u6269\u5c55/" + category_for(key)
                widget = "value" if key.endswith("\u503c") or isinstance(value, str) else "toggle"
                page_src = "fallback"

        sites = hits.get(key, [])
        direct = {r for r, _, _ in sites if role_of(r) == "BEHAVIOR"}
        members = sorted({m for r, _, m in sites if role_of(r) == "API" and m})
        via = set()
        for member in members:
            for rel in consumers.get(member, ()):
                if role_of(rel) in ("BEHAVIOR",):
                    via.add(rel)
        live = direct | via
        tiers = {consumer_tier(r) for r in live}
        if not sites:
            state = "MISSING"
        elif "ENGINE" in tiers:
            state = "IMPLEMENTED"
        elif "SCRIPT" in tiers:
            state = "SCRIPT_ONLY"
        else:
            state = "LABEL_ONLY"

        patch_vas = atlas.get(key) or atlas.get(key.rstrip("a")) or set()
        str_va, xrefs = keystrings.get(key, (None, 0))
        rows.append({
            "key": key,
            "value": value,
            "on": on,
            "page": page,
            "page_src": page_src,
            "widget": widget,
            "state": state,
            "sites": sites,
            "behavior_files": sorted(live),
            "dead_accessors": sorted(m for m in members if not consumers.get(m)),
            "api_members": members,
            "patch_vas": sorted(patch_vas),
            "str_va": str_va,
            "xrefs": xrefs,
        })

    # Rule: a key is only legitimate if production config has it OR the plugin
    # dump has it as a GBK string.  Anything else is fabricated.
    extra = sorted(k for k in lookups if k not in cfg)
    in_dump = dump_strings(args.dump, extra)
    invented = [k for k in extra if k not in in_dump]
    dump_only = [k for k in extra if k in in_dump]

    # ---- TSV
    tsv = os.path.join(out_dir, "ys_gui_matrix.tsv")
    with open(tsv, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("key\tvalue\tprod_on\tpage\tpage_src\twidget\tstate\t"
                 "n_sites\tbehavior_files\tapi_members\tpatch_target_vas\t"
                 "dump_str_va\tdump_xrefs\n")
        for r in rows:
            fh.write("\t".join([
                r["key"],
                json.dumps(r["value"], ensure_ascii=False),
                "1" if r["on"] else "0",
                r["page"], r["page_src"], r["widget"], r["state"],
                str(len(r["sites"])),
                ";".join(r["behavior_files"]),
                ";".join(r["api_members"]),
                " ".join("%#08x" % v for v in r["patch_vas"]),
                ("%#010x" % r["str_va"]) if r["str_va"] else "",
                str(r["xrefs"]),
            ]) + "\n")

    # ---- Markdown
    counts = collections.Counter(r["state"] for r in rows)
    gap = [r for r in rows if r["on"] and r["state"] in ("MISSING", "LABEL_ONLY")]
    api_gap = [r for r in rows if r["on"] and r["state"] == "SCRIPT_ONLY"]
    by_page = collections.OrderedDict()
    page_order = list(PANEL_CLASS_PAGE.values())
    for r in rows:
        by_page.setdefault(r["page"], []).append(r)
    ordered = [p for p in page_order if p in by_page] + \
              sorted(p for p in by_page if p not in page_order)

    md = os.path.join(out_dir, "ys_gui_matrix.md")
    with open(md, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("# \u773c\u795e 2.0.8 GUI \u529f\u80fd\u603b\u8868\n\n")
        fh.write("\u751f\u6210\u811a\u672c `tools/ys_gui_matrix.py`\uff1b\u914d\u7f6e `%s`\uff08%s\uff0c%d \u952e\uff09\u3002\n\n"
                 % (args.config, enc, len(keys)))
        fh.write("| \u72b6\u6001 | \u8ba1\u6570 |\n|---|---|\n")
        for state in ("IMPLEMENTED", "SCRIPT_ONLY", "LABEL_ONLY", "MISSING"):
            fh.write("| %s | %d |\n" % (state, counts.get(state, 0)))
        fh.write("| INVENTED | %d |\n" % len(invented))
        fh.write("\n\u751f\u4ea7\u5df2\u5f00\u542f\uff08\u503c != 0\uff09%d \u952e\uff1b\u5176\u4e2d\u65e0\u884c\u4e3a\u7684 %d \u952e\u3002\n\n"
                 % (sum(1 for r in rows if r["on"]), len(gap)))

        fh.write("## \u751f\u4ea7\u5df2\u5f00\u542f\u4f46 C# \u65e0\u884c\u4e3a\n\n")
        fh.write("| \u952e | \u503c | \u9875\u9762 | \u72b6\u6001 | \u4e32 VA | xref | \u63d2\u4ef6\u8865\u4e01\u76ee\u6807 VA |\n"
                 "|---|---|---|---|---|---|---|\n")
        for r in sorted(gap, key=lambda x: (x["page"], x["key"])):
            fh.write("| `%s` | %s | %s | %s | %s | %d | %s |\n" % (
                r["key"], json.dumps(r["value"], ensure_ascii=False), r["page"], r["state"],
                ("`%#010x`" % r["str_va"]) if r["str_va"] else "-", r["xrefs"],
                " ".join("`%#08x`" % v for v in r["patch_vas"]) or "-"))

        fh.write("\n## \u6309\u9875\u9762\n")
        for page in ordered:
            group = by_page[page]
            fh.write("\n### %s\uff08%d\uff09\n\n" % (page, len(group)))
            fh.write("| \u952e | \u503c | \u5f00 | \u63a7\u4ef6 | \u72b6\u6001 | C# \u884c\u4e3a\u843d\u70b9 / API \u6210\u5458 |\n|---|---|---|---|---|---|\n")
            for r in sorted(group, key=lambda x: x["key"]):
                where = "; ".join(r["behavior_files"][:3]) or "; ".join(r["api_members"][:4]) or "-"
                fh.write("| `%s` | %s | %s | %s | %s | %s |\n" % (
                    r["key"], json.dumps(r["value"], ensure_ascii=False),
                    "Y" if r["on"] else "", r["widget"], r["state"], where))

        if dump_only:
            fh.write("\n## \u751f\u4ea7 config \u65e0\u4f46\u8f6c\u50a8\u91cc\u6709\uff08\u5408\u6cd5\uff0c\u975e\u81c6\u9020\uff09\n\n")
            for k in dump_only:
                fh.write("- `%s` @ `%#010x` \u2014 %s\n" % (
                    k, in_dump[k], ", ".join("%s:%d" % s for s in lookups[k][:4])))
        if invented:
            fh.write("\n## INVENTED\uff08\u751f\u4ea7 config \u548c\u8f6c\u50a8\u5b57\u7b26\u4e32\u91cc\u90fd\u6ca1\u6709\uff09\n\n")
            for k in invented:
                fh.write("- `%s` \u2014 %s\n" % (
                    k, ", ".join("%s:%d" % s for s in lookups[k][:4])))

    print("keys              ", len(keys))
    for state in ("IMPLEMENTED", "SCRIPT_ONLY", "LABEL_ONLY", "MISSING"):
        print("%-18s %d" % (state, counts.get(state, 0)))
    print("INVENTED           %d" % len(invented))
    print("dump-only (ok)     %d" % len(dump_only))
    print("prod-on            %d" % sum(1 for r in rows if r["on"]))
    print("prod-on w/o behav  %d" % len(gap))
    print("prod-on script-only %d" % len(api_gap))
    print("wrote", tsv)
    print("wrote", md)
    return 0


if __name__ == "__main__":
    sys.exit(main())
