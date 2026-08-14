# 盘古1 页 LABEL_ONLY 键 — 2026-08-14 闭合报告

工作区：`gap/ys-pangu1`。证据：`flat_image.bin` @0x400000 + `recount_dis.py`。

## 汇总

| 状态 | 数量 |
|---|---|
| DONE | 25 |
| BLOCKED | 5 |

---

## DONE（25）

| 键 | VA | C# 落点 |
|---|---|---|
| 专职变性 | — | EQUIVALENT_BY_ABSENCE：原生无 patch，C# 无消费者 |
| 修复刺杀位麻痹 | — | EQUIVALENT_BY_ABSENCE |
| 全服击杀提示 | — | EQUIVALENT_BY_ABSENCE（`ShouldSendKillNotice` 仅插件侧） |
| 关闭摆摊 | 0x6E7C38 | `TPlayObject.NativeStall.cs:79` `IsStallClosed()` START 静默返回 |
| 召唤骷髅_数量 | 0x76EE98 | `MagicManager.cs:2112` `KuLouSlaveCount()` |
| 土城摆摊 | 0x6E7C5F | `YanshenPangu1Patches.cs:73` + `NativeStall.cs:559` 位置闸 bypass |
| 屏蔽元宝增减信息 | 0x6F8288 | `TBaseObject.cs:1063` 抑制 `RM_GAMEGOLDCHANGED` |
| 屏蔽元宝数据库日志 | 0x70F6DC | `YanshenPangu1Patches.cs:53` + `TPlayObject.Message.cs:544-612` |
| 屏蔽属性提升提示 | 0x741A21…0x74298C | `NativeStateArms.cs:114` / `StateArmSysMsg.cs:27` / `TimedAbilityStateDispatch.cs:115` / `TimedAbility.cs:757` / `Base.cs:658` |
| 指定地图编号摆摊 | 0x6E7930/0x6E7934 | `YanshenPangu1Patches.cs:85` `MapMatchesStallPolicy` |
| 摆摊地图 | — | PARAM：`GetStallMapId()` @ `YanshenApi.cs:5325` |
| 摆摊穿人 | 0x77931D | `Envirnoment.cs:334` `SetNativeStallCellAttribute` |
| 盘古击杀触发 | — | EQUIVALENT_BY_ABSENCE |
| 盘古杀死宝宝 | — | PLUGIN_SIDE_ONLY |
| 盘古物理攻击触发 | — | EQUIVALENT_BY_ABSENCE |
| 盘古给与封号 | — | EQUIVALENT_BY_ABSENCE |
| 神兽_序号 | — | EQUIVALENT_BY_ABSENCE（`ShenShouIdx` 随召唤神兽路径） |
| 神兽_数量 | 0x76EE99 | `MagicManager.cs:2020` `ShenShouSlaveCount()` |
| 穿人穿怪 | 0x768454/0x6B30A3 | `TPlayObject.NativePassThrough.cs:57` |
| 限制摆摊 | — | `YanshenPangu1Patches.cs:100` + `YanshenApi.cs:5311` |
| 限制摆摊_左x | — | PARAM @ `IsStallAllowed` |
| 限制摆摊_左y | — | PARAM |
| 限制摆摊_右x | — | PARAM |
| 限制摆摊_右y | — | PARAM |
| 限制摆摊_等级 | — | PARAM |

---

## BLOCKED（5）

| 键 | VA | 原因 |
|---|---|---|
| 攻沙脚本控制 | 0x65C6B6/0x65C76D/0x65C785 | 6B patch 改条件跳，C# 落点（`UserCastle` 攻沙脚本闸）未命名 |
| 盘古高级属性 | 0x6F9AB0 等 | 43B 长载荷 tramp，未回放 |
| 脚本控制头发外显 | 0x740F85 | tramp2 跳过 `[esi+0x70]` 头发写入，C# 落点未定 |
| 邮件防刷 | 0x6E7810 | tramp2 @ mail/stall 闸，C# 落点未定 |
| 随身仓库 | 0x6E087C 等 | 45B 长载荷，未回放 |
