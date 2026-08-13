# -*- coding: utf-8 -*-
"""D5（未知字段被忽略）/ D6（总开关子键缺失 → 门失效）/ INVENTED（回收路径无背包模型门）。"""
import io
import sys

sys.path.insert(0, r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re")
import yslib as Y

out = io.StringIO()
P = lambda *a: print(*a, file=out)
data = Y.ys()

P("=" * 78)
P("### D6  总开关四个子键的解析：四个 je 汇聚到同一个跳过标签")
P("=" * 78)
for va in (0x1006B47C, 0x1006B4BD, 0x1006B4FE, 0x1006B53F):
    P("")
    P(Y.show_ys(va, maxbytes=0x2C))

P("")
P("=" * 78)
P("### D6  汇聚点 0x1006B633 之后：关闭值停在预置 -2，门失效")
P("=" * 78)
P(Y.show_ys(0x1006B633, maxbytes=0x30))

P("")
P("=" * 78)
P("### D5  规则解析全部是「按名查找」，没有任何成员枚举")
P("###     列出 sub_1006B020 里所有 push <.rdata 字符串> 的键名")
P("=" * 78)
seen = []
lo, hi = 0x1006B020 - Y.YS_BASE, 0x1006CF00 - Y.YS_BASE
for off in range(lo, hi):
    if data[off] != 0x68:
        continue
    v = int.from_bytes(data[off + 1:off + 5], "little")
    if not (0x1027D000 + 0x10000000 - 0x10000000 <= v < 0x1030E154):
        continue
    s = Y.cstr_ys(v, 64)
    if not s or s.startswith("'") or len(s) > 24:
        continue
    seen.append((Y.YS_BASE + off, v, s))
P("push 常量字符串 %d 处：" % len(seen))
uniq = {}
for va, v, s in seen:
    uniq.setdefault(s, []).append(va)
for s in sorted(uniq, key=lambda k: uniq[k][0]):
    P("   %-14s  VA %08X   push 处 %d 个：%s"
      % (repr(s), [v for _a, v, t in seen if t == s][0], len(uniq[s]),
         " ".join("%08X" % a for a in uniq[s][:6])))

P("")
P("### jsoncpp 成员枚举 API（Value::begin/getMemberNames）在本函数的调用数")
P("###   —— 若为 0，则未知字段根本不会被看到，D5 成立")
enum_calls = 0
for off in range(lo, hi):
    if data[off] == 0xE8:
        tgt = Y.YS_BASE + off + 5 + int.from_bytes(data[off + 1:off + 5], "little", signed=True)
        if tgt in (0x100E0BE0, 0x100E0DC0, 0x100E1210, 0x100E1240, 0x100E0ED0):
            enum_calls += 1
P("   已知按名访问族 (0x100E0BE0/0DC0/1210/1240/0ED0) 调用数 = %d" % enum_calls)

P("")
P("=" * 78)
P("### INVENTED  回收入口 0x1006CF10 全函数")
P("=" * 78)
P(Y.show_ys(0x1006CF10, maxbytes=0x70))

P("")
P("=" * 78)
P("### INVENTED  背包模型三个配置键的多编码扫描 + 回收函数内引用数")
P("=" * 78)
for name in ("无限背包_是否勾选", "无限背包_是否固定", "固定格子", "V变量控制格子"):
    P("")
    P("--- %s ---" % name)
    vas = []
    for enc, tag in (("gbk", "GBK"), ("ascii", "裸ASCII"), ("utf-16-le", "UTF-16LE")):
        try:
            pat = name.encode(enc)
        except UnicodeEncodeError:
            P("   %-8s : 该名含非 ASCII，此编码不适用" % tag)
            continue
        hits = Y.findall(data, pat)
        P("   %-8s : %d 命中  %s" % (tag, len(hits),
                                    " ".join("VA %08X" % (Y.YS_BASE + h) for h in hits[:8])))
        if enc == "gbk":
            vas = [Y.YS_BASE + h for h in hits]
    # 这些 VA 在回收函数 0x1006B020..0x1006CF80 内被引用多少次
    tot = 0
    for v in vas:
        le = v.to_bytes(4, "little")
        for off in Y.findall(data, le):
            va = Y.YS_BASE + off
            if 0x1006B020 <= va < 0x1006CF80:
                tot += 1
                P("      !! 回收函数内引用 @%08X" % va)
    P("   → 回收函数 0x1006B020..0x1006CF80 内引用数 = %d" % tot)

with open(r"D:\loym2\.claude\wt2\ys-recycle\tools\ys_recycle_re\v5_out.txt", "w",
          encoding="utf-8") as fh:
    fh.write(out.getvalue())
print("ok")
