#!/usr/bin/env python3
"""Cross-mine BLOCKED Yanshen VAs: 207 vs 208 runtime dumps."""
import capstone
import struct
import sys

BASE = 0x10000000
PATH207 = r"D:\loym2\staging\questinfo_runtime_dump\yanshen2_0_7_dll.memory.bin"
PATH208 = r"D:\loym2\staging\yanshen208_strparam_runtime_dump_20260719\yanshen2_0_8_dll.memory.bin"

VAS = [
    ("1031C250", "主号高级暴击 fn ptr"),
    ("1031C254", "高级英雄倍功暴击 fn ptr"),
    ("10067D92", "英雄野蛮 gate"),
    ("10068035", "英雄物攻触发 gate"),
    ("10067F16", "高级物攻触发 gate"),
    ("1006A99D", "千分比经验 gate"),
    ("1009029D", "麻痹中不被麻痹 gate"),
    ("646F40", "获取玩家对象 M2 (not ys base)"),
    ("1006953F", "主号分身术 gate"),
    ("100D120A", "tramp模板"),
    ("100CEB40", "S(1,1) SetS detour"),
    ("100795C0", "五法术切割 sub"),
    ("10068470", "主号分身术 sub"),
    ("10067C90", "英雄触发 parent"),
    ("1006A920", "千分比经验 sub"),
    ("10079FB1", "主号高级暴击 call site"),
    ("1007A014", "高级英雄倍功暴击 call site"),
    ("100CF36E", "刀刀切割 bank read"),
    ("10BB915A", "英雄野蛮 jmp target"),
    ("10F2D759", "call 100795C0 site"),
    ("1123B15E", "call 10068470 site"),
    ("1007A8A7", "英雄千分比免伤"),
    ("1007AF12", "冰咆哮切割 arm"),
]


def classify_bytes(b: bytes) -> str:
    if not b:
        return "OOB"
    if all(x == 0 for x in b):
        return "ALL-ZERO"
    if all(x in (0, 0xCC) for x in b):
        return "ZERO/CC"
    return "HAS-DATA"


def is_vm_region(va: int) -> bool:
    return 0x10400000 <= va < 0x11400000


def read_at(d: bytes, va: int, n: int) -> bytes:
    if va < BASE:
        return b""
    off = va - BASE
    if off < 0 or off >= len(d):
        return b""
    return d[off : off + n]


def disasm(code: bytes, va: int, limit: int = 16):
    cs = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    out = []
    for i in cs.disasm(code, va):
        out.append(i)
        if len(out) >= limit:
            break
    return out


def looks_like_native(insns) -> bool:
    if len(insns) < 2:
        return False
    mnems = {i.mnemonic for i in insns[:8]}
    # VM bytecode often has unusual patterns; native prologue common
    if "push" in mnems and "mov" in mnems:
        return True
    if insns[0].mnemonic in ("55", "push") and any(
        i.mnemonic == "mov" and "ebp" in i.op_str for i in insns[:4]
    ):
        return True
    return insns[0].bytes[0] not in (0x00, 0xCC)


def main():
    d207 = open(PATH207, "rb").read()
    d208 = open(PATH208, "rb").read()
    print(f"207 size={len(d207)} 208 size={len(d208)}")

    for va_s, name in VAS:
        va = int(va_s, 16)
        b207 = read_at(d207, va, 64)
        b208 = read_at(d208, va, 64)
        c207 = classify_bytes(b207[:16])
        c208 = classify_bytes(b208[:16])
        vm = " [VM-REGION]" if is_vm_region(va) else ""
        print(f"\n=== 0x{va:08X} {name}{vm} ===")
        h207 = b207[:16].hex() if b207 else "OOB"
        h208 = b208[:16].hex() if b208 else "OOB"
        print(f"  207[{c207}]: {h207}")
        print(f"  208[{c208}]: {h208}")

        if va in (0x1031C250, 0x1031C254) and len(b207) >= 4:
            ptr207 = struct.unpack("<I", b207[:4])[0]
            ptr208 = struct.unpack("<I", b208[:4])[0] if len(b208) >= 4 else 0
            print(f"  ptr207=0x{ptr207:08X} ptr208=0x{ptr208:08X}")
            for label, ptr, d in [("207", ptr207, d207), ("208", ptr208, d208)]:
                if ptr:
                    pb = read_at(d, ptr, 48)
                    print(
                        f"  {label} target 0x{ptr:08X} [{classify_bytes(pb)}]: "
                        f"{pb[:16].hex() if pb else 'OOB'}"
                    )
                    if classify_bytes(pb) == "HAS-DATA":
                        insns = disasm(pb, ptr, 12)
                        if insns:
                            print(f"    disasm: {insns[0].mnemonic} {insns[0].op_str} ...")

        if c207 == "HAS-DATA":
            insns = disasm(b207, va, 16)
            native = looks_like_native(insns)
            print(f"  207 disasm native={native} ({len(insns)} insns):")
            for i in insns[:14]:
                print(f"    0x{i.address:08X}: {i.mnemonic} {i.op_str}")


if __name__ == "__main__":
    main()
