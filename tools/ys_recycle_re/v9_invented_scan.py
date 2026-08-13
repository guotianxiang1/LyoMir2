# -*- coding: utf-8 -*-
"""INVENTED 判定证据：回收路径上不存在任何「背包模型」门。

按 REPLICATION_RULES §3 的要求做多编码扫描（GBK + 裸 ASCII + UTF-16LE），
并补一层本例真正需要的证据：这些字符串的 VA 在回收函数体内被引用多少次。
（这些键在插件里确实存在——它们属于背包容量 sub_1007E370——所以「全镜像 0 命中」
 不是这条的正确判据；正确判据是「回收路径内 0 引用」。两层都给。）
"""
import io
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re")
import yslib as Y

out = io.StringIO()
P = lambda *a: print(*a, file=out)
data = Y.ys()

REC_LO, REC_HI = 0x1006B020, 0x1006CF80          # sub_1006B020 + 入口 sub_1006CF10
BAG_LO, BAG_HI = 0x1007E340, 0x1007E460          # 背包容量 sub_1007E370 邻域（对照组）

KEYS = ["无限背包_是否勾选", "无限背包_是否固定", "固定格子", "V变量控制格子",
        "无限背包_额外格子", "无限背包_变量v1", "无限背包_变量v2"]

P("=" * 78)
P("### 第一层：多编码字节扫描（GBK / 裸 ASCII / UTF-16LE）")
P("=" * 78)
key_vas = {}
for name in KEYS:
    P("")
    P("--- %s ---" % name)
    for enc, tag in (("gbk", "GBK      "), ("ascii", "裸ASCII  "), ("utf-16-le", "UTF-16LE ")):
        try:
            pat = name.encode(enc)
        except UnicodeEncodeError:
            P("   %s: 名含非 ASCII 字符，该编码不适用" % tag)
            continue
        hits = [Y.YS_BASE + h for h in Y.findall(data, pat)]
        P("   %s: %d 命中   %s" % (tag, len(hits), " ".join("%08X" % v for v in hits)))
        if enc == "gbk":
            key_vas[name] = hits

P("")
P("=" * 78)
P("### 第二层：这些 VA 在「回收路径」与「背包容量路径」各被引用几次")
P("###   回收路径 = 0x%08X..0x%08X（sub_1006B020 主体 + 入口 sub_1006CF10）"
  % (REC_LO, REC_HI))
P("###   对照组   = 0x%08X..0x%08X（背包容量 sub_1007E370）" % (BAG_LO, BAG_HI))
P("=" * 78)
grand_rec = 0
for name, vas in key_vas.items():
    rec, bag, other = [], [], 0
    for v in vas:
        for off in Y.findall(data, v.to_bytes(4, "little")):
            va = Y.YS_BASE + off
            if REC_LO <= va < REC_HI:
                rec.append(va)
            elif BAG_LO <= va < BAG_HI:
                bag.append(va)
            else:
                other += 1
    grand_rec += len(rec)
    P("   %-18s 回收路径 %d 次 %-24s 背包容量 %d 次 %-22s 其他 %d 次"
      % (name, len(rec), " ".join("%08X" % a for a in rec) or "-",
         len(bag), " ".join("%08X" % a for a in bag) or "-", other))
P("")
P("   >>> 回收路径引用总计 = %d" % grand_rec)

P("")
P("=" * 78)
P("### 第三层：回收入口 sub_1006CF10 的全部 call 目标（0 个通向背包配置）")
P("=" * 78)
for off in range(0x1006CF10 - Y.YS_BASE, 0x1006CF80 - Y.YS_BASE):
    if data[off] == 0xE8:
        tgt = Y.YS_BASE + off + 5 + int.from_bytes(data[off + 1:off + 5], "little", signed=True)
        P("   %08X  E8 -> %08X" % (Y.YS_BASE + off, tgt))
    elif data[off] == 0xFF and data[off + 1] == 0x15:
        P("   %08X  FF 15 -> [%08X]"
          % (Y.YS_BASE + off, int.from_bytes(data[off + 2:off + 6], "little")))
P("   入口唯一的门是 0x1006CF16 80 3D C5 B8 31 10 00 cmp byte [0x1031B8C5],0")

with open(r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re\v9_out.txt", "w",
          encoding="utf-8") as fh:
    fh.write(out.getvalue())
print("ok")
