# -*- coding: utf-8 -*-
"""把可叠材料分支的五路单价 / 五个累加器 与栈槽对应起来，并找出 0x1031BCxx 全部初始化点。"""
import io
import re
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re")
import yslib as Y

out = io.StringIO()
P = lambda *a: print(*a, file=out)

P("=" * 78)
P("### 0x1031BCxx / 0x10310Dxx 全部写入点（mov [imm32], edx/eax/esi 形状）")
P("=" * 78)
data = Y.ys()
for slot in (0x1031BC4C, 0x1031BC50, 0x1031BC54, 0x1031BC58, 0x1031BC5C,
             0x1031BC60, 0x1031BC64, 0x1031BC68, 0x1031BCC4):
    le = slot.to_bytes(4, "little")
    hits = []
    for pre in (b"\x89\x15", b"\xA3", b"\x89\x35", b"\x89\x0D", b"\x89\x1D", b"\x89\x3D",
                b"\x89\x05", b"\x89\x25", b"\x89\x2D"):
        for off in Y.findall(data, pre + le):
            hits.append((Y.YS_BASE + off, len(pre)))
    P("")
    P("[%08X]  写入点 %d 个" % (slot, len(hits)))
    for va, _pl in sorted(set(hits)):
        # 往前 6 字节通常是 add edx, imm32
        prev = Y.bytes_ys(va - 6, 6)
        note = ""
        if prev[0] == 0x81 and prev[1] == 0xC2:
            note = "  <- add edx,0x%08X" % int.from_bytes(prev[2:6], "little")
        P("   %08X  %s%s" % (va, Y.hexb(Y.bytes_ys(va, 6)), note))

P("")
P("=" * 78)
P("### 可叠材料分支 五路结算 0x1006BBD8..0x1006BD30")
P("=" * 78)
P(Y.show_ys(0x1006BBD8, maxbytes=0x158))

with open(r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re\v3_out.txt", "w",
          encoding="utf-8") as fh:
    fh.write(out.getvalue())
print("ok")
