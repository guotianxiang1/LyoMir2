"""恢复眼神插件 memcpy 补丁站点的替换载荷。

背景
----
裸 memcpy builder ``0x10033340`` 占全部 407 个补丁站点的 306 个（75%），签名是
cdecl 四参 ``C(payload, len, va, va)``，做整段字节替换。调用点形如::

    100CF45C  mov dword [ebp-0xB0], 0x89575653   ; 就地拼载荷
    100CF466  mov word  [ebp-0xAC], 0xF84D       ; 合计 6 字节
    100CF47F  push 0x767BAE                      ; arg4 = 宿主 VA
    100CF484  push 0x767BAE                      ; arg3 = 宿主 VA（同值）
    100CF489  push 6                             ; arg2 = 长度
    100CF48B  push eax                           ; arg1 = lea [ebp-0xB0]
    100CF496  call 0x10033340

因为是 cdecl，四个实参就紧贴在 call 前面，载荷也在同一个基本块里就地拼出。
**窄窗口（0x60 字节）足够，加宽窗口只会让对齐搜索爆炸** —— 有代理用"加宽窗口"
的思路跑了 189 秒零输出被超时杀掉，而本脚本单站点 <10ms。

用法
----
    python tools/ys_payload_recover.py                 # 跑 atlas 里全部 memcpy 站点
    python tools/ys_payload_recover.py 0x100CF496 ...  # 只跑指定站点
"""

import struct
import sys
from pathlib import Path

from capstone import CS_ARCH_X86, CS_MODE_32, Cs

DLL_BASE = 0x10000000
HOST_BASE = 0x400000
MEMCPY_BUILDER = 0x10033340

REPO = Path(__file__).resolve().parents[1]
DLL = Path(r"D:\loym2\staging\yanshen208_strparam_runtime_dump_20260719"
           r"\yanshen2_0_8_dll.memory.bin")
HOST = Path(r"D:\loym2\staging\_reunpack_work\flat_image.bin")
ATLAS = REPO / "docs" / "ys_patch_sites_atlas.tsv"

_md = Cs(CS_ARCH_X86, CS_MODE_32)


def _decode_to(img, base, end_va, window=0x60):
    """返回一条能线性对齐到 end_va 的指令流（含 end_va 那条）。"""
    for off in range(window, 3, -1):
        start = end_va - off
        stream = list(_md.disasm(img[start - base:end_va - base + 8], start))
        if stream and any(i.address == end_va for i in stream):
            return [i for i in stream if i.address <= end_va]
    return []


def _imm(op):
    try:
        return int(op, 16) if op.startswith("0x") else int(op)
    except ValueError:
        return None


def recover(dll, call_va):
    """→ (len, host_va, payload_bytes) 或 None。"""
    stream = _decode_to(dll, DLL_BASE, call_va)
    if not stream:
        return None
    pushes = [i for i in stream if i.mnemonic == "push"]
    if len(pushes) < 4:
        return None
    arg1, arg2, arg3 = pushes[-1], pushes[-2], pushes[-3]
    length, host_va = _imm(arg2.op_str), _imm(arg3.op_str)
    if length is None or host_va is None:
        return None

    # arg1 之前最近的 `lea reg,[ebp-X]` 给出载荷缓冲的基址。
    slot = None
    for ins in reversed(stream):
        if ins.address >= arg1.address:
            continue
        if ins.mnemonic == "lea" and "ebp - " in ins.op_str:
            slot = int(ins.op_str.split("ebp - ")[1].rstrip("]"), 16)
            break
    if slot is None:
        return length, host_va, b""

    # 收集写进 [slot, slot+length) 的立即数 mov。
    cells = {}
    for ins in stream:
        if ins.mnemonic != "mov" or "ebp - " not in ins.op_str:
            continue
        dst, _, src = ins.op_str.partition(",")
        src = src.strip()
        if not src.startswith("0x"):
            continue
        try:
            off = int(dst.split("ebp - ")[1].rstrip("]"), 16)
        except (IndexError, ValueError):
            continue
        rel = slot - off
        if not 0 <= rel < length:
            continue
        width = 4 if "dword" in dst else (2 if "word" in dst else 1)
        value = int(src, 16)
        for k in range(width):
            if rel + k < length:
                cells[rel + k] = (value >> (8 * k)) & 0xFF

    payload = bytes(cells.get(i, 0) for i in range(length))
    return length, host_va, payload if cells else b""


def _memcpy_sites():
    if not ATLAS.exists():
        return []
    rows = ATLAS.read_text(encoding="utf-8").splitlines()
    head = rows[0].split("\t")
    ix = {name: i for i, name in enumerate(head)}
    out = []
    for row in rows[1:]:
        col = row.split("\t")
        if len(col) <= ix["kind"] or col[ix["kind"]] != "memcpy":
            continue
        out.append((int(col[ix["site_va"]], 16), col[ix["label"]],
                    col[ix["arm"]]))
    return out


def main(argv):
    dll = DLL.read_bytes()
    host = HOST.read_bytes() if HOST.exists() else b""

    if argv:
        sites = [(int(a, 16), "", "") for a in argv]
    else:
        sites = _memcpy_sites()
        if not sites:
            print("找不到 docs/ys_patch_sites_atlas.tsv，请显式传入站点 VA")
            return 1

    ok = 0
    for call_va, label, arm in sites:
        got = recover(dll, call_va)
        if not got:
            print(f"0x{call_va:08X}  {label}  {arm}  -- 恢复失败")
            continue
        length, host_va, payload = got
        ok += 1
        print(f"0x{call_va:08X}  {label}  {arm}  "
              f"host=0x{host_va:08X} len={length}")
        if payload:
            print(f"    patch : {payload.hex(' ')}")
            for ins in _md.disasm(payload, host_va):
                print(f"      {ins.address:08X}  {ins.bytes.hex():<14} "
                      f"{ins.mnemonic} {ins.op_str}")
        if host and HOST_BASE <= host_va < HOST_BASE + len(host):
            orig = host[host_va - HOST_BASE:host_va - HOST_BASE + length]
            print(f"    orig  : {orig.hex(' ')}")
            for ins in _md.disasm(orig, host_va):
                print(f"      {ins.address:08X}  {ins.bytes.hex():<14} "
                      f"{ins.mnemonic} {ins.op_str}")

    print(f"\n{ok}/{len(sites)} 个站点恢复成功")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
