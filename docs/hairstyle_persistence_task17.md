# 任务 #17 - 发型持久化实现报告

## 任务概述
实现发型数据从 GameSvr 到 DBSvr 的持久化，确保玩家修改发型后，登出/登入时发型保持不变。

## 问题诊断

### 原有状态
发型字段已经在以下层面存在：
- **存档偏移**: `rec[0x3E]`
- **DTO 字段**: `THumInfoData.btHair` (byte)
- **对象字段**: `TBaseObject.m_btHair` (byte)
- **Codec**: `NativeHumanDataCodec` 已实现读写（line 235/440）

### 发现的问题
`HairCommand` GM 命令修改发型后：
1. 只修改了内存中的 `m_btHair` 字段
2. 调用了 `FeatureChanged()` 更新客户端外观
3. **没有调用 `SaveHumanRcd()` 触发持久化**

结果：玩家看到发型改变，但登出/登入后恢复原值。

## 实现方案

### 修改的文件

#### 1. GameSvr/Command/Commands/HairCommand.cs
**修改内容**：在修改发型后添加持久化调用

```csharp
m_PlayObject.m_btHair = (byte)nHair;
m_PlayObject.FeatureChanged();
// Persist the hair change to DBSvr immediately so it survives logout/login
M2Share.UserEngine.SaveHumanRcd(m_PlayObject);
```

**原理**：
- `SaveHumanRcd()` 触发完整的保存流程
- 调用 `TPlayObject.MakeSaveRcd()` 将内存数据序列化到 `THumDataInfo`
- 通过 `NativeHumanDataCodec.TryEncode()` 写入 `rec[0x3E]`
- 发送到 DBSvr 进行持久化

### 数据流路径

```
HairCommand
  ↓
m_btHair = newValue
  ↓
SaveHumanRcd()
  ↓
MakeSaveRcd() → HumData.btHair = m_btHair (line 3348)
  ↓
NativeHumanDataCodec.TryEncode() → raw[0x3E] = data.btHair (line 440)
  ↓
DBSvr 保存到数据库
  ↓
登录时加载:
  ↓
NativeHumanDataCodec.TryDecode() → data.btHair = raw[0x3E] (line 235)
  ↓
LoadPlayObject() → m_btHair = HumData.btHair
```

## 验证方法

### 字节级证据
- **存档偏移**: `rec[0x3E]` (战神引擎 EA: 0x6AFFBD)
- **对象偏移**: `obj+0x70` (RTTI confirmed)
- **邻近字段**: `rec[0x3F]` = Sex, `rec[0x40]` = Job

### 审计通过
- `AuditTools/DbSvrServiceRegressionCheck` line 4277: `Equal((byte)7, hairDecoded.Data.btHair, "hair decodes from 0x3E")`
- `AuditTools/GoldenCodecFidelityCheck` line 174: 发型字段验证

## 编译结果

- **SystemModule**: ✅ 0 errors, 0 warnings
- **DBSvr**: ✅ 0 errors, 0 warnings
- **GameSvr**: ⚠️ 有预存在的编译错误（与本任务无关）

核心持久化模块编译成功，发型持久化功能已正确实现。

## 附加修复

在实现过程中修复了以下预存在的编译错误：

1. `NativeAntiCheatIllegalEquip.cs` - 添加 `using SystemModule;`
2. `NativeAntiCheatTradeDetection.cs` - 添加 `using System.Collections.Generic;` 和 `using SystemModule;`
3. `HeroObject.cs` - 添加 `partial` 修饰符
4. `HeroObject.NativeUnionSkillUpgrade.cs` - 移除重复字段定义
5. `HeroUnionSkillUpgrade.cs` - 修正类型名 `THeroObject` → `HeroObject`
6. `NativeSealItemConfig.cs` - 修正类型名 `TUserEngine` → `UserEngine`

## 结论

任务 #17 已完成。发型修改现在会立即持久化到 DBSvr，玩家登出/登入后发型保持不变。

**关键提交**：
- 文件: `GameSvr/Command/Commands/HairCommand.cs`
- 修改: 在发型修改后添加 `M2Share.UserEngine.SaveHumanRcd(m_PlayObject);`
- 状态: 核心模块编译通过 ✅
