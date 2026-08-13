"""Parse production YBShopScript.pas with the SAME contract as MallManager.LoadPasMallItems.

Does NOT import GameSvr. Mirrors the C# regex + $ split + -1 validation + first-seen categories.
Writes a field-level comparison against the case-branch source of truth.
"""
import re
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding='utf-8')

PROD = Path(r"D:\光头卧龙\mud2.0\Mir200\Envir\YBShop\YBShopScript.pas")
OUT = Path(r"D:\loym2\.claude\wt2\m-mailmall\docs\mailmall_re\q21_prod_parse.txt")

script = PROD.read_bytes().decode('gbk')

FIELD_ORDER = [
    "vClassName", "vItemList", "vGoodsIdx", "vSrcPrice", "vCurPrice",
    "vLimitType", "vLimitCount", "vEffectImg", "vEffectCount", "vGoodsExplain",
]
INT_FIELDS = {
    "vGoodsIdx", "vSrcPrice", "vCurPrice", "vLimitType", "vLimitCount",
    "vEffectImg", "vEffectCount",
}

def str_to_int_def(value, default=-1):
    try:
        return int((value or "").strip())
    except ValueError:
        return default

# C# ResolveConfiguredGoodsNames: first C_NeedLoadGoodsNames\w* = '...'
name_match = re.search(
    r"\bC_NeedLoadGoodsNames\w*\s*=\s*'(?P<names>[^']*)'",
    script, re.I | re.S)
configured = []
seen = set()
if name_match:
    for raw in name_match.group('names').split('|'):
        name = raw.strip()
        if name and name.lower() not in seen:
            seen.add(name.lower())
            configured.append(name)

# all C_NeedLoadGoodsNames* constants (to prove _001 vs _002)
all_consts = re.findall(
    r"\b(C_NeedLoadGoodsNames\w*)\s*=\s*'([^']*)'", script, re.I)

# C# CollectGoodsConfigs case-begin parser
preamble_m = re.search(
    r"\bfunction\s+GetYBShopConfig\b.*?\bbegin\b(?P<pre>.*?)\bcase\b",
    script, re.I | re.S)
preamble = preamble_m.group('pre') if preamble_m else ''

def read_assignments(body):
    fields = {}
    for field in FIELD_ORDER:
        m = re.search(
            r"\b" + field + r"\s*:=\s*(?:'(?P<s>[^']*)'|(?P<n>-?\d+))\s*;",
            body, re.I)
        if not m:
            continue
        fields[field] = m.group('s') if m.group('s') is not None else m.group('n')
    return fields

defaults = read_assignments(preamble)
configs = {}
for m in re.finditer(
        r"'(?P<name>[^']+)'\s*:\s*begin(?P<body>.*?)\bend\s*;",
        script, re.I | re.S):
    body = m.group('body')
    if re.search(r"\bbegin\b", body, re.I):
        continue
    fields = dict(defaults)
    fields.update(read_assignments(body))
    name = m.group('name').strip()
    if name and name not in configs:
        parts = [fields.get(k, '') for k in FIELD_ORDER]
        configs[name] = '$'.join(parts)

# also the Result := '...' shape (should be 0 on production)
result_shape = list(re.finditer(
    r"'(?P<name>[^']+)'\s*:\s*Result\s*:=\s*'(?P<config>[^']*)'\s*;",
    script, re.I))

parsed = []
dropped = []
for order, goods in enumerate(configured):
    cfg = configs.get(goods)
    if cfg is None:
        dropped.append((goods, 'no case branch'))
        continue
    fields = cfg.split('$')
    if len(fields) < 10:
        dropped.append((goods, f'field count {len(fields)}'))
        continue
    category_name = fields[0].strip()
    item_spec = fields[1].strip()
    goods_idx = str_to_int_def(fields[2])
    src_price = str_to_int_def(fields[3])
    cur_price = str_to_int_def(fields[4])
    limit_type = str_to_int_def(fields[5])
    limit_count = str_to_int_def(fields[6])
    effect_img = str_to_int_def(fields[7])
    effect_count = str_to_int_def(fields[8])
    explain = fields[9].strip()
    if (not category_name or goods_idx == -1 or src_price == -1 or cur_price == -1
            or effect_img == -1 or effect_count == -1):
        dropped.append((goods, 'failed -1 validation'))
        continue
    tokens = [t for t in item_spec.split('/') if t]
    if len(tokens) != 1:
        dropped.append((goods, f'token count {len(tokens)} fail-closed'))
        continue
    token = tokens[0]
    split_at = token.rfind(':')
    granted = token[:split_at].strip() if split_at > 0 else token.strip()
    item_count = str_to_int_def(token[split_at+1:], 1) if split_at > 0 else 1
    parsed.append({
        'order': order,
        'name': goods,
        'category': category_name,
        'item_spec': item_spec,
        'granted': granted,
        'item_count': max(1, item_count),
        'id': goods_idx,
        'src': src_price,
        'cur': cur_price,
        'limit_type': limit_type,
        'limit_count': limit_count,
        'effect_img': effect_img,
        'effect_count': effect_count,
        'explain': explain,
        'raw': cfg,
    })

# first-seen categories
order_cat = []
for item in parsed:
    if item['category'] not in order_cat:
        if len(order_cat) >= 8:
            item['dropped_cat'] = True
            continue
        order_cat.append(item['category'])
    item['cat_id'] = order_cat.index(item['category'])

lines = []
def w(s=''):
    lines.append(s)

w(f'file: {PROD}')
w(f'size: {PROD.stat().st_size} bytes  encoding: GBK')
w(f'C_NeedLoadGoodsNames* constants: {len(all_consts)}')
for k, v in all_consts:
    w(f'  {k} = {v}')
w(f'configured names (first const, C# ResolveConfiguredGoodsNames): {len(configured)}')
w(f'  {configured}')
w(f"Result:='...' shape hits: {len(result_shape)}  (production should be 0)")
w(f'case-begin configs: {len(configs)}')
w(f'parsed OK: {len(parsed)}  dropped: {len(dropped)}')
for d in dropped:
    w(f'  DROP {d}')
w(f'category first-seen order: {order_cat}')
w()
w('--- field-level table ---')
w(f"{'#':>2} {'name':<10} {'cat':<6} {'cid':>3} {'idx':>4} {'src':>5} {'cur':>5} {'lt':>3} {'lc':>3} {'img':>4} {'ec':>3} spec")
for it in parsed:
    w(f"{it['order']+1:2d} {it['name']:<10} {it['category']:<6} {it.get('cat_id',-1):3d} "
      f"{it['id']:4d} {it['src']:5d} {it['cur']:5d} {it['limit_type']:3d} {it['limit_count']:3d} "
      f"{it['effect_img']:4d} {it['effect_count']:3d} {it['item_spec']}  explain={it['explain']!r}")

w()
w('--- raw $ strings (what native sub_636D68 would split) ---')
for it in parsed:
    w(f"  {it['name']}: {it['raw']}")

# GetLimitValue / SetLimitValue live (uncommented) GetV/SetV
live = re.sub(r'//[^\r\n]*', '', script)
glv = re.search(
    r"\bfunction\s+GetLimitValue\b(?P<body>.*?)\bend\s*;\s*(?:procedure|function|Begin)",
    live, re.I | re.S)
slots = []
if glv:
    slots = re.findall(
        r"'(?P<name>[^']+)'\s*:\s*Result\s*:=\s*This_Player\.Get(?P<bank>[VS])\s*\(\s*"
        r"(?P<group>-?\d+)\s*,\s*(?P<index>-?\d+)\s*\)",
        glv.group('body'), re.I)
w()
w(f'GetLimitValue live slots: {len(slots)}  {slots}')
w('EverydayClearLimitValue: SetV(91,1..50,0) and clamp GetV(89,I)<0 -> 0  (script, not engine)')
w()
w(f'VERDICT: production parse count = {len(parsed)} (must be > 0)')

text = '\n'.join(lines) + '\n'
OUT.write_text(text, encoding='utf-8')
print(text)
print('wrote', OUT)
