# -*- coding: utf-8 -*-
"""经验单价 [ebp-0x78] 的全部读写点；并确认 D3 五路判零操作数都是缩放前单价。"""
import io
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re")
import yslib as Y

out = io.StringIO()
P = lambda *a: print(*a, file=out)

FN_LO, FN_HI = 0x1006B020, 0x1006CF00          # sub_1006B020 全域
R8 = {0x45: "eax", 0x4D: "ecx", 0x55: "edx", 0x5D: "ebx",
      0x65: "esp", 0x6D: "ebp", 0x75: "esi", 0x7D: "edi"}
R32 = {0x85: "eax", 0x8D: "ecx", 0x95: "edx", 0x9D: "ebx",
       0xA5: "esp", 0xAD: "ebp", 0xB5: "esi", 0xBD: "edi"}


def scan(disp, label):
    """扫 [ebp+disp]（disp 为负）的 mov 读 / 写 / 立即数写，disp8 与 disp32 两种编码都扫。"""
    data = Y.ys()
    lo, hi = FN_LO - Y.YS_BASE, FN_HI - Y.YS_BASE
    d8 = (disp & 0xFF) if -128 <= disp <= 127 else None
    d32 = (disp & 0xFFFFFFFF).to_bytes(4, "little")
    writes, reads, imm = [], [], []
    for off in range(lo, hi):
        b0, b1 = data[off], data[off + 1]
        if d8 is not None and data[off + 2] == d8:
            if b0 == 0x89 and b1 in R8:
                writes.append((Y.YS_BASE + off, "mov [ebp%d], %s" % (disp, R8[b1]), 3))
            elif b0 == 0x8B and b1 in R8:
                reads.append((Y.YS_BASE + off, "mov %s, [ebp%d]" % (R8[b1], disp), 3))
            elif b0 == 0xC7 and b1 == 0x45:
                v = int.from_bytes(data[off + 3:off + 7], "little", signed=True)
                imm.append((Y.YS_BASE + off, "mov dword [ebp%d], %d" % (disp, v), 7))
            elif b0 in (0x83, 0x01, 0x03, 0x85, 0x39, 0x3B) and b1 in R8:
                reads.append((Y.YS_BASE + off, "op(%02X) %s [ebp%d]" % (b0, R8[b1], disp), 3))
        if data[off + 2:off + 6] == d32:
            if b0 == 0x89 and b1 in R32:
                writes.append((Y.YS_BASE + off, "mov [ebp%d], %s" % (disp, R32[b1]), 6))
            elif b0 == 0x8B and b1 in R32:
                reads.append((Y.YS_BASE + off, "mov %s, [ebp%d]" % (R32[b1], disp), 6))
            elif b0 == 0xC7 and b1 == 0x85:
                v = int.from_bytes(data[off + 6:off + 10], "little", signed=True)
                imm.append((Y.YS_BASE + off, "mov dword [ebp%d], %d" % (disp, v), 10))
            elif b0 in (0x01, 0x03, 0x83, 0x39, 0x3B) and b1 in R32:
                reads.append((Y.YS_BASE + off, "op(%02X) %s [ebp%d]" % (b0, R32[b1], disp), 6))
    P("")
    P("---- %s   ebp%d ----" % (label, disp))
    P("  立即数写入 %d 处:" % len(imm))
    for va, t, n in imm:
        P("     %08X  %-30s %s" % (va, Y.hexb(Y.bytes_ys(va, n)), t))
    P("  寄存器写入 %d 处:" % len(writes))
    for va, t, n in writes:
        P("     %08X  %-30s %s" % (va, Y.hexb(Y.bytes_ys(va, n)), t))
    P("  读取/运算 %d 处:" % len(reads))
    for va, t, n in reads:
        P("     %08X  %-30s %s" % (va, Y.hexb(Y.bytes_ys(va, n)), t))


P("=" * 78)
P("### 五路单价槽 在 sub_1006B020 (0x1006B020..0x1006CF00) 全域的读写普查")
P("=" * 78)
scan(-0x70, "元宝单价（0x1006BC18 起被复用为件数）")
scan(-0x90, "灵符单价")
scan(-0x94, "金币单价")
scan(-0x78, "★经验单价")
scan(-0x68, "其他值")

P("")
P("=" * 78)
P("### D3 判零点上文：0x1006BAF0..0x1006BB3D，确认 edi/esi 装的是哪两个槽")
P("=" * 78)
P(Y.show_ys(0x1006BAF4, maxbytes=0x4C))

P("")
P("=" * 78)
P("### 物品种类分支判零段（对齐到 0x1006CC68）")
P("=" * 78)
P(Y.show_ys(0x1006CC68, maxbytes=0x30))

P("")
P("=" * 78)
P("### 循环前唯一一次清零 [ebp-0x78]：0x1006B240..0x1006B2AC 对齐重扫")
P("=" * 78)
P(Y.show_ys(0x1006B24F, maxbytes=0x60))

with open(r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re\v4_out.txt", "w",
          encoding="utf-8") as fh:
    fh.write(out.getvalue())
print("ok")
