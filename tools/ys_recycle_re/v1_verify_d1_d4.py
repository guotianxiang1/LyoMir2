# -*- coding: utf-8 -*-
"""复核 D1（先删后结算无回滚）/ D2（总额一次落账）/ D3（缩放前判零）/ D4（元宝直接加）。"""
import io
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re")
import yslib as Y

out = io.StringIO()
P = lambda *a: print(*a, file=out)

P("=" * 78)
P("D3 / 产出全 <= 0 的判据（可叠材料分支）  0x1006BB25..0x1006BB60")
P("=" * 78)
P(Y.show_ys(0x1006BB25, maxbytes=0x40))

P("")
P("=" * 78)
P("D3 / 物品种类分支同构段  0x1006CC55..0x1006CC90")
P("=" * 78)
P(Y.show_ys(0x1006CC55, maxbytes=0x40))

P("")
P("=" * 78)
P("D1 / 删除段  0x1006BB60..0x1006BBD8（重读 FList、Delete、下发、Dispose）")
P("=" * 78)
P(Y.show_ys(0x1006BB60, maxbytes=0x80))

P("")
P("=" * 78)
P("D1+D2 / 删除之后的四路累加  0x1006BBD8..0x1006BC10")
P("=" * 78)
P(Y.show_ys(0x1006BBD8, maxbytes=0x40))

P("")
P("=" * 78)
P("D2+D4 / 循环后的总额落账  0x1006CE42..0x1006CEC0")
P("=" * 78)
P(Y.show_ys(0x1006CE42, maxbytes=0x80))

P("")
P("=" * 78)
P("D4 / 元宝落账目标 [0x1031BC64] 的静态值 + M2 侧函数头")
P("=" * 78)
for slot, name in ((0x1031BC50, "exp"), (0x1031BC54, "SetV"), (0x1031BC58, "GetV"),
                   (0x1031BC5C, "IncGold"), (0x1031BC60, "LingFu"), (0x1031BC64, "Yuanbao"),
                   (0x1031BC68, "TList.Delete"), (0x1031BC4C, "Dispose")):
    v = int.from_bytes(Y.bytes_ys(slot, 4), "little")
    P("  [%08X] = %08X   (%s)   bytes %s" % (slot, v, name, Y.hexb(Y.bytes_ys(slot, 4))))

P("")
P("--- IncGold 0x6D791C 上限判定 ---")
P(Y.show_m2(0x6D791C, maxbytes=0x30))

P("")
P("--- 元宝 0x6F8730 函数头 ---")
P(Y.show_m2(0x6F8730, maxbytes=0x30))

sys.stdout.write(out.getvalue())
with open(r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re\v1_out.txt", "w",
          encoding="utf-8") as fh:
    fh.write(out.getvalue())
