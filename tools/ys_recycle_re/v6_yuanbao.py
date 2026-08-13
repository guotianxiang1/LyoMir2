# -*- coding: utf-8 -*-
"""元宝落账目标 0x6F8730 的契约（D4 要用）。"""
import io
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re")
import yslib as Y

out = io.StringIO()
P = lambda *a: print(*a, file=out)

P("=" * 78)
P("### 0x6F8730 全函数（到 0x6F8860）")
P("=" * 78)
P(Y.show_m2(0x6F8730, maxbytes=0x130))

P("")
P("=" * 78)
P("### 0x6F8730 的 rel32 调用者")
P("=" * 78)
d = Y.m2()
n = 0
for off in range(0, len(d) - 5):
    if d[off] != 0xE8:
        continue
    tgt = Y.M2_BASE + off + 5 + int.from_bytes(d[off + 1:off + 5], "little", signed=True)
    if tgt == 0x6F8730:
        n += 1
        P("   caller @%08X" % (Y.M2_BASE + off))
P("   共 %d 个" % n)

with open(r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re\v6_out.txt", "w",
          encoding="utf-8") as fh:
    fh.write(out.getvalue())
print("ok")
