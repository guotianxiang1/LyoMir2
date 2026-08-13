# -*- coding: utf-8 -*-
"""其他.v1 / 其他.v2 的缺失门；回收倍率 三个门的目标汇总。"""
import io
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re")
import yslib as Y

out = io.StringIO()
P = lambda *a: print(*a, file=out)

P("### 其他.v1 门（isMember @0x1006BA52 之后）")
P(Y.show_ys(0x1006BA57, maxbytes=0x14))
P("")
P("### 其他.v2 门（isMember @0x1006BAD1 之后）")
P(Y.show_ys(0x1006BAD6, maxbytes=0x14))
P("")
P("### 每件循环头的重置块全量（0x1006B294..0x1006B310），确认哪些槽被重置")
P(Y.show_ys(0x1006B294, maxbytes=0x7C))

with open(r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re\v8_out.txt", "w",
          encoding="utf-8") as fh:
    fh.write(out.getvalue())
print("ok")
