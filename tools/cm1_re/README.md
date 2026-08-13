# CM 分发跳表：一手推导

底本 `D:\loym2\staging\_reunpack_work\flat_image.bin`，ImageBase `0x400000`。

## 分发器

客户端命令分发器是 `sub_6D7D68`，唯一调用者 `0x6B1B36`。签名（序言字节）：

```
006D7D7E  89 4D F8     mov [ebp-8], ecx    ; ECX = 包体指针（len<=0 时为 nil）
006D7D81  8B DA        mov ebx, edx        ; EDX = 12 字节头记录
006D7D83  89 45 FC     mov [ebp-4], eax    ; EAX = self (TPlayObject)
006D7D86  8B 75 08     mov esi, [ebp+8]    ; [ebp+8] = 包体长度（第 4 参）
```

case 树的头在 `0x6D805C`：

```
006D805C  8B 45 CC              mov   eax, [ebp-0x34]
006D805F  0F B7 40 04           movzx eax, word [eax+4]   ; Ident
006D8063  3D D6 0C 00 00        cmp   eax, 0xCD6          ; <- 树根
```

## 推导方法

`_cm1_jumptable.py` 对 `0..0xFFFF` 每个 opcode 做**具体执行**：从 `0x6D8063` 单步，
只解释构成 Delphi case 树的指令（`cmp/sub/add/dec/inc eax` + 条件跳转 +
`jmp dword [eax*4+table]`）。碰到第一条不属于 case 树的指令即判定为处理器入口。
没有区间推理，因此不会因为符号化近似而漏臂或多臂。

`0x6DBC2C` 不是「default 处理器」而是**函数共同出口**（`33 C0 / 5A / 59 / 59 /
64 89 10 / E9 D5 00 00 00 -> 0x6DBD0E`，即 `Result := False` 后走 SEH 清理）。
所有真实处理器执行完也 `jmp 0x6DBC2C`。部分臂落在与它逐字节相同的副本上。
判定「原生 no-op」的标准因此是：**case 树直接跳到 `0x6DBC2C` 或其字节副本，
中间没有任何其它指令**——脚本按 `DEFAULT_SIG` 做这个判定。

## 结果

**311** 个 opcode 有真实处理器体。该数字与仓库既有结论
`GameSvr/Services/NativeClientBodyLengthGate.cs`（「311 real handlers」中 39 个带长度门）
独立吻合。完整映射见 `_cm1_map.json`。

## 文件

| 文件 | 内容 |
|---|---|
| `_cm1_jumptable.py` | 跳表推导；输出 `_cm1_map.json` |
| `_cm1_map.json` | opcode -> 处理器 VA，311 项 |
| `_cm1_diff.py` | 与 C# 侧 `case` 全集比对，输出缺失清单并四等分 |
| `_cm1_disasm.py` | 单点反汇编 |
| `_cm1_probe.py` | 处理器体遍历 + Delphi 长字符串解码 + 直接被调用者清单 |
| `_cm1_str.py` | 按 VA 解 Delphi 长字符串（写 UTF-8 文件，不走控制台） |
| `_cm1_dumpall.py` / `_cm1_batch1.txt` | 第 1/4 批 25 个处理器体 |
| `_cm1_dumpw.py` / `_cm1_workers.txt` | 这 25 个处理器的 worker 函数体 |
| `_cm1_bytes.py` / `_cm1_bytes.txt` | 25 个处理器的原始字节 |
| `_cm1_mgr.txt` | 元宝寄售经理侧 `0x6326F4 / 0x632B4C / 0x632FC4 / 0x6F9594` |

复现：

```
python _cm1_jumptable.py     # -> _cm1_map.json
python _cm1_diff.py          # -> 缺失清单
```
