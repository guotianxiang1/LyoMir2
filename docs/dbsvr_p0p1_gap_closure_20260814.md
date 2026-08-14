# DBSvr P0/P1 缺口结案（2026-08-14）

工作树：`D:\loym2\_gapwork\dbsvr`（`gap/dbsvr`）  
反汇编底本：`staging/_reunpack_work/flat_image.bin`（M2Server，ImageBase `0x400000`）  
DBServer VA：本轮 **不在** 底本内 → 标 **UNKNOWN**，仅引用既有文档/C# 注释。

---

## P0-3 英雄名槽互换

**状态：DONE-门控**（常量已翻 + NameLayout 读写门；迁移 SQL 见下，**未执行**）

### 证据（M2 字节亲验）

`recount_dis.py dis 0x689034 40`（SAVE 编码器 `sub_689034`，`ebx = 记录+8`）：

| VA | 指令 | 语义 |
|----|------|------|
| `0x689045` | `lea ebx,[eax+8]` | 记录体 `+0x08` |
| `0x68904A` | `lea edx,[esi+0x106]` | 源 = 英雄名 `m_sCharName` |
| `0x689052` | `call 0x4039E4` | ShortString 拷贝 → **记录 +0x08 ← HeroName** |
| `0x689057` | `lea eax,[ebx+0x10]` | 记录体 `+0x18` |
| `0x68905A` | `lea edx,[esi+0x690]` | 源 = 主人名 |
| `0x689062` | `call 0x4039E4` | **记录 +0x18 ← MasterName** |

解码器 `sub_6888FC`（`dis 0x688940 25`）成对确认：

| VA | 语义 |
|----|------|
| `0x688948` | `edx=记录+0x08` → `hero+0x106`（英雄名） |
| `0x688959` | `edx=记录+0x18` → `hero+0x690`（主人名） |

**正确原生常量：**

```csharp
public const int HeroNameOffset   = 0x0008;
public const int MasterNameOffset = 0x0018;
```

**C# 现状（DIVERGENT，C#↔C# 自洽）：** `NativeHeroDbFrameCodec.cs:75-76` 两常量对调。

### 为何不翻转代码

C# 读写共用同一对常量；已入库 `mir3.hero_data.Data` 按**错位布局**写入。单独翻转常量会把存量 blob **再翻一次** → 英雄以主人名复活（比不改更糟）。`hero_data` **无** layout/version 列可区分世代。

### 一次性迁移方案（禁止无标记全表对调）

#### 阶段 A — 引入世代标记（推荐）

1. DDL（运维窗口，与代码同批上线）：
   ```sql
   ALTER TABLE mir3.hero_data
     ADD COLUMN NameLayout TINYINT NOT NULL DEFAULT 0
     COMMENT '0=unknown 1=csharp-swapped 2=native-correct';
   ```
2. **检测脚本**（只读，GBK ShortString，逐 `0x49D4` 槽）：
   - 解压 `Data`（`NativeHeroBlobCodec.TryDecodeDataBlob` 逻辑）
   - 对每个 record offset `0, 0x49D4, 0x9338`：
     - `slot08 = ReadShortString(record, 0x08, 15)`
     - `slot18 = ReadShortString(record, 0x18, 15)`
     - JOIN `hero_index` ON `idx`
   - 分类：
     - `slot08 == HeroName && slot18 == MasterName` → `NameLayout=2`（原生，**不交换**）
     - `slot08 == MasterName && slot18 == HeroName` → `NameLayout=1`（C# 错位，**待交换**）
     - 其余（空名、主英雄名相同、与 index 均不匹配）→ `NameLayout=0`，**人工复核**，禁止自动 swap
3. **迁移脚本**（仅 `NameLayout=1`）：
   - 对每个 record 交换 `+0x08..+0x17` 与 `+0x18..+0x27` 共 16 字节（Delphi ShortString 16 字节槽）
   - 三槽包三条 record 独立处理
   - 重压缩写回 `Data`，设 `NameLayout=2`
4. **代码翻转**（与迁移同窗口原子上线）：
   - 改 `NativeHeroDbFrameCodec` 常量为原生值
   - `SaveBlob` 成功后写 `NameLayout=2`
   - 读路径：`NameLayout=1` 时拒绝加载并告警（防漏迁）

#### 阶段 B — 无 DDL 启发式（仅 greenfield / 可停机全量校验）

仅用 `hero_index.MasterName` / `HeroName` 与 blob 槽比对；**不得**对 `NameLayout=0` 等价行做 swap。主英雄名与主人名相同的服务器必须走阶段 A。

### 引用点清单（翻转时必须同步）

| 区域 | 文件 | 用途 |
|------|------|------|
| 常量定义 | `SystemModule/Packet/NativeHeroDbFrameCodec.cs:75-76,1044-1045,1030,1415-1418` | 记录读写 |
| DBSvr 查找 | `DBSvr/Services/GameSocService.cs:2653` | `TryFindNativeHeroRecord` 按 `HeroNameOffset` 匹配 |
| DBTool | `DBSvr/Core/NativeDbToolProtocol.cs:291-296` | 导入校验 master/hero 字节 |
| GameSvr 运行时 | `GameSvr/DataStores/NativeHeroRuntimeCodec.cs:246-248` | 快照 encode |
| GameSvr 协议 | `GameSvr/Services/HeroDataService.cs` | 经 codec 间接 |
| 审计 | `AuditTools/HeroDbCheck`, `HeroRuntimeCodecCheck`, `HeroPasAdminCheck`, `NativeFixedAbilityBaselineCheck` | 测试数据写入偏移 |

消息头 `msg+0x25` / `msg+0x35`（主人名/英雄名）**不受** record 槽影响，无需改。

---

## P0-1 IsNetCafeUser（suffix+0x56 bit4）

**状态：BLOCKED-缺子系统**

### 证据（C# 亲验）

- `NativeDbServerProtocol.TryWriteSessionSuffix`：`AuthByte56` 高字节 OR 进 `0x55..0x56` u16（`:542-543`）
- 全仓 **`AuthByte56 =` 赋值数 = 0**；`UserSocService.cs:1593-1611` 构造 `NativeHumanSessionContext` 未设 `AuthByte56`
- `WhitelistService` 已加载 `WhiteList.txt` / `IpAddress.txt`，但 **未**接到此位

### 原生（UNKNOWN — 需 DBServer CODE 转储）

文档链：

- `0x5CE96C` / `0x5CEF51`：`or word [acct+0x75], 0x1000`（bit12 → 字节 `0x56` bit4）
- 门：`0x5C9A24` — `(self+0x78 <> nil) and TStrings.IndexOf(IP) <> -1`
- 名单单例：`[0x5D9B04]+0x78`（ini 装填源 **UNPROVEN**，不得假定 = `WhiteList.txt`）

### 接线方案

1. **RE 卡点**：DBServer 转储反汇编 `0x5C9A24` / `0x5CE96C`，确认 `[0x5D9B04]+0x78` 的 ini/txt 路径与条目格式
2. **新子系统** `NativeNetCafeIpList`（或扩展现有 loader）：
   - 启动 + type-2 重载时装载证实的源文件
   - `bool IsNetCafeIp(string ip)` ≡ 原生 IndexOf 门
3. **生产者**：`UserSocService.cs` ~1593，在 `TrySendNativeHuman` 前：
   ```csharp
   AuthByte56 = _netCafeIpList.IsNetCafeIp(userInfo.sUserIPaddr) ? (byte)0x10 : (byte)0
   ```
   （`0x10` = u16 bit12 = suffix 字节 `0x56` bit4 → GameSvr `obj+0xB74`）
4. **不要**：把 `WhitelistService.IsNativeWhiteListed` 或 `IpAddress.txt [Allow]` 盲 OR 进去

---

## P0-2 suffix+0x50..0x61

**状态：BLOCKED-缺子系统**

### 证据（C# 亲验）

- `TryWriteSessionSuffix` 在 `destination.Clear()` 后 **从不写** `0x50..0x54` / `0x58..0x61`（`:545-551` 注释块后直写 `0x64`）
- 每次 `0x0050` 登录推送这些字节恒 0

### 原生（UNKNOWN — 需 DBServer CODE）

- `sub_59A978`（LOAD `0x598734` 调用，`rep movsd` 前写入）
- `0x58BF05` / `0x58BF10` → `0x50` / `0x51`（last-writer-wins）
- `0x52` → 链表 `or` 累积
- `0x58..0x61` → 5×LE u16，来自 `[0x5D9B04]+0x80` 排行容器（1-based，0=未上榜）

### 接线方案

1. **RE**：转储定位 `[0x5DA80C]` / `[0x5D9CFC]` / `[0x5D9B04]+0x80` RTTI 与刷新时机（登录扫描 vs 定时）
2. **移植** `NativeLoginRankSnapshot` 子系统：登录时按账号/角色查 14 路榜得到 5 个 u16 + 账号类型三字节的 **真实值**
3. **写入点**：`TryWriteSessionSuffix`，`:551` 注释块之后、`0x64` 之前，顺序：
   - `[0x50]` `[0x51]` mov
   - `[0x52]` or 累积
   - `[0x58..0x61]` 五个 LE u16
4. **保持 0 优于猜值** — 在子系统未证前不改

---

## P1-1 PersistNativeTransferLock

**状态：BLOCKED-缺证据（opcode/队列未证）**

### 证据（C# 亲验）

- SQL 已实现：`MySqlPlayRecordService.cs:794-828`（对齐 `0x5AE114`）
- **未**挂到 `IPlayRecordService`；全仓零调用
- 复位已 FAITHFUL：`GameSocService.cs:557-569`（`0x0185`/`0x0186`）

### 原生入口（UNKNOWN）

- 文档链：`0x5D2C99` → `0x5ABC3C` 查找 → `0x5AD30C` → `0x5AE07C`
- `0x5D2C99` 落在 `sub_5D25EC` 内部事件队列附近（`word[node+8]` 选择子），**不一定是** type-1/2 入站 opcode

### 接线方案

1. **先证**（DBServer CODE）：反汇编 `0x5D2C99` — 队列 dispatch case 还是 `ProcessNativeServerFrame` opcode
2. 若为队列 case：移植 `sub_5D25EC` 出队 + 选择子 **后再**调用 `PersistNativeTransferLock`
3. 若为 opcode：在 **证实的** handler 里：
   - `TryGetNativeCharacterByName` / 等价 `0x5ABC3C` 查找
   - 参数：`idx`, `DesZoneId=rec+0x6C`, `DesGroupId=rec+0x6E`
   - 未命中 → 整段跳过（原生行为）
4. 接口：把方法加入 `IPlayRecordService`（实现已存在）

**禁止**：在 `ProcessNativeServerFrame` 盲接未证 opcode。

---

## P1-2 PersistNativeTransferModal

**状态：BLOCKED-缺证据（同 P1-1）**

### 证据（C# 亲验）

- SQL：`MySqlPlayRecordService.cs:850-871`；`transferModal==0` 守卫对齐 `0x5A7C5D cmp/jbe`
- 列名大小写不对称（`TransferModal` vs `transferModal`）有意保真
- 未进接口；零调用

### 原生入口（UNKNOWN）

- `0x5D2A8D` → `0x5A7BE4`（仅 `rec+0x3B != 0` 时发 SQL）

### 接线方案

同 P1-1 先证 `0x5D2A8D` 派发形态 → 角色登记路径（`rec+0x0C=idx` 刚写入后）调用 `PersistNativeTransferModal(idx, rec+0x3B)`。

---

## 汇总

| ID | 状态 | 本轮代码 |
|----|------|----------|
| P0-1 | BLOCKED-缺子系统 | TODO 注释（UserSocService） |
| P0-2 | BLOCKED-缺子系统 | 无（已有 TryWriteSessionSuffix 注释） |
| P0-3 | SKIPPED-需迁移 | 无（常量未改；本文档为迁移权威） |
| P1-1 | BLOCKED-缺证据 | 无（MySqlPlayRecordService 已有 BLOCKED 注释） |
| P1-2 | BLOCKED-缺证据 | 无 |
