# 眼神 BLOCKED 项 · 207 转储交叉挖掘报告

- 日期：2026-08-14
- 分支：`gap/ys-207mine`
- 底本：
  - **207** `staging/questinfo_runtime_dump/yanshen2_0_7_dll.memory.bin` @0x10000000（28,446,720 B）
  - **208** `staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin` @0x10000000（45,821,952 B）
- 工具：`tools/ys_207_crossmine.py` + capstone 5.0.7

## 汇总

| 类别 | 数 |
|---|---:|
| **207-RECOVERED**（已接线） | **2** |
| **STILL-VM-BLOCKED** | **11** |
| **207-ALSO-ZERO / 无原生 x86** | **4** |

207 VM 区 `0x10400000..0x11400000` 非零 **98.12%**；208 同区 **0%**（Themida 未激活）。207 对 VM 区内字节仍多为 mutation/VM 字节码，**不能**当 x86 实现。

---

## 207-RECOVERED

| 键 / 项 | 207 VA | 208 VA | C# 落点 |
|---|---|---|---|
| 英雄千分比免伤 | `0x1006DA87` 门 / `0x1006DAD4` GetS(1,58) | `0x1007A8A7` | `YanshenPage1PostDamage.cs:ApplyHeroPermilleReduction` → `TBaseObject.NativeMagicDamage.cs` |
| 麻痹中不被麻痹a（首臂） | `0x100827A4` `cmp [target+0x168],0` | `0x100902B4` | `YanshenPage2ExtBehaviors.cs:ShouldImmuneParalysisWhileStatusActive` → `TBaseObject.NativeMakePosion.cs` |

注：207 配置字段偏移与 208 有差（例：英雄免伤 `+0x104` vs `+0x108`；麻痹免疫 `+0x6A0` vs `+0x6C0`），C# 按 **208 生产键名** 走 `PatchToggleOn`，不硬编码偏移。

---

## STILL-VM-BLOCKED

| 键 / 项 | 208 | 207 | 卡点 |
|---|---|---|---|
| 主号高级暴击 | `0x10079FB1` → `call [0x1031C250]` | 流水线在 `0x1006D3EF`，桩 `[0x7AA650B8]`（宿主址，转储 OOB） | 运行期桩体；207/208 均无槽内可 disasm 的 x86 |
| 高级英雄倍功暴击 | `0x1007A014` → `[0x1031C254]` | `0x1006D452` → `[0x7AA650BC]` | 同上 |
| `sub_100795C0` 宿主入口 | `0x10F2D759` 208 零页 | 207 有字节但为 VM mutation，无 `E8→0x100795C0` | Themida 远端区 |
| 英雄野蛮 | `0x10067D92` jmp `0x10BB915A` | 207 门 `0x1005B9E8`（偏移不同），无 jmp 等价 | VM 目标 |
| 英雄/高级物理攻击触发 | `0x10068035` / `0x10067F16` | 207 布局不同，`sub_10067C90` 无静态 rel32 调用者 | 插件入口不可证 |
| 千分比经验倍数 | `0x1006A99D` | `0x1006A9A3` 语义同，但 `sub_1006A920` **0 静态调用者** | 宿主落点不可证 |
| 主号分身术a | `0x1006953F` | 207 同址字节不可 disasm 为门控 | `sub_10068470` 仅 VM 区 `call` |
| S(1,1) 禁言 6/7/8 | SetS detour `0x100CEB40` jmp VM | 207 `0x100CEB40` 为 C++ vector 体，非 SetS 语义 | 208 进零页；207 同 VA 不对齐 |
| 刀刀切割 `@Cutting` | 208 `0x100CF36E` trampoline | 207 `0x100CF370` 为 ctor，非切割体 | 银行 `[+0x18]/[+0x470]` 仍无 C# 模型 |
| `0x100D120A` 永久属性模板 | 208 为 builder 调用 | 207 有 x86 模板体，但未回收 184 dword 完整链 | 模板→站点映射未完成 |
| 获取玩家对象函数 | M2 `0x646F40` 72/84B 载荷 | 不在 207 插件镜像 | 长载荷未逐帧回放 |

---

## 207-ALSO-ZERO / 桩内非代码

| VA | 208 | 207 | 判定 |
|---|---|---|---|
| `0x1031C250` | 全零 | 非零但 `ptr=0x8A1F2ECB` OOB；无 `mov ebx,[1031C250]` | 非函数指针，不可实现 |
| `0x1031C254` | 全零 | 同左 `0x83E1E758` OOB | 同上 |
| `0x10F2D759` | 全零 | 有数据，disasm 为 VM garbage | BLOCKED-VM |
| `0x10BB915A` | 全零 | VM garbage | BLOCKED-VM |

---

## 已在此前 wave 接线（本轮未改）

- 五法术切割 / 攻击吸血 / 火墙不吸血：`sub_100795C0` 语义已在 `YanshenPage1PostDamage` / `YanshenPage2ExtBehaviors`（208 字节 + 函数内序；207 流水线在 `0x1006D000` 段同构但入口仍 VM）。

---

## commit

见 `git log -1 --oneline gap/ys-207mine`。
