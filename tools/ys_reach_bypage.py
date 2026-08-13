#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Per-page reachability breakdown, reusing tools/ys_key_reachability.py logic."""
import sys, os, re, collections, io, importlib.util

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
REPO = sys.argv[1]

spec = importlib.util.spec_from_file_location('mx', os.path.join(REPO, 'tools', 'ys_gui_matrix.py'))
mx = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mx)

cfg, enc = mx.load_config(mx.DEFAULT_CONFIG)
keys = list(cfg.keys())
hits = mx.scan_sources(REPO, keys)

API = 'GameSvr/Plugins/YanshenApi.cs'
text = open(os.path.join(REPO, API.replace('/', os.sep)), encoding='utf-8-sig').read()
decls = [d for d in mx.MEMBER_DECL_RE.finditer(text) if d.group(1) not in mx.NON_MEMBER_NAMES]
PROP_RE = re.compile(r"^[ \t]*(?:(?:public|private|internal|protected|static|readonly|override|virtual|sealed|abstract|partial|new)\s+)+[\w<>\[\],?\.]+\s+(\w+)\s*(?:=>|\{)", re.M)
props = [d for d in PROP_RE.finditer(text) if d.group(1) not in mx.NON_MEMBER_NAMES]
allm = sorted(decls + props, key=lambda d: d.start())
seen = set(); members = []
for d in allm:
    if d.start() in seen:
        continue
    seen.add(d.start()); members.append(d)
declared = {d.group(1) for d in members}

calls = collections.defaultdict(set)
for n, d in enumerate(members):
    end = members[n + 1].start() if n + 1 < len(members) else len(text)
    for m in mx.IDENT_RE.finditer(text[d.end():end]):
        if m.group(1) in declared and m.group(1) != d.group(1):
            calls[d.group(1)].add(m.group(1))

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

panels = mx.panel_pages(REPO)
main_keys, explicit_params = mx.catalog_pages(REPO)

def page_for(key):
    if key in panels:
        return panels[key][0]
    if key in main_keys:
        return main_keys[key]
    if key in explicit_params:
        return explicit_params[key]
    base = re.sub(r'_[^_]*$', '', key)
    if base in panels:
        return panels[base][0]
    if base in main_keys:
        return main_keys[base]
    try:
        return mx.category_for(key)
    except Exception:
        return '(unknown)'

bypage = collections.defaultdict(collections.Counter)
rows = []
for key in keys:
    sites = hits.get(key, [])
    direct = {r for r, _, _ in sites if mx.role_of(r) == 'BEHAVIOR'}
    apim = sorted({m for r, _, m in sites if mx.role_of(r) == 'API' and m})
    tiers = set()
    for r in direct:
        tiers.add(mx.consumer_tier(r))
    for m in apim:
        for rel, tier in live.get(m, ()):
            tiers.add(tier)
    if not sites:
        st = 'MISSING'
    elif 'ENGINE' in tiers:
        st = 'IMPLEMENTED'
    elif 'SCRIPT' in tiers:
        st = 'SCRIPT_ONLY'
    else:
        st = 'LABEL_ONLY'
    pg = page_for(key)
    bypage[pg][st] += 1
    rows.append((pg, key, st, cfg.get(key)))

tot = collections.Counter()
print('=== BY PAGE ===')
for pg in sorted(bypage):
    c = bypage[pg]
    n = sum(c.values())
    tot.update(c)
    print('%-18s n=%-4d IMPLEMENTED=%-4d SCRIPT_ONLY=%-3d LABEL_ONLY=%-4d MISSING=%d'
          % (pg, n, c['IMPLEMENTED'], c['SCRIPT_ONLY'], c['LABEL_ONLY'], c['MISSING']))
print('--- TOTAL ---', dict(tot), 'n=', sum(tot.values()))

print()
print('=== LABEL_ONLY keys by page (production value) ===')
for pg in sorted(bypage):
    ks = [(k, v) for (p, k, s, v) in rows if p == pg and s == 'LABEL_ONLY']
    if not ks:
        continue
    print('[%s] %d' % (pg, len(ks)))
    for k, v in ks:
        print('   %-32s prod=%s' % (k, v))

with open(os.path.join(REPO, '_ys_reach_rows.tsv'), 'w', encoding='utf-8') as fh:
    fh.write('page\tkey\tstatus\tprod\n')
    for pg, k, s, v in rows:
        fh.write('%s\t%s\t%s\t%s\n' % (pg, k, s, v))
