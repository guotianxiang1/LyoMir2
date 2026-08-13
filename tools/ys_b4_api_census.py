#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""眼神 B 类 B4（脚本 API 面）普查 —— 可复跑。

回答三个问题：

1. 权威 API 面到底有哪些名字？
   直接解析随包脚本，不抄任何既有报告：
     staging/_ys_out/AllFuc_208_DECRYPTED.txt   生产 AllFuc.txt 的解密件（UTF-8）
     staging/_ys_out/NpcFuc_208_DECRYPTED.txt
     staging/ys208_original_capture/.../AllFuc.pas  随包明文（\\r 行尾，GBK）
   外加官方《AllFuc 使用例子》里 20 个 `Ys*` 声明名。

2. 每个名字走哪条 `!!!!` 隧道、落到哪个原生 handler？
   从 Pascal 函数体里把隧道串抽出来（数字 / caret / 中文 / Give / PlayerNotice /
   GetItemNameOnBody），再查隧道跳表。跳表本身由 --verify 复核。

3. C# 侧每个登记名有没有原生字节佐证？
   判据同 docs/yanshen_completeness_audit_20260814.md §2.5：派发臂内联 VA，
   或其调用到的 `YanshenApi` 成员 ±70 行内有 VA。

用法:
    python tools/ys_b4_api_census.py <repo> [--verify]
"""
import sys, os, io, re, json, struct, collections

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VERIFY = '--verify' in sys.argv
ST = r'D:\loym2\staging'

DUMP_A = os.path.join(ST, 'yanshen208_strparam_runtime_dump_20260719', 'yanshen2_0_8_dll.memory.bin')
DUMP_B = os.path.join(ST, 'yanshen208_strparam_runtime_dump_delayed_20260719', 'yanshen2_0_8_dll.memory.bin')
BASE_A = 0x10000000

# ---------------------------------------------------------------- 隧道跳表 --
# 数字型分发器 0x100761A0：cmp [ebp-0x154],0x28 / ja / jmp [ecx*4+0x10077A78]
NUMERIC_TABLE_VA = 0x10077A78
NUMERIC_TABLE_N = 41
# caret 型分发器 0x1005DBA0：cmp edi,0x25 / ja / jmp [edi*4+0x1005E3D8]
CARET_TABLE_VA = 0x1005E3D8
CARET_TABLE_N = 38

# 每个数字 case 臂只是"配置门 + call 真 handler"，下表是臂里那条 call 的目标。
NUMERIC = {
    1: None, 2: 0x1006D690, 3: 0x1006DAB0, 4: 0x100700A0, 5: 0x100706A0, 6: 0x10070BF0,
    7: 0x10070D20, 8: 0x10070E70, 9: 0x10070FD0, 10: 0x10071710, 11: 0x10071920,
    12: 0x1006FDE0, 13: 0x10071A70, 14: 0x10071F10, 15: 0x10072650, 16: 0x100728A0,
    17: 0x10072CD0, 18: 0x10072F90, 19: 0x1006D3D0, 20: 0x10073210, 21: 0x10073440,
    22: 0x10072A30, 23: 0x100735B0, 24: 0x10073B40, 25: 0x10073E20, 26: 0x100740B0,
    27: 0x10074C60, 28: 0x10074EE0, 29: 0x10075090, 30: 0x10075170, 31: 0x10075600,
    32: 0x10075B70, 33: 0x10076060, 34: 0x1006E8D0, 35: 0x1006F0E0, 36: 0x1006F1C0,
    37: 0x1006F2C0, 38: 0x1006F630, 39: 0x1006F790, 40: 0x1006F8E0, 41: 0x1006FD00,
}
CARET = {
    1: 0x10058ED0, 2: 0x10059060, 3: 0x10059160, 4: 0x100592A0, 5: 0x100594C0,
    6: 0x10059A40, 7: 0x10059BE0, 8: 0x10059EC0, 9: 0x100596B0, 10: 0x1005A1D0,
    11: 0x1005A600, 12: 0x1005A900, 13: 0x1005AC10, 14: 0x1005AE70, 15: 0x1005B0B0,
    16: 0x1005B260, 17: 0x1005BDE0, 18: 0x1005BEC0, 19: 0x1005BB80, 20: 0x1005AD40,
    21: 0x1005B3F0, 22: 0x1005B5A0, 23: 0x1005B730, 24: 0x1005B8E0, 25: 0x1005BFC0,
    26: 0x1005C100, 27: 0x1005AFC0, 28: 0x1005BA70, 29: 0x1005C220, 30: 0x1005C330,
    31: 0x1005C4E0, 32: 0x1005C810, 33: 0x1005CDD0, 34: 0x1005CFF0, 35: 0x1005D290,
    36: 0x1005D4E0, 37: 0x1005D760, 38: 0x1005D9F0,
}
# 第三条隧道：Player.GetSignInActPrizer(lucker1, lucker2)
#   lucker1 = '!!!!^<id>^<args…>'，lucker2 = 'lucker2'（或 'libmysql' 走 SQL 支）
#   钩子 0x100879xx：比对第二实参 == "lucker2"(0x102C02EC) -> 按 '^' 切段（至少 3 段，
#   cmp eax,0x48 / jl）-> stoi(seg[1]) -> dec / cmp eax,7 / ja -> jmp [eax*4+0x10087C68]
#   ⇒ 8 个 opcode，AllFuc 只用了前 5 个。
LUCKER = {
    1: 0x100863B0, 2: 0x10086860, 3: 0x10086860, 4: 0x10086E60,
    5: 0x100872A0, 6: 0x10087400, 7: 0x10087620, 8: 0x10087850,
}
LUCKER_TABLE_VA = 0x10087C68
LUCKER_TABLE_N = 8
LUCKER_NOTE = {2: '（flag=0）', 3: '（flag=1，与 op2 同体）'}

CHINESE = {
    '集成函数': '数字分发器 0x100761A0',
    '爱心分割': 'caret 分发器 0x1005DBA0',
    'hq取sj戳': '内联 0x1005E65C -> [Self+0xE0]',
    'zd义回收': '[[0x1031BFB4]] vtable[0]',
    'plus伤害': '内联 0x1005E762（Themida 变形）',
    '给与元素': '内联 0x1005E7CE..0x1005EA88',
    '获取元素': '内联 0x1005EAE3..0x1005ED01',
    '定义伤害': '内联 0x1005EDDC.. -> [0x1031B9BC]',
    '英雄极品': '内联 0x1005EFDB.. -> [Self+0xBB0]',
}
# 官方《AllFuc 使用例子》里带签名声明、AllFuc.pas 里没有的 20 个 `Ys*` 名
NATIVE_DOC = """YsAttact YSBindItem YSChangeRole YSCreateMon YSFindPlayerByName YSGetBodyItem
YsGetG YsGetHeroshuxing YsGetItem YSGetItemID YsGetStr YSKillMon YSKillRole YsNewtuitui
YSSafeZone YSSay YsSetG YsSetStr YSyeman YSGetOnLinePlayerNum""".split()

DECL = re.compile(
    r'^[ \t]*(function|procedure)[ \t]+([A-Za-z_]\w*)[ \t]*(\([^)]*\))?[ \t]*(?::[ \t]*(\w+))?[ \t]*;',
    re.I | re.M)
RE_NUM = re.compile(r"!!!!集成函数,\s*(\d+)")
RE_CARET = re.compile(r"!!!!爱心分割\^(\d+)\^?")
RE_LUCKER = re.compile(r"!!!!\^(\d+)\^")
RE_SIGNIN = re.compile(r"GetSignInActPrizer\(\s*\w+\s*,\s*(\w+)\s*\)")
RE_GIVE = re.compile(r"!!!!(#ys[,……])")
RE_NOTICE = re.compile(r"PlayerNotice\('(#\$\$#[^']*)'")
RE_ONBODY = re.compile(r"GetItemNameOnBody\((\d+)\)")
RE_CH = re.compile(r"!!!!([^'^,#][^']*?)(?:'|\+)")


def read_script(p):
    b = open(p, 'rb').read().replace(b'\r\n', b'\n').replace(b'\r', b'\n')
    for enc in ('utf-8-sig', 'utf-8', 'gbk'):
        try:
            return b.decode(enc)
        except UnicodeDecodeError:
            continue
    return b.decode('gbk', errors='replace')


def parse_pascal(text):
    out, ms = [], list(DECL.finditer(text))
    for i, m in enumerate(ms):
        end = ms[i + 1].start() if i + 1 < len(ms) else len(text)
        out.append({'kind': m.group(1).lower(), 'name': m.group(2),
                    'sig': (m.group(3) or '').strip(), 'ret': (m.group(4) or '').strip(),
                    'body': text[m.end():end]})
    return out


def tunnels_of(body):
    t = []
    for m in RE_NUM.finditer(body):
        t.append(('numeric', int(m.group(1))))
    for m in RE_CARET.finditer(body):
        t.append(('caret', int(m.group(1))))
    for m in RE_LUCKER.finditer(body):
        t.append(('lucker', int(m.group(1))))
    if "'libmysql'" in body:
        t.append(('libmysql', 0))
    for m in RE_GIVE.finditer(body):
        t.append(('give', m.group(1)))
    for m in RE_NOTICE.finditer(body):
        t.append(('notice', m.group(1)))
    for m in RE_ONBODY.finditer(body):
        t.append(('onbody', int(m.group(1))))
    for m in RE_CH.finditer(body):
        if m.group(1) in CHINESE:
            t.append(('chinese', m.group(1)))
    seen, out = set(), []
    for x in t:
        if x not in seen:
            seen.add(x); out.append(x)
    return out


def describe(t):
    k, v = t
    if k == 'numeric':
        h = NUMERIC.get(v)
        return '数字%02d@%s' % (v, ('0x%08X' % h) if h else '内联')
    if k == 'caret':
        return 'caret%02d@0x%08X' % (v, CARET[v])
    if k == 'lucker':
        return 'lucker2^%d^@0x%08X%s' % (v, LUCKER[v], LUCKER_NOTE.get(v, ''))
    if k == 'libmysql':
        return 'GetSignInActPrizer/libmysql 支 @0x10087DC0'
    if k == 'chinese':
        return '中文%s[%s]' % (v, CHINESE[v])
    if k == 'give':
        return 'Give载荷%s' % v
    if k == 'notice':
        return 'PlayerNotice %s' % v
    if k == 'onbody':
        return 'GetItemNameOnBody(%d)' % v
    return str(t)


def verify_tables():
    """复核两张跳表确实是 41 / 38 项，并且数字臂里那条 call 的目标与上表一致。"""
    from capstone import Cs, CS_ARCH_X86, CS_MODE_32
    a = open(DUMP_A, 'rb').read()
    b = open(DUMP_B, 'rb').read()
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    ok = True

    def dw(buf, va, n):
        o = va - BASE_A
        return list(struct.unpack('<%dI' % n, buf[o:o + 4 * n]))

    narms = dw(a, NUMERIC_TABLE_VA, NUMERIC_TABLE_N)
    carms = dw(a, CARET_TABLE_VA, CARET_TABLE_N)
    larms = dw(a, LUCKER_TABLE_VA, LUCKER_TABLE_N)
    print('数字跳表 0x%08X: %d 项  %08X..%08X' % (NUMERIC_TABLE_VA, len(narms), narms[0], narms[-1]))
    print('caret 跳表 0x%08X: %d 项  %08X..%08X' % (CARET_TABLE_VA, len(carms), carms[0], carms[-1]))
    print('lucker2 跳表 0x%08X: %d 项  %08X..%08X' % (LUCKER_TABLE_VA, len(larms), larms[0], larms[-1]))
    for arr, tag in ((narms, '数字'), (carms, 'caret'), (larms, 'lucker2')):
        bad = [x for x in arr if not (0x10001000 <= x < 0x1027CC40)]
        if bad:
            ok = False
            print('  !! %s 跳表有 %d 项不在 .text 内: %s' % (tag, len(bad), ['%08X' % x for x in bad[:4]]))

    # 数字臂 -> handler：臂里最后一条 call rel32 就是真 handler
    delta = 0x47C40000
    for i, arm in enumerate(narms, start=1):
        want = NUMERIC.get(i)
        if want is None:
            continue
        o = arm - BASE_A
        found = None
        for ins in md.disasm(b[o:o + 0x200], arm + delta):
            if ins.mnemonic == 'call' and ins.op_str.startswith('0x'):
                t = int(ins.op_str, 16)
                if 0x10001000 + delta <= t < 0x1027CC40 + delta:
                    found = t - delta
            if ins.mnemonic == 'jmp' and ins.op_str.startswith('0x'):
                break
        if found != want:
            ok = False
            print('  !! 数字 %d 臂 0x%08X 的 call 目标 %s != 表里的 0x%08X'
                  % (i, arm, ('0x%08X' % found) if found else 'None', want))

    # lucker2 臂 -> handler：臂里第一条"非运行库辅助"的 call
    # （op3 只 push 1 然后 jmp 进 op2 的尾巴，所以要跟一跳）
    RT = {0x10064A70, 0x1000B710, 0x1000B650, 0x1000B330, 0x10018460, 0x10018680}

    def first_real_call(va, budget=0x40, hops=1):
        for ins in md.disasm(b[va - BASE_A: va - BASE_A + budget], va + delta):
            if ins.mnemonic == 'call' and ins.op_str.startswith('0x'):
                t = int(ins.op_str, 16) - delta
                if t not in RT:
                    return t
            if ins.mnemonic == 'jmp' and ins.op_str.startswith('0x') and hops:
                return first_real_call(int(ins.op_str, 16) - delta, budget, hops - 1)
        return None

    for i, arm in enumerate(larms, start=1):
        want = LUCKER[i]
        found = first_real_call(arm)
        if found != want:
            ok = False
            print('  !! lucker2 %d 臂 0x%08X 的 call 目标 %s != 表里的 0x%08X'
                  % (i, arm, ('0x%08X' % found) if found else 'None', want))
    print('跳表复核: %s' % ('通过' if ok else '不通过'))
    return ok


def main():
    allfuc = parse_pascal(read_script(os.path.join(ST, '_ys_out', 'AllFuc_208_DECRYPTED.txt')))
    npcfuc = parse_pascal(read_script(os.path.join(ST, '_ys_out', 'NpcFuc_208_DECRYPTED.txt')))
    capfuc = parse_pascal(read_script(os.path.join(
        ST, 'ys208_original_capture', 'Mir200', 'Envir', 'CommonScripts', '眼神专用', 'AllFuc.pas')))

    print('AllFuc(生产解密) %d 声明 / AllFuc(随包明文) %d / NpcFuc %d'
          % (len(allfuc), len(capfuc), len(npcfuc)))
    a = {d['name'].lower() for d in allfuc}
    c = {d['name'].lower() for d in capfuc}
    print('  仅生产件有: %s' % sorted(a - c))
    print('  仅随包件有: %s' % sorted(c - a))

    catalog = {}
    for d in allfuc:
        catalog[d['name'].lower()] = ('AllFuc.pas', d)
    for d in npcfuc:
        catalog.setdefault(d['name'].lower(), ('NpcFuc.pas', d))
    for n in NATIVE_DOC:
        catalog.setdefault(n.lower(), ('官方例子', {'kind': 'native', 'name': n, 'sig': '',
                                                    'ret': '', 'body': ''}))
    print('权威面合计 %d = AllFuc %d + NpcFuc %d + 官方例子 %d'
          % (len(catalog), len(allfuc), len(npcfuc), len(NATIVE_DOC)))

    br = open(os.path.join(REPO, 'GameSvr', 'ScriptSystem', 'PasEngine',
                           'PasApiBridge.Yanshen.cs'), encoding='utf-8-sig').read()
    m = re.search(r'YanshenApiNames\s*=\s*new\(\s*@"(.*?)"\.Split', br, re.S)
    registered = set(m.group(1).split())
    print('C# 登记 %d；权威面未登记 %d；登记但不在权威面 %d'
          % (len(registered), len(set(catalog) - registered), len(registered - set(catalog))))

    # ---- 原生 VA 佐证 ----
    apilines = open(os.path.join(REPO, 'GameSvr', 'Plugins', 'YanshenApi.cs'),
                    encoding='utf-8-sig').read().splitlines()
    apitext = '\n'.join(apilines)
    VA = re.compile(r'0x(?:10[0-9A-Fa-f]{6}|0?0?[4-7][0-9A-Fa-f]{5})\b')
    MEMBER = re.compile(r'^[ \t]*(?:(?:public|private|internal|protected|static|readonly|override'
                        r'|virtual|sealed|abstract|partial|new)\s+)+[\w<>\[\],?\.]+\s+(\w+)\s*[\(\{=]', re.M)
    # 判据用"成员自身的跨度"，不用 ±70 行窗口。窗口法会让一段长注释把邻居一起算成
    # 有佐证 —— 本轮给 GetHeroExtreme / GetOther 写完字节注释后，窗口法把证据债从
    # 33 直接算到 11，其中一多半是邻居蹭到的，不是真的补了证据。
    decls = list(MEMBER.finditer(apitext))
    spans = collections.defaultdict(list)
    for i, d in enumerate(decls):
        # 往前吃掉紧邻的 /// 文档注释块，它才是写字节佐证的地方
        head = apitext.rfind('\n\n', 0, d.start())
        lo = head + 1 if head >= 0 else d.start()
        hi = decls[i + 1].start() if i + 1 < len(decls) else len(apitext)
        spans[d.group(1)].append((lo, hi))
    arms = collections.defaultdict(list)
    for am in re.finditer(r'case\s+"([^"]+)"\s*:', br):
        nxt = br.find('case "', am.end())
        arms[am.group(1).lower()].append(br[am.end(): nxt if nxt > 0 else am.end() + 4000])

    def has_va(name):
        for lo, hi in spans.get(name, ()):
            if VA.search(apitext[lo:hi]):
                return True
        return False

    rows = []
    for n in sorted(registered):
        where, d = catalog.get(n, ('?', {'body': '', 'sig': '', 'ret': '', 'name': n}))
        tun = tunnels_of(d.get('body', ''))
        seg = ' '.join(arms.get(n, []))
        cited = bool(VA.search(seg))
        calls = sorted(set(re.findall(r'\b_?[aA]pi\.(\w+)\s*\(', seg)))
        if not cited:
            cited = any(has_va(x) for x in calls)
        rows.append({'name': d.get('name', n), 'key': n, 'src': where,
                     'sig': d.get('sig', ''), 'ret': d.get('ret', ''),
                     'tunnels': [describe(t) for t in tun], 'cited': cited,
                     'api': calls})

    debt = [r for r in rows if not r['cited']]
    provable = [r for r in debt if r['tunnels']]
    unreach = [r for r in debt if not r['tunnels']]
    print()
    print('登记名有原生 VA 佐证 : %d' % (len(rows) - len(debt)))
    print('登记名无佐证（证据债）: %d  其中有隧道可证 %d / 无隧道 %d'
          % (len(debt), len(provable), len(unreach)))
    print()
    print('--- 有隧道、可证（应当补证据） ---')
    for r in provable:
        print('  %-26s %-10s %s' % (r['name'], r['src'], ' | '.join(r['tunnels'])))
    print()
    print('--- 无隧道（插件无按名注册机制，静态不可达） ---')
    for r in unreach:
        print('  %-26s %-10s api=%s' % (r['name'], r['src'], ','.join(r['api']) or '-'))

    print()
    print('--- 权威面有、C# 未登记（%d） ---' % len(set(catalog) - registered))
    for n in sorted(set(catalog) - registered):
        where, d = catalog[n]
        tun = tunnels_of(d.get('body', ''))
        print('  %-26s %-10s %s' % (d['name'], where,
                                    ' | '.join(describe(t) for t in tun) or '（纯 Pascal，无隧道）'))

    out = os.path.join(REPO, 'docs', 'ys_b4_api_census.tsv')
    with open(out, 'w', encoding='utf-8', newline='') as f:
        f.write('name\tsource\tregistered\tcited_native_va\ttunnels\tret\tsig\n')
        allkeys = sorted(set(catalog) | registered)
        for k in allkeys:
            where, d = catalog.get(k, ('C#-only', {'name': k, 'sig': '', 'ret': '', 'body': ''}))
            tun = tunnels_of(d.get('body', ''))
            r = next((x for x in rows if x['key'] == k), None)
            f.write('%s\t%s\t%s\t%s\t%s\t%s\t%s\n' % (
                d.get('name', k), where, 'yes' if k in registered else 'no',
                ('yes' if r['cited'] else 'no') if r else '-',
                ' | '.join(describe(t) for t in tun), d.get('ret', ''),
                re.sub(r'\s+', ' ', d.get('sig', ''))))
    print('\n已写出 %s' % out)

    if VERIFY:
        print()
        verify_tables()


main()
