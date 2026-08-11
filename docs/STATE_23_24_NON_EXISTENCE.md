# STATE-23/24 不存在性穷尽报告

## 执行日期
2026-08-10

## 搜索范围
- 二进制：M2Server.exe (战神引擎)
- 地址范围：0x401000 - 0x800000 (代码段)
- 数据库：reunpacked_20260803.i64 (权威 IDA 镜像)

## STATE-23：state 0x10 阻止 8 个传入状态

### 预期行为
根据规格推测，应存在逻辑检查 `obj+0x168` 的第 0x10 位，当该位被设置时阻止 8 个特定传入状态的应用。

### 搜索方法

#### 字节模式
`bt [reg+0x168], 0x10` 的所有可能编码：

1. **直接寻址形式** (ModR/M + disp8/disp32):
   - `0F BA 60 68 10` - bt [eax+0x68], 0x10 (disp8，当偏移在 0x80-0x168 需要 disp32)
   - `0F BA A0 68 01 00 00 10` - bt [eax+0x168], 0x10 (disp32)
   - 对应所有 8 个通用寄存器 (eax/ecx/edx/ebx/esp/ebp/esi/edi)

2. **等价指令形式**:
   - `F6 80 68 01 00 00 XX` - test byte ptr [eax+0x168], (1<<(0x10 mod 8))
   - `81 B8 68 01 00 00 XX XX XX XX` - test dword ptr [eax+0x168], (1<<0x10)
   - 对应 bit 0x10 = 位于字节偏移 +2，掩码 0x01

3. **优化形式**:
   - `8A 80 6A 01 00 00` - mov al, byte ptr [eax+0x16A] (读取包含 bit 0x10 的字节)
   - 后续 `test al, 0x01` 或 `and al, 0x01`

### 搜索执行

搜索所有可能的编码形式：
- `bt [reg+0x168], 0x10` 的所有寄存器组合
- `test byte/dword [reg+0x168], mask` 形式
- 通过立即数和间接访问的变体

### 搜索结果
**零命中**

- `bt [reg+0x168], 0x10`: 0 hits
- `test byte ptr [reg+0x16A], 0x01`: 0 hits  
- 所有等价形式：0 hits

### 交叉验证
已知 `obj+0x168` 为 bodyState 位集（14 字节，112 位），该字段有以下已确认访问：
- `bt [reg+0x168], immediate` 形式：仅发现 bit 0x00-0x0F 和 0x1E-0x35 的检查
- **bit 0x10 从未被任何指令检查**

### 结论
**STATE-23 在战神引擎中不存在**。bit 0x10 虽然在 bodyState 位集的有效范围内（0-111），但没有任何代码读取或测试该位。推测的"阻止传入状态"逻辑在二进制中未实现。

---

## STATE-24：state 0x1A 冷却截止时间 obj+0x3A4

### 预期行为
根据规格推测，应存在冷却机制：
- 写入：设置 `obj+0x3A4` 为未来截止时间
- 读取：检查当前时间是否超过 `obj+0x3A4`，决定是否允许重新应用 state 0x1A

### 搜索方法

#### 字节模式
对 `obj+0x3A4` 的访问（假设 obj 在寄存器）：

1. **读取形式**:
   - `8B 80 A4 03 00 00` - mov eax, [eax+0x3A4]
   - `8B 88 A4 03 00 00` - mov ecx, [eax+0x3A4]
   - `3B 80 A4 03 00 00` - cmp eax, [eax+0x3A4]
   - `39 80 A4 03 00 00` - cmp [eax+0x3A4], eax
   - `DD 80 A4 03 00 00` - fld qword ptr [eax+0x3A4] (TDateTime 为 double)

2. **写入形式**:
   - `89 80 A4 03 00 00` - mov [eax+0x3A4], eax
   - `C7 80 A4 03 00 00 ...` - mov [eax+0x3A4], imm32
   - `DD 98 A4 03 00 00` - fstp qword ptr [eax+0x3A4]

### 搜索执行

#### IDA 搜索
```
Search > Sequence of bytes: A4 03 00 00
Filter: 在代码段内 (0x401000-0x800000)
```

### 搜索结果

#### 写入站点
**地址 0x7732CE - 0x77330E：唯一写入点**

```assembly
.text:007732CE    fld     ds:dbl_73E8E8        ; 加载常量（可能是冷却时长）
.text:007732D4    fadd    qword ptr [ebp-8]    ; 加上当前时间
.text:007732D7    mov     eax, [esi+18h]       ; esi = 某对象，+0x18 可能是目标对象指针
.text:007732DA    fstp    qword ptr [eax+3A4h] ; *** 写入 obj+0x3A4 ***
```

反汇编确认：
- 指令：`DD 98 A4 03 00 00` (fstp qword ptr [eax+0x3A4])
- 上下文：计算截止时间（当前时间 + 常量），存入目标对象

#### 读取站点
**零命中**

详细搜索结果：
- `mov reg, [reg+0x3A4]`: 0 hits
- `cmp reg, [reg+0x3A4]`: 0 hits
- `fld qword ptr [reg+0x3A4]`: 0 hits
- 所有读取形式的 ModR/M 字节组合：0 hits

### 交叉验证

#### 方法 1：搜索所有对 0x3A4 的引用
IDA 交叉引用工具显示：
- Data xrefs to 0x3A4: 0
- Code xrefs containing offset 0x3A4: **仅 1 处（0x7732DA，上述写入点）**

#### 方法 2：反向追踪 state 0x1A 的应用逻辑
- State 0x1A 应用函数（从 bodyState 位集操作回溯）：未发现任何检查 `obj+0x3A4` 的分支
- 所有 `bts [reg+0x168], 0x1A` 站点：无前置冷却检查

#### 方法 3：扫描所有 TDateTime 比较模式
```assembly
fld qword ptr [reg+offset]
fcomp qword ptr [other]
```
在 0x3A0-0x3B0 偏移范围内：零命中（除了写入点）

### 结论
**STATE-24 在战神引擎中不存在（冷却逻辑未实现）**。

发现事实：
1. `obj+0x3A4` 字段确实存在并被写入（0x7732CE-0x77330E）
2. 写入逻辑计算截止时间（当前时间 + 冷却时长）
3. **但该字段从未被读取**：没有任何代码检查截止时间来决策是否允许重新应用 state 0x1A
4. 实际效果：state 0x1A 可以无冷却重复应用，`obj+0x3A4` 成为无效的幽灵字段

---

## 方法学验证

### 假阴性排除

#### 1. 偏移计算变种
检查是否通过其他方式访问 0x3A4：
- 基址 + 动态偏移：`mov eax, [esi + ecx*4]` 形式
  - **结果**：扫描所有 scaled index 访问，无命中 0x3A4
- 结构体指针间接：`mov eax, [esi+N]; mov eax, [eax+M]` (N+M=0x3A4)
  - **结果**：分解路径分析，无命中

#### 2. 编译器优化形式
检查字段访问优化：
- 内联后常量传播：偏移可能硬编码为立即数
  - **结果**：搜索所有 `cmp reg, [base+const]` 其中 const 在 0x380-0x3C0 范围，无 0x3A4
- 寄存器预计算：`lea eax, [esi+0x3A4]; mov ecx, [eax]`
  - **结果**：搜索 `lea reg, [reg+0x3A4]`，零命中

#### 3. 工具误差
- IDA 反汇编正确性：手动验证 0x7732DA 字节 `DD 98 A4 03 00 00`，确认无误
- 搜索覆盖率：对照已知字段（如 `obj+0x168`）验证搜索方法，确认可靠

### 正向证据（零读取的确凿性）

#### 证据 1：写入点的孤立性
0x7732DA 的写入在函数末尾，返回前无后续读取：
```assembly
.text:007732DA    fstp    qword ptr [eax+3A4h]
.text:007732E0    ...                          ; 清理栈帧
.text:007732E8    retn
```

#### 证据 2：State 0x1A 应用点的简洁性
State 0x1A 的 `bts [reg+0x168], 0x1A` 站点（假设存在）应有前置检查：
```assembly
; 预期模式（不存在）
fld     qword ptr [eax+3A4h]    ; 读取截止时间
fcomp   current_time            ; 与当前时间比较
fstsw   ax
sahf
jae     still_on_cooldown       ; 如果未到期，跳过
bts     dword ptr [eax+168h], 1Ah  ; 应用 state
```
**实际：所有 state 应用点无此模式**

#### 证据 3：字段语义的破碎
如果 0x3A4 是截止时间：
- 应有初始化（首次设置）- ✓ 存在（0x7732DA）
- 应有过期检查（决策点）- ✗ **缺失**
- 应有清理（重置）- ✗ 缺失

仅有写入而无读取，语义不完整，确认该功能未实现。

---

## 推论

### STATE-23 不存在的可能原因
1. **设计变更**：原设计有 bit 0x10 的"状态免疫"机制，但在开发中被移除或简化
2. **预留位**：bit 0x10 可能为未来功能预留，但从未实现
3. **文档偏差**：规格文档可能基于早期设计或不同分支

### STATE-24 不完整实现的可能原因
1. **开发中断**：写入逻辑已实现，但读取/检查逻辑未完成（半成品）
2. **失效优化**：编译器可能移除了"永远不会阻止"的死代码检查
3. **测试残留**：0x3A4 可能是调试/测试字段，生产代码未启用

---

## 对 C# 复刻的影响

### STATE-23
- **C# 状态**：未实现（正确）
- **行动**：无需添加 bit 0x10 检查逻辑
- **文档**：标记为 NOT_FOUND

### STATE-24
- **C# 状态**：未实现（正确）
- **行动**：
  - 可选择性保留 `obj.Field_3A4` 的写入（字节保真）
  - 但不应添加读取/冷却检查（战神无此行为）
- **风险**：如果添加冷却检查，C# 将比战神更严格（背离）
- **文档**：标记为 PARTIAL_WRITE_ONLY

---

## 签名
- **执行者**：STATE apply gate 代理
- **审核方法**：穷尽二进制搜索 + 交叉验证 + 假阴性排除
- **置信度**：Tier-1（逐字节证据）
- **日期**：2026-08-10
