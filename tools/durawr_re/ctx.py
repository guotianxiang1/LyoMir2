#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Context disassembler for the M2Server flat image.

  ctx.py fn   <va> [maxbytes]   disassemble a function from its entry
  ctx.py at   <va> [back] [fwd] disassemble a window around a VA
  ctx.py xref <va>              find callers (E8/E9 rel32) + dword table refs
  ctx.py str  <va>              show the Delphi literal at VA

Annotations: Delphi AnsiString literals (len at -4, refcnt at -8), VMT class
names, and known helper labels.
"""
import sys, struct, bisect
from capstone import *
from capstone.x86 import *
from vmt import Vmts, IMG, BASE

KNOWN = {
    0x404828: 'Delphi.IsClass(obj,vmt)',
    0x4048C8: 'Delphi.IsClass.inner',
    0x75EC20: 'GetUseItems(container,slot)',
    0x784584: 'TBaseItem.SetDura',
    0x7845A0: 'TBaseItem.GetDura',
    0x7845A8: 'TBaseItem.GetDuraMax',
    0x404F04: 'Delphi.LStrAsg',
    0x404E6C: 'Delphi.LStrClr',
    0x4051F4: 'Delphi.LStrCatN',
    0x40FA34: 'Delphi.Random',
}

class Ctx:
    def __init__(self):
        self.data = open(IMG, 'rb').read()
        self.n = len(self.data)
        self.md = Cs(CS_ARCH_X86, CS_MODE_32)
        self.md.detail = True
        self.v = Vmts(self.data)
        self._fn_index = None

    # ---------- helpers ----------
    def d32(self, va):
        o = va - BASE
        if o < 0 or o + 4 > self.n:
            return None
        return struct.unpack('<I', self.data[o:o + 4])[0]

    def lit(self, va):
        """Delphi AnsiString literal at VA, if it looks like one."""
        o = va - BASE
        if o < 8 or o >= self.n:
            return None
        ln = struct.unpack('<i', self.data[o - 4:o])[0]
        if not (0 < ln < 500) or o + ln > self.n:
            return None
        b = self.data[o:o + ln]
        if self.data[o + ln] != 0:
            return None
        try:
            s = b.decode('gbk')
        except Exception:
            try:
                s = b.decode('latin1')
            except Exception:
                return None
        if any(ord(c) < 9 for c in s):
            return None
        return s

    def sstr(self, va):
        o = va - BASE
        if o < 0 or o >= self.n:
            return None
        ln = self.data[o]
        if ln == 0 or o + 1 + ln > self.n:
            return None
        b = self.data[o + 1:o + 1 + ln]
        try:
            return b.decode('gbk')
        except Exception:
            return None

    def label(self, va):
        if va in KNOWN:
            return KNOWN[va]
        nm = self.v.name_of(va)
        if nm:
            return 'VMT:' + nm
        own = self.v.slot_owner.get(va)
        if own:
            return '%s.vslot%d' % (own[0][0], own[0][1])
        return None

    def annotate(self, ins):
        notes = []
        for op in ins.operands:
            vals = []
            if op.type == X86_OP_IMM:
                vals.append(op.imm & 0xFFFFFFFF)
            elif op.type == X86_OP_MEM and op.mem.base == 0 and op.mem.index == 0:
                vals.append(op.mem.disp & 0xFFFFFFFF)
            for val in vals:
                if not (BASE <= val < BASE + self.n):
                    continue
                lb = self.label(val)
                if lb:
                    notes.append('%08X=%s' % (val, lb))
                    continue
                s = self.lit(val)
                if s:
                    notes.append('"%s"' % s)
                    continue
                # pointer to a VMT (class global)
                p = self.d32(val)
                if p and self.v.name_of(p):
                    notes.append('[%08X]=VMT:%s' % (val, self.v.name_of(p)))
        if ins.mnemonic in ('call', 'jmp') and ins.operands and ins.operands[0].type == X86_OP_IMM:
            t = ins.operands[0].imm & 0xFFFFFFFF
            lb = self.label(t)
            if lb and not any(lb in x for x in notes):
                notes.append(lb)
        return '  ; ' + ' | '.join(notes) if notes else ''

    # ---------- commands ----------
    def dis(self, start, end):
        o = start - BASE
        out = []
        while BASE + o < end:
            ins = next(self.md.disasm(self.data[o:o + 16], BASE + o, count=1), None)
            if ins is None:
                out.append('%08X  db %02X' % (BASE + o, self.data[o]))
                o += 1
                continue
            out.append('%08X  %-22s %-38s%s' % (
                ins.address, ins.bytes.hex(), '%s %s' % (ins.mnemonic, ins.op_str),
                self.annotate(ins)))
            o += ins.size
        return out

    def fn(self, start, maxb=0x600):
        """Disassemble until a ret followed by padding/next-function heuristics."""
        o = start - BASE
        end = o + maxb
        out = []
        depth_ret = 0
        while o < end:
            ins = next(self.md.disasm(self.data[o:o + 16], BASE + o, count=1), None)
            if ins is None:
                out.append('%08X  db %02X' % (BASE + o, self.data[o]))
                o += 1
                continue
            out.append('%08X  %-22s %-38s%s' % (
                ins.address, ins.bytes.hex(), '%s %s' % (ins.mnemonic, ins.op_str),
                self.annotate(ins)))
            o += ins.size
            if ins.mnemonic == 'ret':
                depth_ret += 1
                # stop if next bytes are alignment padding or a new prologue
                nxt = self.data[o:o + 3]
                if nxt[:1] in (b'\x90', b'\xcc', b'\x00') or nxt == b'\x55\x8b\xec':
                    break
                if depth_ret >= 12:
                    break
        return out

    def xref(self, target):
        res = []
        t = target - BASE
        for op, kind in ((b'\xe8', 'call'), (b'\xe9', 'jmp')):
            i = 0
            while True:
                i = self.data.find(op, i)
                if i < 0 or i + 5 > self.n:
                    break
                rel = struct.unpack('<i', self.data[i + 1:i + 5])[0]
                if i + 5 + rel == t:
                    res.append((BASE + i, kind))
                i += 1
        pat = struct.pack('<I', target)
        i = 0
        while True:
            i = self.data.find(pat, i)
            if i < 0:
                break
            res.append((BASE + i, 'dword'))
            i += 1
        return res

def main():
    c = Ctx()
    cmd = sys.argv[1]
    if cmd == 'fn':
        va = int(sys.argv[2], 16)
        mx = int(sys.argv[3], 0) if len(sys.argv) > 3 else 0x600
        print('=== function %08X ===' % va)
        print('\n'.join(c.fn(va, mx)))
    elif cmd == 'at':
        va = int(sys.argv[2], 16)
        b = int(sys.argv[3], 0) if len(sys.argv) > 3 else 0x40
        f = int(sys.argv[4], 0) if len(sys.argv) > 4 else 0x40
        print('\n'.join(c.dis(va - b, va + f)))
    elif cmd == 'xref':
        va = int(sys.argv[2], 16)
        for a, k in c.xref(va):
            extra = ''
            if k == 'dword':
                own = c.v.slot_owner.get(va)
                extra = ' (vmt/table)' if own else ''
            print('%08X  %s%s' % (a, k, extra))
    elif cmd == 'str':
        va = int(sys.argv[2], 16)
        print(repr(c.lit(va)), repr(c.sstr(va)))

if __name__ == '__main__':
    main()
