# -*- coding: utf-8 -*-
"""复核 D1 删除段、D3 物品种类分支对齐、经验单价跨件泄漏、以及 0x1031BCxx 槽的初始化。"""
import io
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re")
import yslib as Y

out = io.StringIO()
P = lambda *a: print(*a, file=out)

P("### 0x1006BB57 jle 目标 = 0x1006BD2D ；直落臂 0x1006BB5D 起（删除段）")
P("raw 0x1006BB5D..0x1006BBD8:")
P("  " + Y.hexb(Y.bytes_ys(0x1006BB5D, 0x7B)))
P("")
P(Y.show_ys(0x1006BB5D, maxbytes=0x7B))

P("")
P("=" * 78)
P("### D3 物品种类分支：从 0x1006CC40 对齐重扫")
P("=" * 78)
P(Y.show_ys(0x1006CC40, maxbytes=0x58))

P("")
P("=" * 78)
P("### 经验单价 [ebp-0x78]：循环前唯一清零点 0x1006B24F 附近")
P("=" * 78)
P(Y.show_ys(0x1006B240, maxbytes=0x60))

P("")
P("=" * 78)
P("### 每件循环头的重置块 0x1006B29C..0x1006B300（元宝/灵符/金币 有，经验 无）")
P("=" * 78)
P(Y.show_ys(0x1006B29C, maxbytes=0x70))

P("")
P("=" * 78)
P("### 类型缺 经验 键时沿用上一件：0x1006B950..0x1006B990")
P("=" * 78)
P(Y.show_ys(0x1006B950, maxbytes=0x40))

P("")
P("=" * 78)
P("### 乘过倍率的结果写回 [ebp-0x78]：0x1006BC80..0x1006BCB8")
P("=" * 78)
P(Y.show_ys(0x1006BC80, maxbytes=0x38))

P("")
P("=" * 78)
P("### 0x1031BCxx 槽的运行期初始化：0x1006B0C0..0x1006B160")
P("=" * 78)
P(Y.show_ys(0x1006B0C0, maxbytes=0xA0))

P("")
P("### [0x10310D0C] 静态值（FCount<=1 的清包分支目标）")
P("  [10310D0C] = %08X  bytes %s"
  % (int.from_bytes(Y.bytes_ys(0x10310D0C, 4), "little"), Y.hexb(Y.bytes_ys(0x10310D0C, 4))))
P("  [10310D08] = %08X  bytes %s   -> %r"
  % (int.from_bytes(Y.bytes_ys(0x10310D08, 4), "little"), Y.hexb(Y.bytes_ys(0x10310D08, 4)),
     Y.cstr_ys(int.from_bytes(Y.bytes_ys(0x10310D08, 4), "little"), 32)
     if 0x10000000 < int.from_bytes(Y.bytes_ys(0x10310D08, 4), "little") < 0x10400000 else "-"))
P("  [10310CFC] = %08X  bytes %s"
  % (int.from_bytes(Y.bytes_ys(0x10310CFC, 4), "little"), Y.hexb(Y.bytes_ys(0x10310CFC, 4))))

with open(r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re\v2_out.txt", "w",
          encoding="utf-8") as fh:
    fh.write(out.getvalue())
print("ok")
