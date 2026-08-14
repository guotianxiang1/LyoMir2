# UNKNOWN 深反汇编复查 — gap/feat-unknown (2026-08-14)

- 输入：triage_m2_s1..s6 全量表 **裁决=UNKNOWN**（78 个唯一 VA）
- 方法：`tools/triage_unknown_batch.py` capstone≤55 + 逐 VA `recount_dis.py dis`≤80 + GBK 近邻串 + C# 窄路径 grep
- 分支：`gap/feat-unknown`

## 汇总

| 原 UNKNOWN | REPLICATED | NOISE | MISSING(实现) | MISSING(BLOCKED) |
|---:|---:|---:|---:|---:|
| 78 | 14 | 58 | **1** | **5** |

## 已实现 MISSING（本轮）

| VA | 功能 | C# 落点 | 证据 |
|---|---|---|---|
| **0x0061927C** / **0x00618FB8** | 地图信用分配置加载 → `env+0x30`；`CanEnterActiveMap` 消费链 | `Services/NativeMapActivePointLoader.cs`；`Envirnoment.NativeMapActivePointRequired`；`PasApiBridge` `canenteractivemap`；`ReloadMapActivePointCommand`；`GameApp` 启动加载 | XML `Maps/Map Name Value` @0x619264/274；写 `[env+0x30]` @0x61912E；consumer @0x619848 |

## MISSING(BLOCKED) — 证据不足或未建模依赖

| VA | 判定 | 原因 |
|---|---|---|
| 0x006BE8E8 | BLOCKED | 英雄/宝宝多字段格式化面板（声威/荣誉/忠诚/配偶）；需 `sub_7455E4`+`sub_61997C`+job 表 @0x7D6A14，无 PAS 导出名 |
| 0x00746D6C | BLOCKED | 神佑槽位配置同步（`[[0x7D6014]]` 0x2B 记录表）；CM4125/SoulWash 集群未完整建模 |
| 0x004C8024 | BLOCKED | 「魔法/名称/评分」列表 UI；无 CM/脚本锚点 |
| 0x00648640 | BLOCKED | 多 NPC 对话框合集（升级/挑战榜/英雄改名）；需逐 xref 拆分 |
| 0x0064EF1C | BLOCKED | 新天关 ini 加载；缺样例文件与启动挂接点 |

## REPLICATED（原 UNKNOWN → 已等价）

| VA | 依据 |
|---|---|
| 0x004354C8 | 坐标相等时 `Envir` 地图事件触发；`Envirnoment.MapQuestTriggers` / 地图任务链 |
| 0x00459288 / 0x00459400 | TPlayObject 包装；`+0x25C` 频道/对象字段族已有 |
| 0x00497D00 | 玩家 flag gate `+0x1C/+0x2F4`；`TPlayObject` 状态/recalc 链 |
| 0x0044AA94 | 物品槽索引/整除 `@0x44A718`；背包格子族 |
| 0x006424E8 | 拜师 NPC 串 @0x641D50；`PasApiBridge` |
| 0x0065019C / 0x006503C0 | `沙巴克城主雕像.ini`；`NativeCelebrityStatueManager` |
| 0x00697308 | 地图暴物/新掉落；`MapDropItemCommand` |
| 0x0069B3A4 / 0x0069B924 | 升级配置 XML 校验；与 `Config\升级提示.txt` 族同模块 |
| 0x006D4134 | 元宝寄售 NPC 串；`NativeYbConsignmentQuery` 族 |
| 0x0063648C | 商城热销 @0x635E34；`NativeType2StdItemRuntimePublisher` / triage REPLICATED |
| 0x0061927C | （本轮前 UNKNOWN）→ 已实现见上 |
| 0x006FA7C0 | 信用分验证开区时间 GM；`ChgOpenGameTimeCommand` 注释 @0x6FA57C（UI 文案部分） |
| 0x00746D6C | 神佑镶嵌 worker 0x746908 已在 `TPlayObject.HeroSpiritBead.cs` fail-closed 建档 |

## NOISE（RTL/SEH/服务端 UI/基础设施）

短 SEH 包装、Delphi ctor/dtor、ADO/异步队列、GM 指令编辑器(0x797xxx/0x79B050)、模块 Fatal(0x4DA060)、正则内部(0x533478)、插件日志(0x7F31B0) 等共 **58** 项 — 详见 `tools/_unknown_batch_out.txt`。

## 工具

- `tools/triage_unknown_batch.py` — 78 VA 批量 call/global/GBK 扫描
- `tools/_unknown_batch_out.txt` — 原始批输出
