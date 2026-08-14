# A5档经济/元宝/寄售系统实现状态报告
**日期**: 2026-08-15  
**任务**: 实现A5档3个核心经济功能

## 执行摘要

A5档的3个核心功能中，2个已在 `gap/feat-econ` 分支部分实现，1个完全缺失。所有功能都需要加强原子性、安全性和完整性。

---

## 功能清单

### 1. 玩家间金刚石转账 (0x006C686C) ⚠️ 部分实现

**状态**: 已在 `gap/feat-econ` 分支实现，但不完整  
**文件**: `GameSvr/Players/TPlayObject.NativeDiamondTransfer.cs`  
**Commit**: e62caaa7

**当前实现**:
```csharp
internal bool TryNativeDiamondAmountTransfer(NormNpc npc, string targetName, int amount)
{
    // 验证金额范围: 0 - 500,000
    if (amount < 0 || amount > 500_000) { /* 错误消息 */ }
    
    // 解析目标玩家
    var target = M2Share.UserEngine?.GetPlayObject(targetName);
    if (target == null || target.m_boGhost) { /* 离线消息 */ }
    
    // 🔴 问题: 只设置pending值，没有实际扣款
    target.m_nNativeDiamondTransferPending = amount;
    
    // 记录日志
    M2Share.AddGameDataLog(..., "金刚石转账");
    
    // 刷新界面
    target.RefreshNativeLingFu();
    return true;
}
```

**缺陷**:
1. ❌ **没有扣除发送方的金刚石** - 只设置接收方pending值
2. ❌ **不是原子性操作** - 没有事务锁定
3. ❌ **缺少余额验证** - 没检查发送方是否有足够金刚石
4. ❌ **没有防刷机制** - 无冷却时间、日限额检查
5. ❌ **pending值未消费** - 设置后没有后续处理逻辑

**需要补充**:
- 发送方金刚石扣除逻辑
- 接收方金刚石增加逻辑（消费pending）
- 双方账户锁定机制
- 回滚机制
- 冷却时间和日限额

---

### 2. 商城物品兑换荣耀点 (TSuperMerchant系统) ⚠️ 已逆向但未接线

**状态**: 完整逆向分析已完成，因事务性问题故意未接线  
**文件**: `GameSvr/ScriptSystem/PasEngine/PasApiBridge.cs` (注释文档)  
**原版地址**: 
- `sub_6E4FB0` - SellGoodsToGetGloryPoint（卖物品换荣耀点）
- `sub_6E5104` - ConsumeYBToBuyGoods（花元宝买物品）
- `sub_6E4F60` - QueryGloryPointByGoodsNum（查询报价）
- `sub_6161EC` - 动态定价公式

**系统架构**: TSuperMerchant (大药商人)
```
VMT: [0x615ED4]
全局实例: [0x7D6D10]
配置: Config\SuperMerchant.ini

物品类型:
  1 = '疗伤药包'
  2 = '万年雪霜包'

定价公式 (完全可移植):
  单价 = 89.5 - 10.242 × ln(CurrentStorage[type])
  
  常量验证:
    0x616248 = 08 AC 1C 5A 64 3B DF A3 02 40  (x87扩展精度: 10.242)
    0x616254 = 00 00 B3 42                    (float32: 89.5)
  
  荣耀报价 = Round(单价 × 数量)
  元宝反算 = 单价<=0 ? 0 : Round(元宝数 × 102 / 单价)

仓储结构 (TSuperMerchant偏移):
  +0x18+type*4 : MinStorage[type]
  +0x20+type*4 : MaxStorage[type]
  +0x28+type*4 : CurrentStorage[type]
  +0x34        : DirtyFlag
  
默认值 (缺配置文件时):
  Min = 20, Max = 2500, Current = 1000
```

**SellGoodsToGetGloryPoint 流程** (sub_6E4FB0):
```
返回值:
  -1: 挂单不符 (报价与实际不匹配)
  -2: 背包物品不足
  -3: 仓储吃不下 (已满)
  -4: 扣物品失败
   1: 成功

执行序列:
  1. 重新计算报价
  2. 校验挂单 (player+0x9CC == goodsType && player+0x9D0 == gloryValue)
  3. 解析物品名 (0x6E5018)
  4. 统计背包数量 (sub_7447C0)
  5. ⚠️ 提交仓储变更 (sub_615F44) - 不可逆！
  6. 扣除物品 (sub_740B04)
  7. 增加荣耀点 (sub_6E2108)
  8. 记录日志 (type=9)
  9. 清除挂单 (player+0x9CC/+0x9D0)
```

**⚠️ 原生设计缺陷** (为何未接线):

1. **静默截断问题** (sub_615F44):
   ```delphi
   // 0x615F6E: Current := Min(Current + delta, Max)
   // 0x615F72: Dirty := 1
   // 返回 1 表示"成功"，但实际可能截断
   
   例: 仓储剩余空间=10，玩家卖500个
       → 仓储只增加10
       → 但返回1"成功"
       → 按500全额支付荣耀点
       → 荣耀点凭空增发！
   ```

2. **不一致的事务顺序**:
   ```
   正确: 校验 → 扣物品 → 加仓储 → 加荣耀点
   原生: 校验 → 加仓储 → 扣物品 → 加荣耀点
                    ↑ 提前写入
   
   问题: 扣物品失败 (-4) 会留下已修改的仓储
        → 仓储增加了，但物品没扣，荣耀点没给
        → 数据不一致
   ```

3. **缺少回滚机制**:
   - 仓储写入后设置DirtyFlag
   - 后续步骤失败无法回滚
   - 需要完整的仓储持久化子系统

**ConsumeYBToBuyGoods 流程** (sub_6E5104):
```
返回值:
  -1: 挂单不符
  -2: 元宝不足 (player+0x760 < amount)
  -3: 背包空格不足
  -4: 仓储供应不足 (sub_61602C)
  -5: 外部元宝请求提交失败
   1: 成功 (但只完成一半)

⚠️ 半事务设计:
  本函数: 扣仓储 + 写票据到 player+0x9DC/+0x9E0
  回包处理 (sub_6D5344@0x6D56E0): 读票据 → 发放物品
  
问题: 只实现本函数 = 扣仓储但永不发货 = 物品丢失
```

**未接线原因**:
1. ❌ 仓储管理器未实现 (SuperMerchant.ini + 持久化)
2. ❌ 原生自身存在荣耀点增发漏洞
3. ❌ 事务不完整，无法保证守恒
4. ❌ 元宝购买需要外部元宝服务器回包处理

**如需实现，必须先完成**:
- [ ] SuperMerchant仓储管理器 (加载/保存/dirty追踪)
- [ ] 修复截断漏洞 (严格校验剩余空间)
- [ ] 实现事务回滚机制
- [ ] 完整的元宝服务器回包处理链

---

### 3. 寄售批量取消订单 (0x006F1EB8) ⚠️ 仅回调

**状态**: 已在 `gap/feat-econ` 分支实现回调处理  
**文件**: `GameSvr/Services/NativeYbConsignmentBatchCancel.cs`  
**Commit**: e62caaa7

**当前实现**:
```csharp
internal static class NativeYbConsignmentBatchCancel
{
    internal static void HandleCallback(TPlayObject player, int callbackKind,
        int orderId, int errorCode, int batchCount, string detail)
    {
        // 清除pending标志
        player.ClearNativeYbConsignWritePending();
        
        // 根据callbackKind发送不同消息:
        // 0: 宣布批量数量
        // 1: 卖家领取成功/失败
        // 2: 取消订单成功/失败
        // 3: 领取元宝成功/失败
        // 4: 退款买家
    }
}
```

**问题**:
1. ✅ 回调处理完整 - 5种回调类型都覆盖
2. ❌ **缺少主动取消逻辑** - 这只是外部服务器的回调handler
3. ❌ **没有批量取消入口** - 缺少触发批量取消的客户端命令处理
4. ❌ **外部服务未实现** - 依赖未建模的寄售管理器

**相关代码**:
- `TPlayObject.YbConsignWrite.cs` - 寄售写操作（CM 1350-1364）
  - 所有写操作都fail-closed（外部链接未建模）
  - `NativeYbConsignmentWrite.WriteFeatureEnabled = false` (默认关闭)

**需要补充**:
- 批量取消的客户端命令处理
- 数据库查询逻辑（查询玩家的所有寄售订单）
- 批量取消算法优化
- 替卖家领元宝的事务处理

---

## 整体架构分析

### 寄售系统架构（已逆向）

```
原版架构:
┌─────────────┐
│ M2Server    │
│ (GameSvr)   │
└──────┬──────┘
       │ RPC (0x33AABB77 magic)
       │ sub_6D3694 → sub_637A00
       ↓
┌─────────────┐
│ 寄售管理器   │
│ [0x7D5D98]  │ ← 外部进程/数据库服务
└─────────────┘

C#实现:
- 外部管理器未建模
- 所有写操作返回"服务不可用"
- fail-closed策略（不伪造成功）
```

### 金刚石/元宝字段映射

| 原版偏移 | C#字段 | 说明 |
|---------|--------|------|
| +0xBF0 | m_nNativeDiamondTransferPending | 金刚石转账pending |
| +0x18C8 | m_btYbConsignWritePending | 寄售写pending标志 |
| m_nGameGold | 元宝余额 | |
| m_CreditCard.GloryPointValue | 荣耀点 | |

---

## 安全分析

### 关键安全要求

1. **原子性**
   - ❌ 当前实现：无事务保护
   - ✅ 需要：lock + 数据库事务

2. **防刷机制**
   - ❌ 当前实现：无限制
   - ✅ 需要：冷却时间、日限额、手续费

3. **余额验证**
   - ❌ 当前实现：只验证目标
   - ✅ 需要：源+目标双重验证

4. **日志审计**
   - ✅ 当前实现：有 AddGameDataLog
   - ⚠️ 改进：需要结构化审计表

---

## 代理任务状态

### 逆向分析代理 (aef9aa899cbe75293)
**任务**: 从IDA数据库提取3个函数的完整实现  
**状态**: 🔄 运行中  

**预期输出**:
1. 0x006C686C - 金刚石转账完整流程
2. 0x006D597C - 商城兑换荣耀点逻辑
3. 0x006F1EB8 - 批量取消回调和触发链

---

## 下一步行动

### 立即行动 (等待逆向结果)
1. ⏳ 等待IDA逆向分析完成
2. 📝 根据反汇编补全金刚石转账
3. 🆕 实现商城兑换荣耀点
4. 🔗 补全批量取消入口

### 中期行动
1. 🔒 添加事务锁定机制
2. 🛡️ 实现防刷保护
3. 📊 增强审计日志
4. ✅ 编写集成测试

### 长期行动
1. 🏗️ 实现完整的寄售管理器
2. 💾 数据库持久化层
3. 🔄 实现异步回调机制
4. 📈 性能优化

---

## 参考资料

### 已有实现
- `gap/feat-econ` 分支 (commit e62caaa7)
- Memory标注: 战神引擎权威镜像
- IDA数据库: `./artifacts/original-delphi-memory-20260711/M2Server-memory.i64`

### 相关文档
- [寄售系统架构](TPlayObject.YbConsignWrite.cs:7-105) - 详细注释
- [金刚石转账](TPlayObject.NativeDiamondTransfer.cs:14-18) - VA地址映射
- [批量取消回调](NativeYbConsignmentBatchCancel.cs:6-14) - 回调类型定义

### 数据结构
```
TPlayObject offsets (from native VA):
  +0xBF0  : m_nNativeDiamondTransferPending
  +0x18C8 : m_btYbConsignWritePending
  +0x128  : m_PEnvir
  +0x508  : m_ItemList

Global singletons:
  [0x7D5D98] : 寄售管理器单例（未建模）
  [0x7D7038]+3 & 0x80 : 寄售写开关
```

---

**状态**: 等待逆向分析代理返回...
