"""Three-path census of yanshen S-bank access.

Path 1: wrapper 0x10056040 / SetS wrappers (constant args).
Path 2: trampoline templates that dereference [player+0x804] then a naked bank offset.
Path 3: Themida-virtualized functions (push imm / jmp into the zero page).

Does not invent S(group,index) from a naked offset. A flat key is only named
when a trampoline compares a loaded dword against an immediate of the form
group*1000+index with group>=1 and index>=1 (formula verified at 0x6E42CC).
"""
from __future__ import annotations

import json
import os
import struct
import sys
from collections import defaultdict

from capstone import CS_ARCH_X86, CS_MODE_32, Cs
from capstone.x86 import (
    X86_OP_IMM, X86_OP_MEM, X86_OP_REG,
    X86_REG_EAX, X86_REG_EBP, X86_REG_EBX, X86_REG_ECX,
    X86_REG_EDI, X86_REG_EDX, X86_REG_ESI,
)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.join(HERE, "..", "docs", "_ys_svars")
os.makedirs(OUT_DIR, exist_ok=True)

YS_DUMP = r"D:\loym2\staging\yanshen208_strparam_runtime_dump_20260719\yanshen2_0_8_dll.memory.bin"
M2_DUMP = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
YS_BASE = 0x10000000
M2_BASE = 0x400000
TEXT_RVA, TEXT_SIZE = 0x1000, 0x27BC40
RDATA_LO, RDATA_HI = 0x1027D000, 0x1030E154
# File offsets 0x400000..0x1400000 are the Themida VM gap (100% zero in this dump).
# VAs are YS_BASE-relative, so the gap is 0x10400000..0x11400000.
VM_LO, VM_HI = YS_BASE + 0x400000, YS_BASE + 0x1400000

GETS_W = 0x10056040
GETV_W = 0x10065F00
SETS_W = 0x10065F40
SETS_G1 = 0x100CE200
BUILDERS = (0x10032CC0, 0x10032FD0, 0x10032B10)
LABEL = 0x100F018C

ys = open(YS_DUMP, "rb").read()
m2 = open(M2_DUMP, "rb").read()


def md():
    c = Cs(CS_ARCH_X86, CS_MODE_32)
    c.detail = True
    return c


def ysat(va, n):
    o = va - YS_BASE
    return ys[o:o + n]


def ysdw(va):
    return struct.unpack_from("<I", ys, va - YS_BASE)[0]


def m2at(va, n):
    o = va - M2_BASE
    return m2[o:o + n]


def hexb(b):
    return " ".join(f"{x:02X}" for x in b)


def gbk_at(va, n=96):
    o = va - YS_BASE
    if o <= 0 or o >= len(ys):
        return None
    if ys[o - 1] != 0:
        return None
    e = ys.find(b"\x00", o, o + n)
    s = ys[o:e if e >= 0 else o + n]
    try:
        t = s.decode("gbk")
        return t if len(t) >= 2 else None
    except Exception:
        return None


def dis(va, n):
    return list(md().disasm(ysat(va, n), va))


def call_sites(target, lo=TEXT_RVA, hi=TEXT_RVA + TEXT_SIZE):
    hits = []
    i = lo
    while i < hi - 5:
        if ys[i] == 0xE8:
            rel = struct.unpack_from("<i", ys, i + 1)[0]
            if YS_BASE + i + 5 + rel == target:
                hits.append(YS_BASE + i)
        i += 1
    return hits


def func_start(site, maxback=0x8000):
    o = site - YS_BASE
    for i in range(o - 1, max(o - maxback, TEXT_RVA + 1), -1):
        if ys[i] == 0xCC and ys[i - 1] == 0xCC and ys[i + 1] != 0xCC:
            return YS_BASE + i + 1
    return None


def fmt_ins(ins):
    return f"{ins.address:#010x}  {hexb(ins.bytes):<22} {ins.mnemonic} {ins.op_str}"


# ---------------------------------------------------------------------------
# Path 1: wrapper argument recovery
# ---------------------------------------------------------------------------

def recover_wrapper_args(site, window=0x80):
    """Linear-disassemble from the enclosing function start (int3 boundary)
    so `push imm` / `mov edx, imm` land on the right offsets. Fall back to
    the longest pre-call window that decodes onto the call itself."""
    fs = func_start(site)
    best = None
    if fs and fs < site:
        seq = list(md().disasm(ysat(fs, site - fs + 8), fs))
        if any(x.address == site for x in seq):
            best = [x for x in seq if x.address <= site]
    if not best:
        # longest window that still lands on the call
        for back in range(window, 7, -1):
            seq = list(md().disasm(ysat(site - back, back + 8), site - back))
            if any(x.address == site for x in seq):
                best = [x for x in seq if x.address <= site]
                break
    if not best:
        return {"group": None, "index": None, "group_how": "unaligned",
                "index_how": "unaligned", "window": []}

    reg_imm = {}
    ebp_imm = {}
    group = index = None
    group_how = index_how = None
    window_fmt = []

    def set_reg(reg, val, how):
        if val is None:
            reg_imm.pop(reg, None)
        else:
            reg_imm[reg] = (val & 0xFFFFFFFF, how)

    for ins in best:
        window_fmt.append(fmt_ins(ins))
        if ins.address == site:
            break
        m, ops = ins.mnemonic, ins.operands
        if m == "mov" and len(ops) == 2:
            d, s = ops
            if d.type == X86_OP_REG and s.type == X86_OP_IMM:
                set_reg(d.reg, s.imm, f"imm:{s.imm:#x}")
                if d.reg == X86_REG_EDX:
                    group, group_how = s.imm & 0xFFFFFFFF, f"mov edx, {s.imm:#x}"
            elif d.type == X86_OP_REG and s.type == X86_OP_REG:
                src = reg_imm.get(s.reg)
                if src:
                    set_reg(d.reg, src[0], src[1])
                    if d.reg == X86_REG_EDX:
                        group, group_how = src[0], f"mov edx, {ins.reg_name(s.reg)} ({src[1]})"
                else:
                    reg_imm.pop(d.reg, None)
                    if d.reg == X86_REG_EDX:
                        group, group_how = None, f"mov edx, {ins.reg_name(s.reg)} (unknown)"
            elif d.type == X86_OP_MEM and d.mem.base == X86_REG_EBP and d.mem.index == 0 \
                    and s.type == X86_OP_IMM:
                ebp_imm[d.mem.disp] = s.imm & 0xFFFFFFFF
            elif d.type == X86_OP_REG:
                reg_imm.pop(d.reg, None)
        elif m == "lea" and len(ops) == 2 and ops[0].type == X86_OP_REG \
                and ops[1].type == X86_OP_MEM:
            mem = ops[1].mem
            if ops[0].reg == X86_REG_EDX:
                if mem.base and mem.index == 0:
                    src = reg_imm.get(mem.base)
                    if src:
                        group = (src[0] + mem.disp) & 0xFFFFFFFF
                        group_how = f"lea edx, [{ins.reg_name(mem.base)}+{mem.disp:#x}]"
                    else:
                        group, group_how = None, (
                            f"lea edx, [{ins.reg_name(mem.base)}+{mem.disp:#x}] (indexed)"
                        )
                else:
                    group, group_how = None, f"lea edx, {ins.op_str.split(',',1)[-1].strip()}"
        elif m == "push":
            op = ops[0]
            if op.type == X86_OP_IMM:
                index, index_how = op.imm & 0xFFFFFFFF, f"push {op.imm:#x}"
            elif op.type == X86_OP_REG:
                src = reg_imm.get(op.reg)
                if src:
                    index, index_how = src[0], f"push {ins.reg_name(op.reg)} ({src[1]})"
                else:
                    index, index_how = None, f"push {ins.reg_name(op.reg)} (runtime)"
            elif op.type == X86_OP_MEM and op.mem.base == X86_REG_EBP and op.mem.index == 0:
                disp = op.mem.disp
                if disp in ebp_imm:
                    index, index_how = ebp_imm[disp], f"push [ebp{disp:+#x}] = {ebp_imm[disp]:#x}"
                else:
                    index, index_how = None, f"push [ebp{disp:+#x}] (runtime slot)"
            else:
                index, index_how = None, f"push {ins.op_str} (runtime)"
        elif m == "call" and ins.address != site:
            # earlier call consumed its stack args
            index = index_how = None

    # edx may have been set after the push; last mov edx wins
    return {
        "group": group, "index": index,
        "group_how": group_how, "index_how": index_how,
        "window": window_fmt[-14:],
    }


def path1():
    out = {}
    for name, va in (("GetS", GETS_W), ("SetS", SETS_W), ("SetS_g1", SETS_G1)):
        sites = call_sites(va)
        rows = []
        for s in sites:
            rec = recover_wrapper_args(s)
            rec["site"] = s
            rec["func"] = func_start(s)
            rec["bytes"] = hexb(ysat(s, 5))
            if name == "SetS_g1" and rec["group"] is None:
                rec["group"] = 1
                rec["group_how"] = "hardcoded in 0x100CE200 (mov [ebp-4], 1)"
            rows.append(rec)
        out[name] = {"wrapper": va, "n": len(sites), "rows": rows}
    return out


# ---------------------------------------------------------------------------
# Path 2: trampoline templates
# ---------------------------------------------------------------------------

def key_at(v):
    if not (0x102A0000 <= v < 0x1031D000):
        return None
    return gbk_at(v)


def recover_builder_sites():
    sites = []
    i = TEXT_RVA
    while i < TEXT_RVA + TEXT_SIZE - 5:
        if ys[i] == 0xE8:
            t = YS_BASE + i + 5 + struct.unpack_from("<i", ys, i + 1)[0]
            if t in BUILDERS:
                sites.append((YS_BASE + i, t))
        i += 1

    byfunc = defaultdict(list)
    for s, t in sites:
        byfunc[func_start(s)].append((s, t))

    results = []
    cs = md()
    for fs, ss in sorted((k, v) for k, v in byfunc.items() if k):
        span = max(s for s, _ in ss) - fs + 0x400
        ins = list(cs.disasm(ysat(fs, span), fs))
        mem = {}
        reg_lea, reg_imm = {}, {}
        pend = []
        openr = []
        for x in ins:
            m, ops = x.mnemonic, x.operands
            if m == "mov" and len(ops) == 2 and ops[0].type == X86_OP_MEM \
                    and ops[0].mem.base == X86_REG_EBP and ops[0].mem.index == 0 \
                    and ops[1].type == X86_OP_IMM:
                n = 1 if x.bytes[0] == 0xC6 else (2 if x.bytes[0] == 0x66 else 4)
                raw = (ops[1].imm & ((1 << 8 * n) - 1)).to_bytes(n, "little")
                for k, c in enumerate(raw):
                    mem[ops[0].mem.disp + k] = c
            elif m in ("movups", "movaps", "movdqu", "movdqa") and len(ops) == 2:
                d, s2 = ops
                if d.type == X86_OP_MEM and d.mem.base == X86_REG_EBP and s2.type == X86_OP_REG:
                    src = reg_imm.get(s2.reg)
                    if isinstance(src, tuple) and src[0] == "xmm":
                        for k, c in enumerate(src[1]):
                            mem[d.mem.disp + k] = c
                elif d.type == X86_OP_REG and s2.type == X86_OP_MEM and s2.mem.base == 0:
                    p = s2.mem.disp & 0xFFFFFFFF
                    reg_imm[d.reg] = ("xmm", ysat(p, 16))
            elif m == "mov" and len(ops) == 2 and ops[0].type == X86_OP_REG:
                if ops[1].type == X86_OP_IMM:
                    reg_imm[ops[0].reg] = ops[1].imm & 0xFFFFFFFF
                    reg_lea.pop(ops[0].reg, None)
                else:
                    reg_imm.pop(ops[0].reg, None)
                    reg_lea.pop(ops[0].reg, None)
            elif m == "lea" and len(ops) == 2 and ops[1].type == X86_OP_MEM \
                    and ops[1].mem.base == X86_REG_EBP and ops[0].type == X86_OP_REG:
                reg_lea[ops[0].reg] = ops[1].mem.disp
                reg_imm.pop(ops[0].reg, None)
            if m == "push":
                op = ops[0]
                if op.type == X86_OP_IMM:
                    pend.append(("imm", op.imm & 0xFFFFFFFF))
                elif op.type == X86_OP_REG:
                    pend.append(("ebp", reg_lea[op.reg]) if op.reg in reg_lea
                                else ("reg", reg_imm.get(op.reg)))
                else:
                    pend.append(("mem", None))
            if m == "call" and ops and ops[0].type == X86_OP_IMM:
                t = ops[0].imm & 0xFFFFFFFF
                if t in BUILDERS and len(pend) >= 6:
                    cnt, code, resume, tgt2, tgt1, outp = pend[-6:]
                    openr.append({"site": x.address, "fn": fs, "builder": t,
                                  "count": cnt[1], "code": code, "resume": resume[1],
                                  "target": tgt1[1], "mem": dict(mem), "label": None})
                elif t == LABEL:
                    lab = None
                    for k, v in reversed(pend):
                        if k == "imm" and isinstance(v, int):
                            lab = key_at(v)
                            if lab:
                                break
                    for r in openr:
                        r["label"] = lab
                    results.extend(openr)
                    openr = []
                pend = []
        results.extend(openr)

    # fill dword arrays from stack mem or .rdata rep movsd
    for r in results:
        r["arr"] = None
        if r["code"][0] == "ebp" and r["count"]:
            d0 = r["code"][1]
            arr, ok = [], True
            for k in range(r["count"]):
                w = [r["mem"].get(d0 + 4 * k + j) for j in range(4)]
                if any(v is None for v in w):
                    ok = False
                    break
                arr.append(int.from_bytes(bytes(w), "little"))
            if ok:
                r["arr"] = arr
        if r["arr"] is None:
            src, cnt = replay_repmovsd(r["site"])
            if src and cnt:
                raw = ysat(src, 4 * cnt)
                r["arr"] = list(struct.unpack_from(f"<{cnt}I", raw))
                r["template"] = src
    return sites, results


def replay_repmovsd(site):
    for back in (0x80, 0x140, 0x240):
        s0 = site - back
        for adj in range(16):
            ins = list(md().disasm(ysat(s0 + adj, back - adj), s0 + adj))
            if not ins or ins[-1].address + ins[-1].size != site:
                continue
            src = cnt = None
            for x in ins:
                if x.mnemonic == "mov" and x.operands \
                        and x.operands[0].type == X86_OP_REG \
                        and x.operands[1].type == X86_OP_IMM:
                    nm = x.reg_name(x.operands[0].reg)
                    if nm == "esi":
                        src = x.operands[1].imm & 0xFFFFFFFF
                    elif nm == "ecx":
                        cnt = x.operands[1].imm & 0xFFFFFFFF
                if "movs" in x.mnemonic and "rep" in x.mnemonic:
                    if src and cnt:
                        return src, cnt
            break
    return None, None


def to_bytes(arr):
    o, rel = bytearray(), []
    k = 0
    while k < len(arr):
        v = arr[k]
        if v in (0xE8, 0xE9) and k + 1 < len(arr) and arr[k + 1] > 0xFF:
            o.append(v)
            rel.append((len(o), arr[k + 1]))
            o += b"\x00\x00\x00\x00"
            k += 2
            continue
        o.append(v & 0xFF)
        k += 1
    return bytes(o), rel


def flat_key(imm):
    """Return (group, index) iff imm matches the verified engine formula
    with both operands strictly positive and index < 1000."""
    if imm is None or imm < 1001 or imm > 99099:
        return None
    g, i = divmod(imm, 1000)
    if g >= 1 and 1 <= i <= 999:
        return g, i
    return None


def extract_804_chains(code, rel, label, site, builder, target):
    """Disassemble trampoline bytes and collect [reg+0x804] then naked offsets."""
    fake = 0x20000000
    fix = bytearray(code)
    for off_, tv in rel:
        struct.pack_into("<i", fix, off_, tv - (fake + off_ + 4))
    ins = list(md().disasm(bytes(fix), fake))
    bank_regs = {}  # capstone reg id -> trampoline offset of the load
    hits = []
    for i, x in enumerate(ins):
        tramp_off = x.address - fake
        for op in x.operands:
            if op.type != X86_OP_MEM:
                continue
            disp = op.mem.disp & 0xFFFFFFFF
            if disp != 0x804:
                continue
            # pointer-range check vs load
            kind = "load"
            if x.mnemonic in ("cmp", "test"):
                kind = "ptr_check"
            rec = {
                "label": label, "site": site, "builder": builder,
                "host_target": target if isinstance(target, int) else None,
                "tramp_off": tramp_off, "kind": kind,
                "bytes": hexb(x.bytes), "asm": f"{x.mnemonic} {x.op_str}",
                "naked": [],
            }
            if kind == "load" and x.mnemonic == "mov" and x.operands[0].type == X86_OP_REG:
                bank_regs[x.operands[0].reg] = tramp_off
                # look ahead for [bank+naked]
                for y in ins[i + 1:i + 12]:
                    if y.mnemonic in ("push", "pop", "pushal", "popal"):
                        # still in the same bank live range often
                        pass
                    for op2 in y.operands:
                        if op2.type != X86_OP_MEM:
                            continue
                        if op2.mem.base != x.operands[0].reg:
                            continue
                        nd = op2.mem.disp & 0xFFFFFFFF
                        if nd == 0x804 or nd > 0x2000:
                            continue
                        # skip scaled-index forms
                        if op2.mem.index != 0:
                            continue
                        cmp_imm = None
                        key = None
                        # next few insns: cmp dest-reg, imm
                        if y.mnemonic == "mov" and y.operands[0].type == X86_OP_REG:
                            dst = y.operands[0].reg
                            for z in ins[ins.index(y) + 1:ins.index(y) + 4]:
                                if z.mnemonic in ("cmp", "test") and len(z.operands) == 2 \
                                        and z.operands[0].type == X86_OP_REG \
                                        and z.operands[0].reg == dst \
                                        and z.operands[1].type == X86_OP_IMM:
                                    cmp_imm = z.operands[1].imm & 0xFFFFFFFF
                                    key = flat_key(cmp_imm)
                                    break
                        rec["naked"].append({
                            "off": nd,
                            "tramp_off": y.address - fake,
                            "bytes": hexb(y.bytes),
                            "asm": f"{y.mnemonic} {y.op_str}",
                            "cmp_imm": cmp_imm,
                            "key": list(key) if key else None,
                            "rw": "w" if y.mnemonic in ("mov", "add", "sub", "or", "and", "xor")
                            and y.operands[0].type == X86_OP_MEM else "r",
                        })
            hits.append(rec)
    return hits, ins


def path2():
    raw_sites, results = recover_builder_sites()
    by_builder = defaultdict(int)
    for s, t in raw_sites:
        by_builder[t] += 1
    decoded, failed = [], []
    all_hits = []
    for r in results:
        if not r.get("arr"):
            failed.append({"site": r["site"], "label": r.get("label"),
                           "builder": r["builder"], "count": r.get("count")})
            continue
        code, rel = to_bytes(r["arr"])
        hits, ins = extract_804_chains(
            code, rel, r.get("label"), r["site"], r["builder"], r.get("target"))
        decoded.append({
            "site": r["site"], "label": r.get("label"), "builder": r["builder"],
            "target": r["target"] if isinstance(r["target"], int) else None,
            "n804": len(hits), "code_len": len(code),
        })
        all_hits.extend(hits)
    return {
        "raw_builder_calls": len(raw_sites),
        "by_builder": {hex(k): v for k, v in by_builder.items()},
        "recovered_sites": len(results),
        "decoded": len(decoded),
        "failed": failed,
        "hits": all_hits,
        "decoded_meta": decoded,
    }


# ---------------------------------------------------------------------------
# Path 3: Themida VM (push imm32 / jmp|call into 0x400000..0x1400000)
# ---------------------------------------------------------------------------

def path3():
    vm_entries = []
    i = TEXT_RVA
    end = TEXT_RVA + TEXT_SIZE - 10
    while i < end:
        # Themida shape: push <.rdata blob> / jmp <VM zero page>
        if ys[i] == 0x68 and ys[i + 5] == 0xE9:
            imm = struct.unpack_from("<I", ys, i + 1)[0]
            rel = struct.unpack_from("<i", ys, i + 6)[0]
            va = YS_BASE + i
            tgt = va + 10 + rel
            if VM_LO <= tgt < VM_HI and RDATA_LO <= imm < RDATA_HI:
                vm_entries.append({
                    "va": va, "bytes": hexb(ys[i:i + 10]),
                    "push": imm, "jmp": tgt,
                    "func": func_start(va),
                    "asm": f"push {imm:#010x} / jmp {tgt:#010x}",
                })
                i += 10
                continue
        i += 1

    # zero-page confirmation (file offsets, not VAs)
    gap = ys[0x400000:min(0x1400000, len(ys))]
    zero_ratio = gap.count(0) / len(gap) if gap else 1.0

    # named ones we care about
    named = []
    for e in vm_entries:
        if e["va"] in (0x100CEB47, 0x100CEB4C) or (e["func"] == 0x100CEB40):
            named.append({**e, "note": "SetS detour body 0x100CEB40"})
    return {
        "n": len(vm_entries),
        "entries": vm_entries,
        "gap_zero_ratio": zero_ratio,
        "gap_len": len(gap),
        "named": named,
    }


# ---------------------------------------------------------------------------
# Plugin .text: instruction-level [reg+0x804] that is NOT a trampoline template
# ---------------------------------------------------------------------------

def path_text_804():
    hits = []
    cs = md()
    i = TEXT_RVA
    end = TEXT_RVA + TEXT_SIZE - 8
    while i < end:
        b0 = ys[i]
        # 8B / 89 / 3B / 81 / 83 with disp32 04 08 00 00
        if b0 in (0x8B, 0x89, 0x3B, 0x8D, 0x03, 0x2B) and i + 6 < end:
            # ModRM: mod=10 (disp32), r/m != 100 (SIB) typically
            # pattern: OP ModRM 04 08 00 00
            if ys[i + 2:i + 6] == b"\x04\x08\x00\x00":
                seq = list(cs.disasm(ys[i:i + 8], YS_BASE + i))
                if seq:
                    x = seq[0]
                    ok = False
                    for op in x.operands:
                        if op.type == X86_OP_MEM and (op.mem.disp & 0xFFFFFFFF) == 0x804:
                            ok = True
                    if ok:
                        hits.append({
                            "va": x.address, "bytes": hexb(x.bytes),
                            "asm": f"{x.mnemonic} {x.op_str}",
                            "func": func_start(x.address),
                        })
                    i += max(len(x.bytes), 1)
                    continue
        if b0 == 0x81 and i + 10 < end and ys[i + 2:i + 6] == b"\x04\x08\x00\x00":
            seq = list(cs.disasm(ys[i:i + 10], YS_BASE + i))
            if seq:
                x = seq[0]
                ok = any(op.type == X86_OP_MEM and (op.mem.disp & 0xFFFFFFFF) == 0x804
                         for op in x.operands)
                if ok:
                    hits.append({
                        "va": x.address, "bytes": hexb(x.bytes),
                        "asm": f"{x.mnemonic} {x.op_str}",
                        "func": func_start(x.address),
                    })
                i += max(len(x.bytes), 1)
                continue
        i += 1
    return hits


# ---------------------------------------------------------------------------
# Engine GetS / SetS evidence
# ---------------------------------------------------------------------------

def engine_gets_sets():
    rows = []
    for va, n, title in (
        (0x6DF1B4, 48, "GetS"),
        (0x6DF240, 56, "SetS"),
        (0x6E42CC, 12, "flat_key imul 1000"),
        (0x6E4270, 80, "S-bank binary search"),
    ):
        ins = list(md().disasm(m2at(va, n + 8), va))
        rows.append({
            "title": title, "va": va,
            "bytes": hexb(m2at(va, min(n, 24))),
            "ins": [fmt_ins(x).replace(f"{x.address:#010x}", f"{x.address:#08x}")
                    for x in ins[:20]],
        })
    return rows


def wrapper_body():
    return [fmt_ins(x) for x in dis(GETS_W, 64)[:20]]


# ---------------------------------------------------------------------------
# report helpers
# ---------------------------------------------------------------------------

def summarize_path1(p1):
    gets = p1["GetS"]["rows"]
    const = []
    indexed = []
    for r in gets:
        g, i = r["group"], r["index"]
        if g is not None and i is not None:
            const.append((g, i, r["site"], r["func"], r["bytes"],
                          r["group_how"], r["index_how"]))
        else:
            indexed.append(r)
    by = defaultdict(list)
    for g, i, *rest in const:
        by[(g, i)].append(rest[0])
    return const, indexed, by


def main():
    print("path1...", flush=True)
    p1 = path1()
    print(f"  GetS {p1['GetS']['n']} SetS {p1['SetS']['n']} SetS_g1 {p1['SetS_g1']['n']}",
          flush=True)

    print("path2...", flush=True)
    p2 = path2()
    print(f"  builder calls {p2['raw_builder_calls']} recovered {p2['recovered_sites']} "
          f"decoded {p2['decoded']} failed {len(p2['failed'])} 804-hits {len(p2['hits'])}",
          flush=True)

    print("path3...", flush=True)
    p3 = path3()
    print(f"  VM entries {p3['n']} gap zero {p3['gap_zero_ratio']:.4f}", flush=True)

    print("text 0x804...", flush=True)
    t804 = path_text_804()
    print(f"  plugin .text [reg+0x804] insns: {len(t804)}", flush=True)

    eng = engine_gets_sets()
    wbody = wrapper_body()

    blob = {
        "path1": p1, "path2": p2, "path3": p3,
        "text_804": t804, "engine": eng, "wrapper_body": wbody,
    }
    # drop huge windows from json for the compact dump; keep them in a side file
    compact = json.loads(json.dumps(blob))
    for name in ("GetS", "SetS", "SetS_g1"):
        for r in compact["path1"][name]["rows"]:
            r.pop("window", None)
    # path3 entries can be long; keep all, they're the boundary list
    path = os.path.join(OUT_DIR, "census.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(compact, f, ensure_ascii=False, indent=1)
    print("wrote", path)

    # human summary
    const, indexed, by = summarize_path1(p1)
    lines = []
    lines.append(f"GetS sites: {p1['GetS']['n']}")
    lines.append(f"const (g,i): {len(by)} unique / {len(const)} sites")
    for (g, i), sites in sorted(by.items()):
        lines.append(f"  S({g},{i})  n={len(sites)}  e.g. {sites[0]:#010x}")
    lines.append(f"non-const GetS: {len(indexed)}")
    for r in indexed:
        lines.append(f"  {r['site']:#010x} func {r['func'] and hex(r['func'])} "
                     f"g={r['group_how']} i={r['index_how']}")
    lines.append(f"SetS const:")
    for r in p1["SetS"]["rows"]:
        lines.append(f"  {r['site']:#010x} g={r['group']} i={r['index']} "
                     f"({r['group_how']}; {r['index_how']})")
    lines.append(f"SetS_g1:")
    for r in p1["SetS_g1"]["rows"]:
        lines.append(f"  {r['site']:#010x} g={r['group']} i={r['index']} "
                     f"({r['group_how']}; {r['index_how']})")

    naked_set = defaultdict(list)
    key_set = defaultdict(list)
    for h in p2["hits"]:
        for n in h["naked"]:
            naked_set[n["off"]].append((h["label"], h["site"], n["tramp_off"], n["asm"], n["rw"]))
            if n["key"]:
                key_set[tuple(n["key"])].append(
                    (h["label"], h["site"], n["off"], n["cmp_imm"], n["asm"]))
    lines.append(f"\ntrampoline [player+0x804] load sites: {len(p2['hits'])}")
    lines.append(f"unique naked offsets: {len(naked_set)}")
    for off, xs in sorted(naked_set.items()):
        labs = sorted({x[0] or '?' for x in xs})
        lines.append(f"  bank+{off:#x}  n={len(xs)}  labels={labs[:6]}")
    lines.append(f"key-checked (formula): {len(key_set)}")
    for k, xs in sorted(key_set.items()):
        lines.append(f"  S{k}  n={len(xs)}  e.g. {xs[0][0]} site {xs[0][1]:#010x} off {xs[0][2]:#x} cmp {xs[0][3]:#x}")

    lines.append(f"\nVM entries: {p3['n']}")
    lines.append(f"plugin .text 0x804 insns: {len(t804)}")
    for h in t804:
        lines.append(f"  {h['va']:#010x}  {h['bytes']}  {h['asm']}")

    sum_path = os.path.join(OUT_DIR, "census_summary.txt")
    with open(sum_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("wrote", sum_path)
    print("\n".join(lines[:80]))

    # extra: plugin .text 0x804 lookahead + init function + failed trampolines
    extra = []
    extra.append("=== plugin .text [reg+0x804] + 8 ins lookahead ===")
    for h in t804:
        extra.append(f"\n{h['va']:#010x} func {h['func'] and hex(h['func'])}")
        for ins in dis(h["va"], 48)[:10]:
            extra.append("  " + fmt_ins(ins))
    extra.append("\n=== init 0x100CE4EA region (try 8 alignments) ===")
    for adj in range(8):
        start = 0x100CE4EA - adj
        extra.append(f"\n-- start {start:#x} --")
        n = 0
        landed = False
        for ins in dis(start, 0xC0):
            extra.append("  " + fmt_ins(ins))
            if ins.address in (0x100CE4F3, 0x100CE522, 0x100CE533, 0x100CE549, 0x100CE557, 0x100CE57C):
                landed = True
            n += 1
            if n >= 28:
                break
        extra.append(f"  landed_on_known_calls={landed}")
    extra.append("\n=== GetS wrapper 0x10056040 ===")
    extra.append("\n".join("  " + x for x in wbody))
    extra.append("\n=== engine GetS/SetS ===")
    for row in eng:
        extra.append(f"\n# {row['title']} @{row['va']:#x} bytes {row['bytes']}")
        extra.extend("  " + x for x in row["ins"][:16])
    extra.append("\n=== failed trampolines ===")
    for f in p2["failed"]:
        extra.append(f"  site {f['site']:#010x} label={f['label']} builder={f['builder']:#x} count={f['count']}")
    extra.append("\n=== 0x100dba50 longer dump ===")
    for ins in dis(0x100dba50, 0x120)[:40]:
        extra.append("  " + fmt_ins(ins))
    extra.append(f"\n=== VM unique funcs: {len({e['func'] for e in p3['entries']})} / {p3['n']} entries ===")
    for e in p3["entries"]:
        if e["func"] == 0x100CEB40 or e["va"] in (0x100CEB47, 0x100CEB4C):
            extra.append(f"  SETS-DETOUR {e['asm']} bytes {e['bytes']}")
    extra_path = os.path.join(OUT_DIR, "census_extra.txt")
    with open(extra_path, "w", encoding="utf-8") as f:
        f.write("\n".join(extra) + "\n")
    print("wrote", extra_path)


if __name__ == "__main__":
    main()
