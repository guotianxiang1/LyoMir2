#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""F2 脚本 API 面 — 批量 handler 反汇编 + 门控/元数摘要（只读）。"""
import io, re, sys, struct
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

DUMP = r'D:\loym2\staging\yanshen208_strparam_runtime_dump_20260719\yanshen2_0_8_dll.memory.bin'
BASE = 0x10000000

with open(DUMP, 'rb') as f:
    DATA = f.read()

md = Cs(CS_ARCH_X86, CS_MODE_32)
md.detail = False

# name -> (handler_va, arm_note)
HANDLERS = {
    'ys_addhp/ys_addmp': (0x10071920, 'numeric 11 arm 0x10076D4E'),
    'ys_addshuxing*': (0x10071F10, 'numeric 14 arm 0x10076E8B'),
    'ys_bbflowme': (0x1006F0E0, 'numeric 35 arm 0x10077728'),
    'ys_change_ly': (0x1005A1D0, 'caret 10 arm 0x1005DD42'),
    'ys_checkwupinisbind': (0x10073440, 'numeric 21 arm 0x1007716A'),
    'ys_geta/ys_seta': (0x1006F8E0, 'numeric 40 arm 0x10077926'),
    'ys_getclientitemidbyitemid': (0x1005AD40, 'caret 20 arm 0x1005E1C2'),
    'ys_getdatabyclientitemid': (0x10086E60, 'lucker2 op4 arm 0x10087B6D'),
    'ys_getitemdbdata': (0x1005D9F0, 'caret 38 arm 0x1005E33C'),
    'ys_getitemid': (0x1005AC10, 'caret 13 arm 0x1005DD6A'),
    'ys_getitemjp': (0x1005D290, 'caret 35 arm 0x1005E218'),
    'ys_getmember*': (0x1006F630, 'numeric 38 arm 0x10077854'),
    'ys_getpis': (0x1005EAE3, 'chinese 获取元素 inline'),
    'ys_getshuxing': (0x1005C4E0, 'caret 31 arm 0x1005E218'),
    'ys_getys': (0x10072F90, 'numeric 18 arm 0x10077099'),
    'ys_givebb_sx': (0x10075600, 'numeric 31 arm 0x1007744F'),
    'ys_givebbskill': (0x10075170, 'numeric 30 arm 0x100773E0'),
    'ys_giveexp': (0x10075090, 'numeric 29 arm 0x100774B2'),
    'ys_givepis': (0x1005E7CE, 'chinese 给与元素 inline'),
    'ys_killbbbyname': (0x1005C810, 'caret 32 arm 0x1005E2A2'),
    'ys_magic_huoqiang': (0x1006F2C0, 'numeric 37 arm 0x100778BD'),
    'ys_myskillexp': (0x10071710, 'numeric 10 arm 0x10076CE5'),
    'ys_repairinbag': (0x1005C330, 'caret 30 arm 0x1005E1F8'),
    'ys_setherocskill': (0x10074EE0, 'numeric 28 arm 0x10077418'),
    'ys_settimerbyname': (0x10073E20, 'numeric 25 arm 0x10077289'),
    'ys_setys': (0x10072CD0, 'numeric 17 arm 0x10077099'),
    'ys_sqldbinsert': (0x10058ED0, 'caret 1 arm 0x1005DD1A'),
    'ys_sqldbselect': (0x10087DC0, 'libmysql selector 0x10087DD9'),
    'ys_senddbmsg': (0x10059160, 'caret 3 arm 0x1005DD6A'),
    'ys_tantanskill': (0x100740B0, 'numeric 26 arm 0x10077377'),
    'ys_wupingetdata*': (0x10086860, 'lucker2 op2/3 arm 0x10087AD1'),
    'ys_wupinmakeindex': (0x100863B0, 'lucker2 op1 arm 0x10087AD1'),
    'ys_test_ground': (0x100728A0, 'numeric 16 arm 0x1007716A'),
    'ys_ground_other': (0x10072A30, 'numeric 22 arm 0x100772A6'),
    'ys_updatabody': (0x1005C220, 'caret 29 arm 0x1005E1C2'),
    'ys_attact/directattack': (0x1005EDDC, 'chinese 定义伤害 inline'),
    'ys_decexp': (0x1006F790, 'numeric 39 arm 0x100778BD'),
    'ys_doeffect': (0x1006FDE0, 'numeric 12 arm 0x10076DB7'),
    'ys_dropitem': (0x10070D20, 'numeric 7 arm 0x10076C7C'),
    'ys_givebind': (0x10076060, 'numeric 33 arm 0x1007765F'),
    'ys_giveduar': (0x10072650, 'numeric 15 arm 0x10076EF5'),
    'ys_npcgiveitemys': (0x10073B40, 'numeric 24 arm 0x100772A6'),
    'ys_pick': (0x1006D3D0, 'numeric 19 arm 0x10077099'),
    'ys_playerout': (0x1006FD00, 'numeric 41 arm 0x1007798D'),
    'ys_rename': (0x10071920, 'numeric 11? check'),
    'ys_setitemjp': (0x1005D4E0, 'caret 36 arm 0x1005E242'),
    'ys_setpetv': (0x100735B0, 'numeric 23 arm 0x10077289'),
    'ys_getsxbyname': (0x1005D760, 'caret 37 arm 0x1005E26C'),
    'ys_checkmapmonbyname': (0x10073210, 'numeric 20 arm 0x1007716A'),
}


def disasm_window(va, n=35):
    buf = DATA[va - BASE:va - BASE + n * 16]
    out = []
    for ins in md.disasm(buf, va):
        out.append((ins.address, ins.bytes.hex(), ins.mnemonic, ins.op_str))
        if len(out) >= n:
            break
    return out


def scan_gates(insns):
    gates = []
    for i, (va, hx, mn, op) in enumerate(insns):
        if mn == 'cmp' and '0x1f4' in op.lower():
            gates.append(f'0x{va:08X} cmp …,0x1F4')
        if mn == 'mov' and '0x1031c244' in op.lower():
            gates.append(f'0x{va:08X} mov …,[0x1031C244] cfg2+0x11C')
        if mn == 'mov' and '0x1031c240' in op.lower():
            gates.append(f'0x{va:08X} mov …,[0x1031C240] cfg2+0x524')
    return gates


def scan_token_floor(insns):
    for va, hx, mn, op in insns:
        if mn == 'cmp' and re.search(r',\s*0x[0-9a-f]+', op, re.I):
            m = re.search(r'0x([0-9a-f]+)', op, re.I)
            if m:
                val = int(m.group(1), 16)
                if 3 <= val <= 0x20:
                    return f'0x{va:08X} {mn} {op}'
    return None


def scan_ret(insns):
    for va, hx, mn, op in insns:
        if mn == 'mov' and op.startswith('eax,') and '0xfffffc88' in op.lower():
            return f'0x{va:08X} ret -888'
        if mn == 'mov' and op.startswith('eax,') and '0xffffffff' in op.lower():
            return f'0x{va:08X} ret -1'
        if mn == 'or' and op.startswith('eax,') and '0xffffffff' in op.lower():
            return f'0x{va:08X} or eax,-1'
    return None


print('name\thandler\tgates\ttoken_floor\tret_pattern')
seen = set()
for name, (hva, note) in sorted(HANDLERS.items(), key=lambda x: x[1][0]):
    if hva in seen:
        continue
    seen.add(hva)
    ins = disasm_window(hva, 45)
    gates = '; '.join(scan_gates(ins)[:3]) or '-'
    tf = scan_token_floor(ins) or '-'
    ret = scan_ret(ins) or '-'
    print(f'{name}\t0x{hva:08X}\t{gates}\t{tf}\t{ret}\t# {note}')
