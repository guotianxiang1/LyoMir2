# TRADE-52：196 字节二进制日志记录 / LogServer 传输 —— 泄漏修复 + fail-closed 登记

- 日期 2026-08-13，分支 `w/trade-storage`，镜像 base 0x400000，capstone
- 判定：**C# 无界字符串列表内存泄漏（已修：FIFO 封顶）· LogServer 196 字节传输 = DBSVR 依赖 out-of-scope（登记）**

---

## 1. 泄漏根因（C#）

`M2Share.AddGameDataLog(sMsg)` → `LogStringList.Add`（`AddLogonCostLog` → `LogonCostLogList`）。
两个列表运行期**只写不读**：全库 `LogStringList` 非审计引用仅 `M2Share.cs`(声明+Add) 与
`GameApp.cs:568`(new ArrayList)；`LogonCostLogList` 同形。**无任何 drain/发送/清空** → 随游戏
运行无界增长 = 内存泄漏。（审计工具各自 new/Clear 当探针用，不是运行期消费者。）

## 2. 原生真相：编码 196 字节记录后立即发送，不缓冲

`AddGameDataLog` 本体 `sub_79D3D8`（玩家侧包装 `sub_768BE0` @0x6C2C8E 等处 dx=类型 调入）：

```
0x79D40D  mov dword [edi], 0x33AABB77   ; magic（77BBAA33 内部转发协议）
0x79D413  mov byte  [edi+4], 1          ; flag/版本
0x79D417  mov byte  [edi+5], 0
0x79D41B  mov word  [edi+6], 0xBC       ; body 长度 = 188
0x79D421  mov byte  [edi+0x1d], bl      ; 日志类型
   … 逐字段填充（string 先 0x4057ac 截 0xFF，再 0x4039e4 定长拷贝）：
0x79D43C  [edi+0x08] char[0x14]         ; 字段1（20B）
0x79D44A  [edi+0x20] dword              ; word[ebp+0x20]（坐标X）
0x79D451  [edi+0x24] dword              ; word[ebp+0x1c]（坐标Y）
0x79D46D  [edi+0x28] char[0x14]         ; 字段2（20B）
0x79D490  [edi+0x3d] char[0x14]         ; 字段3（20B）
0x79D49D  [edi+0x54] dword              ; [ebp+0x10]
0x79D4A3  [edi+0x58] dword              ; [ebp+0x0c]
0x79D4C2  [edi+0x5c] char[0x64]         ; 字段4（100B，如物品/描述）
0x79D4CD  mov eax,[ebp-4] / mov eax,[eax+0x44]   ; LogServer 连接对象
0x79D4D3  mov ecx, 0xC4                 ; ★ 记录长度 = 196
0x79D4D8  call 0x4a0684                 ; 发送 196 字节到 LogServer socket
```

即：**记录 = 8 字节头(magic 0x33AABB77 / flag1 / 0 / word 0xBC=188) + 188 字节体 = 196 字节**，
`sub_768BE0` 组好玩家字段（[+0x106]角色名、[+0x12c]/[+0x130]坐标等）后 `sub_79D3D8` 立即
`call 0x4a0684` 发往 LogServer（`[self+0x44]`）。**原生零缓冲**——不存在无界列表。

LogServer 端点：配置键 `LogServerAddr`(0x794680)/`LogServerPort`(0x794698)，载入 0x793D25/0x793D47，
默认 127.0.0.1:10000（C# 已读入 `sLogServerAddr`/`nLogServerPort`，见 ServerConfig.cs:54-55，
但**无客户端使用**）。脚本侧另有 `AddLogRec(Cmd:word; ItemName; ItemId; ItemNum; Desc)`(0x72E0B6)。

## 3. out-of-scope 依据

`77BBAA33内部转发协议—完整规范.md` L204-205：
> 不需要实现的服务端内部CMD: 0x25,0xC4,0xCC,0xC8,0xD0,0xD4,0xD8,0xBC,0xC0,0xDC-0xF0
> 这些是原始M2Server/DBServer之间的内部同步, C#重写版不需要

日志记录长度/CMD 0xC4、体长 0xBC 均在其列。故 LogServer 196 字节传输**明确 out-of-scope**（DBSVR 邻近）。

## 4. 本轮修复（泄漏，不涉传输）

`M2Share.AppendBoundedLog`：`LogStringList`/`LogonCostLogList` 加 FIFO 上限
`LogRecordBufferCap=20000`，越限摊销裁到 3/4（避免逐条 RemoveAt 的 O(n^2)）。
原生该缓冲概念长度为 0（即发即清），封顶是最贴近的本地建模，消除无界增长。
审计探针用量为个位数（每次 Clear 后断言），远低于上限，不受影响。

## 5. 未来落地（若接入 LogServer）

1. 新增 LogServer socket 客户端（连 LogServerAddr:Port）。
2. `AddGameDataLog` 改为：组 196 字节 `TLogDataInfo`（§2 布局，magic 0x33AABB77）→ 即发即清，
   对齐原生零缓冲；失败/断线时才短暂入队（有界）。
3. 字段语义按 `sub_768BE0` 的玩家字段映射逐一钉死后填充。
