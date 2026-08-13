#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Delphi VMT index for the M2Server flat image.

A Delphi VMT is identified unambiguously by its self pointer:
    [V - 0x4C] == V           (vmtSelfPtr)
Layout used here:
    -0x2C vmtClassName -> ShortString
    -0x28 vmtInstanceSize
    -0x24 vmtParent -> VMT of ancestor (or 0)
    +0x00.. virtual method slots
"""
import struct, json, sys, bisect

IMG = r'D:\loym2\staging\_reunpack_work\flat_image.bin'
BASE = 0x400000

class Vmts:
    def __init__(self, data=None):
        self.data = data or open(IMG, 'rb').read()
        self.n = len(self.data)
        self.by_va = {}
        self._build()

    def d32(self, va):
        o = va - BASE
        if o < 0 or o + 4 > self.n:
            return None
        return struct.unpack('<I', self.data[o:o + 4])[0]

    def sstr(self, va):
        o = va - BASE
        if o < 0 or o >= self.n:
            return None
        ln = self.data[o]
        return self.data[o + 1:o + 1 + ln].decode('latin1', 'replace')

    def _build(self):
        d = self.data
        for o in range(0, self.n - 4, 4):
            v = struct.unpack('<I', d[o:o + 4])[0]
            va = BASE + o + 0x4C
            if v != va:
                continue
            name_ptr = self.d32(va - 0x2C)
            if not name_ptr or not (BASE <= name_ptr < BASE + self.n):
                continue
            nm = self.sstr(name_ptr)
            if not nm or not nm[:1].isalpha():
                continue
            if not all(32 <= ord(c) < 127 for c in nm):
                continue
            # vmtParent is a PPointer: [V-0x24] -> address of a slot holding the parent VMT
            pp = self.d32(va - 0x24)
            parent = self.d32(pp) if pp and BASE <= pp < BASE + self.n else 0
            self.by_va[va] = dict(
                va=va, name=nm,
                size=self.d32(va - 0x28),
                parent=parent)
        # method slot -> owner map
        self.slot_owner = {}
        for va, info in self.by_va.items():
            for i in range(0, 400):
                p = self.d32(va + i * 4)
                if p is None or not (0x401000 <= p < 0x7F0000):
                    break
                self.slot_owner.setdefault(p, []).append((info['name'], i))

    def chain(self, va):
        out = []
        seen = set()
        while va and va in self.by_va and va not in seen:
            seen.add(va)
            out.append(self.by_va[va]['name'])
            va = self.by_va[va]['parent']
        return out

    def name_of(self, va):
        i = self.by_va.get(va)
        return i['name'] if i else None

    def find(self, substr):
        return [i for i in self.by_va.values() if substr.lower() in i['name'].lower()]

if __name__ == '__main__':
    v = Vmts()
    print('VMTs found: %d' % len(v.by_va))
    if len(sys.argv) > 1:
        for a in sys.argv[1:]:
            if a.startswith('0x'):
                va = int(a, 16)
                # is it a method?
                own = v.slot_owner.get(va)
                print('%08X owner=%s vmtname=%s' % (va, own, v.name_of(va)))
            else:
                for i in sorted(v.find(a), key=lambda x: x['name']):
                    print('%08X size=%-6d %-34s parents=%s' % (
                        i['va'], i['size'] or 0, i['name'], '->'.join(v.chain(i['parent'])[:4])))
