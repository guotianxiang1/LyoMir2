#!/usr/bin/env python
# -*- coding: utf-8 -*-
import lib
from lib import p, callers, funcstart

for va, n, name in [
    (0x75F4F8, 0x50, "sub_75F4F8 zero"),
    (0x75F548, 0x60, "sub_75F548 post"),
    (0x758AC0, 0x200, "sub_758AC0 (manager, ecx=agg1, edx=container)"),
]:
    print("=== %s ===" % name)
    p(va, n)
    print()
