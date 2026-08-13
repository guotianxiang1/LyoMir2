# -*- coding: utf-8 -*-
"""总开关 / 回收倍率 / 其他 三个复合字段的「子键缺失」处理形状。"""
import io
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re")
import yslib as Y

out = io.StringIO()
P = lambda *a: print(*a, file=out)

P("### 总开关：0x1006B45E..0x1006B57A（存在性 + v1 + v2 + 关闭值，四个 je 的目标）")
P(Y.show_ys(0x1006B45E, maxbytes=0x11C))
P("")
P("### 回收倍率：0x1006B65E..0x1006B770")
P(Y.show_ys(0x1006B65E, maxbytes=0xB4))
P("")
P("### 其他：0x1006B995..0x1006BA20")
P(Y.show_ys(0x1006B995, maxbytes=0x8C))
P("")
P("### 元宝/灵符/金币/经验 四个标量键的缺失处理（元宝为例 0x1006B7E5..0x1006B830）")
P(Y.show_ys(0x1006B7E5, maxbytes=0x4C))

with open(r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re\v7_out.txt", "w",
          encoding="utf-8") as fh:
    fh.write(out.getvalue())
print("ok")
