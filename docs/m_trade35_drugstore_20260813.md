# TRADE-35：第三个仓库容器「药品仓库 / DrugStore」——fail-closed 登记（DBSvr 依赖）

- 日期 2026-08-13，分支 `w/trade-storage`，工作树 `D:/loym2/.claude/wt2/trade-storage`
- 镜像 `D:/loym2/staging/_reunpack_work/flat_image.bin`，ImageBase `0x400000`，capstone
- 复跑脚本：`D:/loym2/staging/_trade3dis/`（`dz.py` 反汇编 / `scan.py` 串+字节 / `xref.py` 交叉引用）
- 判定：**MISSING（DBSvr 依赖，本地不可全量建模）→ fail-closed 登记 + Series-2 未加载态对齐原生静默**

---

## 1. 三容器结构（硬证据）

`TPlayObject` 有三个仓库容器指针字段，全部在构造函数 `0x6AD8C0` 段建立：

| Series | 字段 | 名字串(GBK) | 类 vtable | 构造 | 容量初值 |
|---|---|---|---|---|---|
| 0 | `[+0x6D0]` | `0x6c2d48` 普通仓库 | `[0x74a22c]=0x74a278` | `0x74a318` | `[obj+8]=0x30`(48)，本地 |
| 1 | `[+0x6D4]` | `0x6c2d5c` 账号仓库 | 同上 | `0x74a318` | `[obj+8]=-1`（DBSvr 加载） |
| 2 | `[+0x6D8]` | `0x6c2d70` **药品仓库** | `[0x74a284]=0x74a2d0` | `0x74a7f4` | `[obj+8]=-1,[obj+0xc]=-1`（DBSvr 加载） |

构造证据：
```
0x6AD8E5  mov [edi+0x6d0], esi          ; 普通仓
0x6AD8EB  mov [esi+8], 0x30             ; 普通仓容量硬编码 48
0x6AD900  mov [edi+0x6d4], eax          ; 账号仓（不设容量 → -1）
0x6AD90A  mov eax,[0x74a284] / call 0x74a7f4
0x6AD914  mov [edi+0x6d8], eax          ; 药品仓（不同类）
```
药品仓类构造 `0x74a7f4`：`mov [eax+8],-1 / mov [eax+0xc],-1`（**双上限字段**）。
析构 `0x6AFBF7` 对 `[+0x6D8]` 调 `0x404690` Free。

## 2. 分发（同一 opcode 一函数处理三容器）

存仓分发桩 `0x6D91BB`：`ax=word[CM+0xA]`(Series) / `sub ax,3 / jae 拒绝`（Series≥3 拒）/
`inc / push`（栈参=Series+1）→ `sub_6C2A34`。取仓桩 `0x6D91FC` 同形 → `sub_6C2D7C`。
即 **Series ∈ {0,1,2}**，Series+1 ∈ {1,2,3} 作容器选择子。

`sub_6C2A34` 容器分派：`sub eax,2 / je →账号(0x6d4,0x74a510)`、`dec/je →药品(0x6d8,0x74a854)`、
默认普通(0x6d0)。存入动作再次按 Series 分派：0x6C2B54(0x6d0)/0x6C2B89(0x6d4)/0x6C2BBD(0x6d8)。

## 3. 药品仓 = DrugStore，DBSvr 加载式

开箱分发器 `sub_6C1860` mode 3（药品）@`0x6C1900`：
```
cmp esi,3 / jne
mov eax,[ebx+0x6d8] / call 0x74a854      ; 已加载？
test al,al / jne 已加载
mov eax,[ebx+0x6d8] / call 0x74a868      ; ★ DrugStore 加载请求
mov eax,[ebx+0x6d8] / call 0x74a854      ; 复检
test al,al / je 0x6c1973                 ; 仍未加载 → 静默退出
```
加载请求 `0x74a868` 向 LogServer/DBSvr（`[0x7d5d20]`）发四个键（`call 0x69aeb8`）：
`InitialDrugStore`(0x74a974) / `GetDrugStore` / `GetDrugStoreMaxCount`(0x74a990) /
`GetDrugStoreMaxDiffKind`(0x74a9b0)。响应写回 `[obj+8]`（最大件数）、`[obj+0xc]`（最大种类）。
存入前的种类门 `0x74a9c8`：`movzx edx,word[item+0x24] / call 0x74acfc / setg`（该种类计数>0）。

开箱五个调用点（`xref 0x6C1860`）：`0x6B55DC`(变量 mode，脚本可给 3)、`0x6D1325`(mode1)、
`0x6D1359`(mode2)、`0x6E577D`(mode2)、`0x6E57ED`(mode2)。密码门 `[+0x683]` 只挡 mode 1/2，
mode 3 免密码。**药品仓完整可达、非死代码。**

## 4. C# 现状与本轮处置

- CM 分发 `TPlayObject.Message.cs:1375` `CM_USERSTORAGEITEM`：wParam 0→普通、1→账号(DBSvr)、
  2→`RejectUnsupportedStorageItem(2)`；取仓同形。**药品仓（Series 2）未实现。**
- DBSvr 侧（本仓 `DBSvr/`）**无任何 DrugStore 支持**（`grep DrugStore` 0 命中）。无文档、无协议实现。
- 结论：药品仓是 **DBSvr 依赖功能**，本地不能全量建模 → **fail-closed 登记**。

**本轮代码改动（fail-closed 对齐原生）**：`RejectUnsupportedStorageItem(2)` /
`RejectUnsupportedTakeBackStorageItem(2)` 由「发 SM_STORAGE_FAIL」改为**静默**。
依据：容器未加载时原生 `sub_6C2A34 @0x6C2A93→je 0x6c2d15` 只析构局部串 + ret，**不发任何包**；
SM_STORAGE_FAIL(0x2BF) 仅在容器**已加载**后于 0x6c2cf4 失败出口才发。C# DrugStore 永远未加载，
故静默才是 1:1。旧实现多发一个原生不会发的包（同为 fail-closed 不丢物，但形状偏差）。
方法签名保留（`NativeAccountStorageCompatCheck` 用其做源码切片边界）。

**不落地全量的理由**：需要 DBSvr 端实现 InitialDrugStore/GetDrugStore/GetDrugStoreMaxCount/
GetDrugStoreMaxDiffKind 四条查询与持久化（双上限 + 种类门），属 roadmap DBSVR。按
「不确定一律 fail-closed，宁可少改」，交主控/ DBSvr 车道裁决。**不丢物、不刷物**（Series-2
存入被静默忽略，物品留在背包）。

## 5. 未来落地清单（当 DBSvr DrugStore 后端就绪）

1. 容器状态：仿 `NativeAccountStorage` 增 `NativeDrugStoreState`（双上限 MaxCount + MaxDiffKind，Items 列表，Capacity=-1 表未加载）。
2. DBSvr 客户端：`InitialDrugStore`/`GetDrugStore`/`GetDrugStoreMaxCount`/`GetDrugStoreMaxDiffKind` 请求/响应（对齐 0x74a868→0x69aeb8）。
3. 开箱：`sub_6C1860` mode 3（免密码；未加载先发加载请求，复检后发布）。
4. 存入 Series 2：`sub_6C2A34` 0x6C2BBD 臂——种类门 `0x74a9c8`（每种类计数上限）+ 双上限。
5. 取回 Series 2：`sub_6C2D7C` 对应臂。
6. Message.cs case 2 由静默改为路由到上述实现。
