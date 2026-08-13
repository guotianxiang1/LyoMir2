#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Per-key reachability for Yanshen config keys.

Fixes the blind spot in tools/ys_gui_matrix.py: accessor_consumers() only seeds
liveness for YanshenApi members that literally contain a key string, so a relay
(e.g. TryGetModifyShenShou) that is called from engine code but holds no key
literal never enters the graph, and every accessor reached only through it is
mis-reported as LABEL_ONLY.

Here the graph is seeded from EVERY YanshenApi member named by a behaviour file.
"""
import sys, os, re, json, collections, bisect, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__))))
REPO = sys.argv[1]
KEYS = sys.argv[2:] or None

sys.path.insert(0, os.path.join(REPO, 'tools'))
import importlib.util
spec = importlib.util.spec_from_file_location('mx', os.path.join(REPO, 'tools', 'ys_gui_matrix.py'))
mx = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mx)

cfg, enc = mx.load_config(mx.DEFAULT_CONFIG)
keys = list(cfg.keys())
hits = mx.scan_sources(REPO, keys)

API = 'GameSvr/Plugins/YanshenApi.cs'
text = open(os.path.join(REPO, API.replace('/', os.sep)), encoding='utf-8-sig').read()
decls = [d for d in mx.MEMBER_DECL_RE.finditer(text) if d.group(1) not in mx.NON_MEMBER_NAMES]
# properties / expression-bodied members without parens
PROP_RE = re.compile(r"^[ \t]*(?:(?:public|private|internal|protected|static|readonly|override|virtual|sealed|abstract|partial|new)\s+)+[\w<>\[\],?\.]+\s+(\w+)\s*(?:=>|\{)", re.M)
props = [d for d in PROP_RE.finditer(text) if d.group(1) not in mx.NON_MEMBER_NAMES]
allm = sorted(decls + props, key=lambda d: d.start())
# dedupe by start
seen = set(); members = []
for d in allm:
    if d.start() in seen:
        continue
    seen.add(d.start()); members.append(d)
declared = {d.group(1) for d in members}
starts = [d.start() for d in members]

calls = collections.defaultdict(set)
for n, d in enumerate(members):
    end = members[n + 1].start() if n + 1 < len(members) else len(text)
    for m in mx.IDENT_RE.finditer(text[d.end():end]):
        if m.group(1) in declared and m.group(1) != d.group(1):
            calls[d.group(1)].add(m.group(1))

# seed: any YanshenApi member named by a BEHAVIOR file
seed = collections.defaultdict(set)
for root, dirs, files in os.walk(REPO):
    dirs[:] = [x for x in dirs if x not in ('.git', 'bin', 'obj', 'node_modules', '.vs')]
    for name in files:
        if not name.endswith('.cs'):
            continue
        rel = os.path.relpath(os.path.join(root, name), REPO).replace(os.sep, '/')
        if rel == API:
            continue
        tier = mx.consumer_tier(rel)
        if tier not in ('ENGINE', 'SCRIPT'):
            continue
        try:
            t = open(os.path.join(root, name), encoding='utf-8-sig').read()
        except Exception:
            continue
        for m in mx.IDENT_RE.finditer(t):
            if m.group(1) in declared:
                seed[m.group(1)].add((rel, tier))

live = {k: set(v) for k, v in seed.items()}
for _ in range(len(declared) + 2):
    grew = False
    for caller, callees in calls.items():
        if caller not in live or not live[caller]:
            continue
        for callee in callees:
            b = len(live.get(callee, ()))
            live.setdefault(callee, set())
            live[callee] |= live[caller]
            grew = grew or len(live[callee]) != b
    if not grew:
        break


def member_at(pos):
    i = bisect.bisect_right(starts, pos) - 1
    return members[i].group(1) if i >= 0 else ''


km = mx.keymap_span(REPO)
out = []
for key in keys:
    sites = hits.get(key, [])
    direct = {r for r, _, _ in sites if mx.role_of(r) == 'BEHAVIOR'}
    apim = sorted({m for r, _, m in sites if mx.role_of(r) == 'API' and m})
    reach = {}
    for m in apim:
        for rel, tier in live.get(m, ()):
            reach.setdefault(m, set()).add((rel, tier))
    tiers = set()
    for r in direct:
        tiers.add(mx.consumer_tier(r))
    for m, s in reach.items():
        for rel, tier in s:
            tiers.add(tier)
    if not sites:
        st = 'MISSING'
    elif 'ENGINE' in tiers:
        st = 'IMPLEMENTED'
    elif 'SCRIPT' in tiers:
        st = 'SCRIPT_ONLY'
    else:
        st = 'LABEL_ONLY'
    out.append((key, st, apim, reach, direct))

if KEYS:
    for key, st, apim, reach, direct in out:
        if key not in KEYS:
            continue
        print('%-24s %s' % (key, st))
        print('   api members:', ', '.join(apim) or '-')
        for m in apim:
            r = reach.get(m)
            print('     %-28s %s' % (m, ', '.join(sorted('%s[%s]' % (a, b) for a, b in r)[:4]) if r else 'DEAD'))
        if direct:
            print('   direct behaviour:', ', '.join(sorted(direct)))
        print()
else:
    c = collections.Counter(s for _, s, _, _, _ in out)
    print(dict(c))
