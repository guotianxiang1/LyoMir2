import sys, io
sys.stdout = io.open(1, "w", encoding="utf-8", closefd=False)
sys.argv=['x']
exec(open(r'_audit\rev.py').read().split('if __name__')[0])
idxmap,gjt,gained,lost=parse_tables()
known={'0x40c89c','0x405890'}
def scan(tag,va):
    info=analyze_handler(va)
    extra=[c for c in info['othercall'] if c not in known]
    if extra or info['bytes']:
        print(f"{tag} 0x{va:06x} kind={info['kind']} extra_calls={extra} bytes={info['bytes']}")
print('--- gained handlers with side-effects (non-string calls / byte writes) ---')
for s in range(107):
    if gained[s]!=DEFAULT: scan(f'g{s}',gained[s])
print('--- lost handlers with side-effects ---')
for s in sorted(lost):
    if lost[s]!=DEFAULT: scan(f'l{s}',lost[s])
print('done')
