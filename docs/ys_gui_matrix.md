# 眼神 2.0.8 GUI 功能总表

生成脚本 `tools/ys_gui_matrix.py`；配置 `D:\光头卧龙\mud2.0\Mir200\Gs1\config.json`（gbk，380 键）。

| 状态 | 计数 |
|---|---|
| IMPLEMENTED | 222 |
| SCRIPT_ONLY | 19 |
| LABEL_ONLY | 138 |
| MISSING | 1 |
| INVENTED | 1 |

生产已开启（值 != 0）215 键；其中无行为的 47 键。

## 生产已开启但 C# 无行为

| 键 | 值 | 页面 | 状态 | 串 VA | xref | 插件补丁目标 VA |
|---|---|---|---|---|---|---|
| `获取玩家对象函数` | 1 | 扩展/脚本相关 | MISSING | `0x102b063c` | 2 | `0x646f40` `0x647d24` `0x736b28` `0x736ef8` |
| `修复刺杀位麻痹` | 1 | 盘古1 | LABEL_ONLY | `0x102b03fc` | 2 | - |
| `全服击杀提示` | 1 | 盘古1 | LABEL_ONLY | `0x102b0384` | 2 | - |
| `召唤骷髅_数量` | 1 | 盘古1 | LABEL_ONLY | `0x102b0a30` | 2 | - |
| `屏蔽属性提升提示` | 1 | 盘古1 | LABEL_ONLY | `0x102b043c` | 2 | `0x741a21` `0x741a5c` `0x741a97` `0x741ad2` `0x741b0d` `0x741b48` `0x741b83` `0x741bbe` `0x741bf9` `0x741c34` `0x741c6f` `0x741caa` `0x741ce5` `0x741d20` `0x741d5b` `0x741dfd` `0x74281d` `0x742835` `0x74284d` `0x742865` `0x74287d` `0x742895` `0x7428ad` `0x7428c5` `0x7428dd` `0x74290d` `0x742925` `0x74293d` `0x742955` `0x74296d` `0x74298c` |
| `摆摊地图` | "3" | 盘古1 | LABEL_ONLY | `0x102b0a6c` | 3 | - |
| `摆摊穿人` | 1 | 盘古1 | LABEL_ONLY | `0x102b0354` | 2 | `0x77931d` |
| `盘古击杀触发` | 1 | 盘古1 | LABEL_ONLY | `0x102b004c` | 2 | - |
| `盘古杀死宝宝` | 1 | 盘古1 | LABEL_ONLY | `0x102b02f4` | 2 | - |
| `盘古给与封号` | 1 | 盘古1 | LABEL_ONLY | `0x102b0304` | 2 | - |
| `盘古高级属性` | 1 | 盘古1 | LABEL_ONLY | `0x102b0748` | 2 | `0x6ba718` `0x6ba72d` `0x6f9ab0` |
| `神兽_数量` | 1 | 盘古1 | LABEL_ONLY | `0x102b0a24` | 2 | - |
| `脚本控制头发外显` | 1 | 盘古1 | LABEL_ONLY | `0x102b0538` | 2 | `0x740f85` |
| `邮件防刷` | 1 | 盘古1 | LABEL_ONLY | `0x102b03b0` | 2 | `0x6e7810` |
| `限制摆摊_右x` | 340 | 盘古1 | LABEL_ONLY | `0x102b0778` | 3 | - |
| `限制摆摊_右y` | 340 | 盘古1 | LABEL_ONLY | `0x102b0788` | 3 | - |
| `限制摆摊_左x` | 280 | 盘古1 | LABEL_ONLY | `0x102b0758` | 3 | - |
| `限制摆摊_左y` | 328 | 盘古1 | LABEL_ONLY | `0x102b0768` | 3 | - |
| `限制摆摊_等级` | 20 | 盘古1 | LABEL_ONLY | `0x102b0798` | 3 | - |
| `随身仓库` | 1 | 盘古1 | LABEL_ONLY | `0x102b0360` | 2 | `0x6c2ab9` `0x6c2dc9` `0x6e087c` |
| `ServerSay函数` | 1 | 盘古2 | LABEL_ONLY | `0x102b0034` | 2 | `0x728913` |
| `名字变色` | 1 | 盘古2 | LABEL_ONLY | `0x102b0264` | 2 | - |
| `攻城修改` | 1 | 盘古2 | LABEL_ONLY | `0x102b059c` | 2 | `0x65bc09` `0x65be2c` `0x65c3b1` |
| `攻城修改_天数` | 3 | 盘古2 | LABEL_ONLY | `0x102b0a78` | 3 | - |
| `攻城修改_小时` | 20 | 盘古2 | LABEL_ONLY | `0x102b0a88` | 3 | - |
| `攻城时长_分钟` | 120 | 盘古2 | LABEL_ONLY | `0x102b0aa8` | 3 | - |
| `火墙_时间` | 120 | 盘古2 | LABEL_ONLY | `0x102b0ab8` | 3 | - |
| `等级禁言` | 1 | 盘古2 | LABEL_ONLY | `0x102b1688` | 2 | - |
| `施毒术_公式值` | "10" | 盘古3 | LABEL_ONLY | `0x102b08d8` | 3 | - |
| `无极真气` | 1 | 盘古3 | LABEL_ONLY | `0x102b06d0` | 2 | `0x74587c` |
| `无极真气_A值` | "10" | 盘古3 | LABEL_ONLY | `0x102b08a8` | 3 | - |
| `无极真气_时间` | "10" | 盘古3 | LABEL_ONLY | `0x102b08b8` | 3 | - |
| `装备吸血` | 1 | 盘古3 | LABEL_ONLY | `0x102b0630` | 2 | `0x76e2a3` |
| `主号高级暴击` | 1 | 眼神2(第1页) | LABEL_ONLY | `0x102b1578` | 2 | - |
| `技能等级突破` | 1 | 眼神2(第1页) | LABEL_ONLY | `0x102b0210` | 2 | - |
| `技能等级突破_最大值` | 255 | 眼神2(第1页) | LABEL_ONLY | `0x102b0a40` | 2 | - |
| `中毒飘血` | 1 | 配置2 | LABEL_ONLY | `0x102affe4` | 2 | `0x767e10` |
| `免毒符` | 1 | 配置2 | LABEL_ONLY | `0x102affc8` | 2 | `0x6ed945` `0x6ed9d1` `0x6ed9eb` `0x6eda85` `0x6edab1` `0x6edadf` `0x6edb11` `0x6edb3e` `0x6edb65` `0x6edb8c` `0x6edc58` `0x6ede1d` |
| `删除技能不提示` | 1 | 配置2 | LABEL_ONLY | `0x102afff0` | 2 | `0x6c7797` |
| `双毒时间_最低` | 5 | 配置2 | LABEL_ONLY | `0x102b0a08` | 3 | - |
| `禁止发言不提示` | 1 | 配置2 | LABEL_ONLY | `0x102b0010` | 2 | `0x6bb5cd` `0x6bb625` `0x6c94a9` |
| `红毒_A` | 1 | 配置2 | LABEL_ONLY | `0x102b09ec` | 3 | - |
| `红毒_B` | 1 | 配置2 | LABEL_ONLY | `0x102b09f4` | 3 | - |
| `绿毒_A` | 1 | 配置2 | LABEL_ONLY | `0x102b09dc` | 3 | - |
| `绿毒_B` | 10 | 配置2 | LABEL_ONLY | `0x102b09e4` | 3 | - |
| `绿毒_最低` | 5 | 配置2 | LABEL_ONLY | `0x102b09fc` | 3 | - |
| `魔法盾修正` | 1 | 配置2 | LABEL_ONLY | `0x102affb0` | 2 | - |

## 按页面

### 盘古1（51）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `下线宝宝死亡` | 0 |  | toggle | IMPLEMENTED | GameSvr/Players/TPlayObject.Message.cs |
| `专职变性` | 0 |  | toggle | LABEL_ONLY | IsChangeJob; IsChangeJobEnabled |
| `修复刺杀位麻痹` | 1 | Y | toggle | LABEL_ONLY | IsFixStabParalysis |
| `修复卡防御` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.NativeMagicDamage.cs |
| `全服击杀提示` | 1 | Y | toggle | LABEL_ONLY | IsKillNotice; ShouldSendKillNotice |
| `关闭摆摊` | 0 |  | toggle | IMPLEMENTED | GameSvr/Players/TPlayObject.NativeStall.cs |
| `召唤神兽` | 0 |  | toggle | IMPLEMENTED | GameSvr/Spells/MagicManager.cs |
| `召唤神兽触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `召唤骷髅` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Spells/MagicManager.cs |
| `召唤骷髅_数量` | 1 | Y | value | LABEL_ONLY | NativeSlaveCountImm8 |
| `召唤骷髅触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `回城按钮触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `土城摆摊` | 0 |  | toggle | LABEL_ONLY | IsTuChengStall |
| `地面物品消失时间` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.ViewRange.cs |
| `地面物品消失时间_时间` | 150 | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.ViewRange.cs |
| `安全区禁止丢物` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Actors/HeroObject.cs; GameSvr/Players/TPlayObject.Operate.cs |
| `屏蔽元宝增减信息` | 0 |  | toggle | LABEL_ONLY | IsHideGoldMsg; ShouldHideGoldMsg |
| `屏蔽元宝数据库日志` | 0 |  | toggle | LABEL_ONLY | IsHideGoldLog; ShouldHideGoldLog |
| `屏蔽发言频繁禁言功能` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Players/TPlayObject.Chat.cs |
| `屏蔽属性提升提示` | 1 | Y | toggle | LABEL_ONLY | IsHideAttrUp; ShouldHideAttrUp |
| `心灵启示触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `指定地图编号摆摊` | 0 |  | toggle | LABEL_ONLY | IsMapStall |
| `挖矿触发` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `摆摊地图` | "3" | Y | value | LABEL_ONLY | GetStallMapId; MapStallMap |
| `摆摊穿人` | 1 | Y | toggle | LABEL_ONLY | IsStallPass; IsStallPassThrough |
| `攻沙脚本控制` | 0 |  | toggle | LABEL_ONLY | IsSiegeScript; IsSiegeScriptEnabled |
| `死亡触发` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `盘古击杀触发` | 1 | Y | toggle | LABEL_ONLY | IsPgKillTrigger |
| `盘古杀死宝宝` | 1 | Y | toggle | LABEL_ONLY | IsPgKillPet |
| `盘古物理攻击触发` | 0 |  | toggle | LABEL_ONLY | IsPgPhysTrigger |
| `盘古穿戴触发` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `盘古给与封号` | 1 | Y | toggle | LABEL_ONLY | IsPgGiveTitle; IsPgGiveTitleEnabled |
| `盘古高级属性` | 1 | Y | toggle | LABEL_ONLY | IsPgAdvancedAttr; IsPgAdvancedAttrEnabled |
| `盘古魔法攻击触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.Wave3.cs; GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `神兽_序号` | 0 |  | value | LABEL_ONLY | ShenShouIdx |
| `神兽_数量` | 1 | Y | value | LABEL_ONLY | NativeSlaveCountImm8 |
| `禁止交易地图` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Players/TPlayObject.Operate.cs |
| `禁止宝宝休息` | 0 |  | toggle | IMPLEMENTED | GameSvr/Command/Commands/ChangeSalveStatusCommand.cs |
| `穿人穿怪` | 0 |  | toggle | LABEL_ONLY | IsPassThrough |
| `脚本控制头发外显` | 1 | Y | toggle | LABEL_ONLY | IsScriptHair; IsScriptHairEnabled |
| `行会显示` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Players/TPlayObject.Base.cs; GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `踢玩家下线` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `邮件防刷` | 1 | Y | toggle | LABEL_ONLY | IsMailAntiSpam; IsMailAntiSpamEnabled |
| `防0拆分` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Players/TPlayObject.PileItems.cs |
| `限制摆摊` | 0 |  | toggle | LABEL_ONLY | IsLimitStall; IsStallAllowed |
| `限制摆摊_右x` | 340 | Y | value | LABEL_ONLY | LimitStall_RightX |
| `限制摆摊_右y` | 340 | Y | value | LABEL_ONLY | LimitStall_RightY |
| `限制摆摊_左x` | 280 | Y | value | LABEL_ONLY | LimitStall_LeftX |
| `限制摆摊_左y` | 328 | Y | value | LABEL_ONLY | LimitStall_LeftY |
| `限制摆摊_等级` | 20 | Y | value | LABEL_ONLY | LimitStall_Level |
| `随身仓库` | 1 | Y | toggle | LABEL_ONLY | IsPortableStorage; IsPortableStorageEnabled |

### 盘古2（48）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `ServerSay函数` | 1 | Y | toggle | LABEL_ONLY | IsServerSay; IsServerSayEnabled |
| `SetNoKillMapLv脚本触发` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Command/Commands/SetNoKillMapLvCommand.cs |
| `删除英雄技能` | 0 |  | toggle | LABEL_ONLY | IsDelHeroSkill; IsDelHeroSkillEnabled |
| `刺杀剑术` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `刺杀剑术_A值` | "1" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `刺杀剑术_B值` | "5" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `半月带毒` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenPoisonKeys.cs |
| `半月弯刀` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `半月弯刀_A值` | "2" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `半月弯刀_B值` | "15" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `名字变色` | 1 | Y | toggle | LABEL_ONLY | IsNameColor |
| `噬魂沼泽绿毒修复` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenPoisonKeys.cs |
| `基本剑术` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.cs |
| `基本剑术_n值` | "3" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.cs |
| `复活戒指重设` | 0 |  | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.NativeRevive.cs |
| `复活戒指重设_无敌时间` | 0 |  | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.NativeRevive.cs |
| `复活戒指重设_重设时间` | 0 |  | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.NativeRevive.cs |
| `攻城修改` | 1 | Y | toggle | LABEL_ONLY | IsSiegeModify |
| `攻城修改_分钟` | 0 |  | value | LABEL_ONLY | GetSiegeModMinute; IsSiegeModMinute |
| `攻城修改_天数` | 3 | Y | value | LABEL_ONLY | GetSiegeModDay; IsSiegeModDay |
| `攻城修改_小时` | 20 | Y | value | LABEL_ONLY | GetSiegeModHour; IsSiegeModHour |
| `攻城时长_分钟` | 120 | Y | value | LABEL_ONLY | GetSiegeDuration; IsSiegeDuration |
| `攻杀剑术` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `攻杀剑术_A值` | "5" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `武器绿毒` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenPoisonKeys.cs |
| `法师群毒` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenPoisonKeys.cs |
| `火墙_时间` | 120 | Y | value | LABEL_ONLY | FireWallTime |
| `火墙设置时间上限` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `烈火剑法` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `烈火剑法_A值` | "4" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `烈火剑法_B值` | "3" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `物功带毒` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenPoisonKeys.cs |
| `盘古冰咆哮的范围` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `盘古冰咆哮的范围_范围值` | 3 | Y | value | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `盘古地狱雷光范围` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `盘古地狱雷光范围_范围值` | 3 | Y | value | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `盘古流星火雨范围` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `盘古流星火雨范围_范围值` | 4 | Y | value | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `盘古爆裂火焰范围` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `盘古爆裂火焰范围_范围值` | 2 | Y | value | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `破复活` | 0 |  | toggle | LABEL_ONLY | IsBreakRevival |
| `等级禁言` | 1 | Y | toggle | LABEL_ONLY | IsLevelMute; IsMailAntiSpamEnabled |
| `设置玩家称号函数` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `逐日剑法` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `逐日剑法_A值` | "6" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `逐日剑法_B值` | "3" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `野蛮麻痹` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `雷电带毒` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenPoisonKeys.cs |

### 盘古3（39）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `中毒时间上限` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenPoisonTimeCap.cs |
| `中毒时间上限_秒` | "60" | Y | value | IMPLEMENTED | GameSvr/Plugins/YanshenPoisonTimeCap.cs |
| `人物爆率调整` | 0 |  | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.Base.cs; GameSvr/Players/TPlayObject.Message.cs |
| `人物等级1_值` | 40 | Y | value | IMPLEMENTED | GameSvr/Spells/MagicManager.cs |
| `人物等级2_值` | 45 | Y | value | IMPLEMENTED | GameSvr/Spells/MagicManager.cs |
| `人物等级3_值` | 48 | Y | value | IMPLEMENTED | GameSvr/Spells/MagicManager.cs |
| `修改召唤神兽` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Spells/MagicManager.cs |
| `屏蔽排行榜` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenHideRank.cs |
| `怪物名字1_值` | "强化神兽" | Y | value | IMPLEMENTED | GameSvr/Spells/MagicManager.cs |
| `怪物名字2_值` | "强化神兽" | Y | value | IMPLEMENTED | GameSvr/Spells/MagicManager.cs |
| `怪物名字3_值` | "白虎" | Y | value | IMPLEMENTED | GameSvr/Spells/MagicManager.cs |
| `怪物数量1_值` | 1 | Y | value | IMPLEMENTED | GameSvr/Spells/MagicManager.cs |
| `怪物数量2_值` | 2 | Y | value | IMPLEMENTED | GameSvr/Spells/MagicManager.cs |
| `怪物数量3_值` | 1 | Y | value | IMPLEMENTED | GameSvr/Spells/MagicManager.cs |
| `战士合击` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenComboTables.cs |
| `战士合击_数值1` | "1.5" | Y | value | IMPLEMENTED | GameSvr/Plugins/YanshenComboTables.cs |
| `战士合击_数值2` | "2" | Y | value | IMPLEMENTED | GameSvr/Plugins/YanshenComboTables.cs |
| `战士合击_数值3` | "2.4" | Y | value | IMPLEMENTED | GameSvr/Plugins/YanshenComboTables.cs |
| `战士合击_数值4` | "2.6" | Y | value | IMPLEMENTED | GameSvr/Plugins/YanshenComboTables.cs |
| `战士合击_数值5` | "2.8" | Y | value | IMPLEMENTED | GameSvr/Plugins/YanshenComboTables.cs |
| `施毒术` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `施毒术_公式值` | "10" | Y | value | LABEL_ONLY | PoisonFormulaVal |
| `无极真气` | 1 | Y | toggle | LABEL_ONLY | IsZhenQi |
| `无极真气_A值` | "10" | Y | value | LABEL_ONLY | ZhenQiA |
| `无极真气_时间` | "10" | Y | value | LABEL_ONLY | ZhenQiTime |
| `最大装备数量` | "" |  | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Base.cs; GameSvr/Players/TPlayObject.Message.cs |
| `法道合击` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenComboTables.cs |
| `法道合击_数值1` | "" |  | value | IMPLEMENTED | GameSvr/Plugins/YanshenComboTables.cs |
| `法道合击_数值2` | "" |  | value | IMPLEMENTED | GameSvr/Plugins/YanshenComboTables.cs |
| `法道合击_数值3` | "" |  | value | IMPLEMENTED | GameSvr/Plugins/YanshenComboTables.cs |
| `法道合击_数值4` | "" |  | value | IMPLEMENTED | GameSvr/Plugins/YanshenComboTables.cs |
| `法道合击_数值5` | "" |  | value | IMPLEMENTED | GameSvr/Plugins/YanshenComboTables.cs |
| `红名K值` | "" |  | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Base.cs; GameSvr/Players/TPlayObject.Message.cs |
| `脚本控制人物爆率` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenScriptDropRate.cs |
| `装备吸血` | 1 | Y | toggle | LABEL_ONLY | IsEquipSteal |
| `装备提升人物爆率` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenEquipDropBoost.cs |
| `装备提升人物爆率_A值` | "10" | Y | value | IMPLEMENTED | GameSvr/Plugins/YanshenEquipDropBoost.cs |
| `装备提升人物爆率_B值` | "10" | Y | value | IMPLEMENTED | GameSvr/Plugins/YanshenEquipDropBoost.cs |
| `非红名K值` | "" |  | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Base.cs; GameSvr/Players/TPlayObject.Message.cs |

### 盘古4（98）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `头盔属性几率_准确_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔属性几率_攻击_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔属性几率_攻速_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔属性几率_道术_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔属性几率_魔法_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔最随机性_极品_值` | "80" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔最高点数_准确_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔最高点数_攻击_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔最高点数_攻速_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔最高点数_道术_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔最高点数_魔法_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔点数几率_准确_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔点数几率_攻击_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔点数几率_攻速_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔点数几率_道术_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `头盔点数几率_魔法_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `屏蔽自动绑定` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `戒指属性几率_准确_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指属性几率_攻击_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指属性几率_攻速_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指属性几率_道术_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指属性几率_魔法_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指最随机性_极品_值` | "80" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指最高点数_准确_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指最高点数_攻击_值` | "" |  | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指最高点数_攻速_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指最高点数_道术_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指最高点数_魔法_值` | "" |  | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指点数几率_准确_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指点数几率_攻击_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指点数几率_攻速_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指点数几率_道术_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `戒指点数几率_魔法_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯属性几率_准确_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯属性几率_攻击_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯属性几率_攻速_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯属性几率_道术_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯属性几率_魔法_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯最随机性_极品_值` | "80" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯最高点数_准确_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯最高点数_攻击_值` | "" |  | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯最高点数_攻速_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯最高点数_道术_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯最高点数_魔法_值` | "" |  | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯点数几率_准确_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯点数几率_攻击_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯点数几率_攻速_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯点数几率_道术_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `手镯点数几率_魔法_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器属性几率_准确_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器属性几率_攻击_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器属性几率_攻速_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器属性几率_道术_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器属性几率_魔法_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器最随机性_极品_值` | "80" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器最高点数_准确_值` | "7" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器最高点数_攻击_值` | "" |  | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器最高点数_攻速_值` | "7" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器最高点数_道术_值` | "7" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器最高点数_魔法_值` | "7" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器点数几率_准确_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器点数几率_攻击_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器点数几率_攻速_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器点数几率_道术_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `武器点数几率_魔法_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服属性几率_准确_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服属性几率_攻击_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服属性几率_攻速_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服属性几率_道术_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服属性几率_魔法_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服最随机性_极品_值` | "80" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服最高点数_准确_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服最高点数_攻击_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服最高点数_攻速_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服最高点数_道术_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服最高点数_魔法_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服点数几率_准确_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服点数几率_攻击_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服点数几率_攻速_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服点数几率_道术_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `衣服点数几率_魔法_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `随机极品` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链属性几率_准确_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链属性几率_攻击_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链属性几率_攻速_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链属性几率_道术_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链属性几率_魔法_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链最随机性_极品_值` | "80" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链最高点数_准确_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链最高点数_攻击_值` | "3" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链最高点数_攻速_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链最高点数_道术_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链最高点数_魔法_值` | "2" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链点数几率_准确_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链点数几率_攻击_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链点数几率_攻速_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链点数几率_道术_值` | "5" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |
| `项链点数几率_魔法_值` | "10" | Y | value | IMPLEMENTED | GameSvr/Items/NativeItemPlus28.cs |

### 配置1（33）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `AddLimLF函数修改` | 0 |  | toggle | LABEL_ONLY | IsAddLimLF; IsAddLimLFModified |
| `BB杀怪触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `BB死亡触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `IncActivePoint函数修改` | 0 |  | toggle | LABEL_ONLY | IsIncActivePoint; IsIncActivePointModified |
| `give极品` | 0 |  | toggle | LABEL_ONLY | IsGiveExtreme; IsGiveExtremeEnabled |
| `上线触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `临时大背包` | 0 |  | toggle | LABEL_ONLY | IsTempBag; SwitchBigBag |
| `全屏拾取` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `刀刀切割` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenCommands.cs; GameSvr/Plugins/YanshenTriggerDispatch.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `千分比免伤` | 0 |  | toggle | LABEL_ONLY | IsDmgReduction |
| `复活戒指改cd` | 0 |  | toggle | LABEL_ONLY | IsReviveCD; IsReviveCDEnabled |
| `复活戒指概率` | 0 |  | toggle | LABEL_ONLY | IsReviveChance; IsReviveChanceEnabled |
| `复活触发脚本` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `捡物触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.Wave2.cs; GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `攻击反伤` | 0 |  | toggle | LABEL_ONLY | IsReflectEnabled |
| `攻击触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.Wave3.cs; GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `新倍攻和暴击` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `新穿戴触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `永久属性` | 0 |  | toggle | LABEL_ONLY | IsPermAttr |
| `永久攻速` | 0 |  | toggle | LABEL_ONLY | IsPermSpeed |
| `特殊宝宝` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `特殊属性` | 0 |  | toggle | LABEL_ONLY | IsPetSpecial |
| `禁止装备自动绑定` | 0 |  | toggle | LABEL_ONLY | IsBindDisabled |
| `移动速度` | 0 |  | toggle | LABEL_ONLY | IsMoveSpeed |
| `英雄倍攻和暴击` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.Wave2.cs; GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `英雄攻速移速` | 0 |  | toggle | LABEL_ONLY | IsHeroSpeed; IsHeroSpeedEnabled |
| `英雄施法速度` | 0 |  | toggle | LABEL_ONLY | IsHeroCastSpeed |
| `英雄穿戴触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `被击杀触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.Wave2.cs; GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `装备来源` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `读取英雄装备` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `魔法攻击触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.Wave3.cs; GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `麻痹概率` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |

### 配置2（31）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `中毒飘血` | 1 | Y | toggle | LABEL_ONLY | IsPoisonBleed |
| `免毒符` | 1 | Y | toggle | LABEL_ONLY | IsAntiPoison |
| `冰咆哮主属性切换` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `冰咆哮范围` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `删除技能不提示` | 1 | Y | toggle | LABEL_ONLY | IsDelSkillSilent; ShouldDelSkillSilent |
| `升级技能不提示` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Command/Commands/ChgHeroSkillCommand.cs; GameSvr/Command/Commands/TrainingMagicCommand.cs |
| `双毒时间_最低` | 5 | Y | value | LABEL_ONLY | DualPoisonMin |
| `嗜血术倍数` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `地狱雷光可换主属性` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `地狱雷光系数` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `地狱雷光范围` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `激光命中概率` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenLaserSlots.cs |
| `激光电影可换主属性` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `激光范围及系数` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenLaserSlots.cs; GameSvr/Plugins/YanshenSkillPatches.cs |
| `火球主属性切换` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `火球自定义范围` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `火雨主属切换` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `爆裂火焰可换主属性` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `爆裂火焰范围及系数` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `禁止发言不提示` | 1 | Y | toggle | LABEL_ONLY | IsBanChatSilent; ShouldBanChatSilent |
| `红毒_A` | 1 | Y | value | LABEL_ONLY | IsRedPoisonA |
| `红毒_B` | 1 | Y | value | LABEL_ONLY | IsRedPoisonB |
| `绿毒_A` | 1 | Y | value | LABEL_ONLY | IsGreenPoisonA |
| `绿毒_B` | 10 | Y | value | LABEL_ONLY | GreenPoisonB |
| `绿毒_最低` | 5 | Y | value | LABEL_ONLY | GreenPoisonMin |
| `群毒` | 0 |  | toggle | LABEL_ONLY | IsGroupPoison |
| `群毒值` | 0 |  | toggle | LABEL_ONLY | IsGroupPoisonVal; IsGroupPoisonValEnabled |
| `野蛮等级` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `雷电主属性切换` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `雷电自定义范围` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `魔法盾修正` | 1 | Y | toggle | LABEL_ONLY | IsMagicShieldFix |

### 眼神2(第1页)（34）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `主号全局法速` | 0 |  | value | LABEL_ONLY | IsMainGlobalSpeed; IsMainGlobalSpeedEnabled |
| `主号分身术a` | 0 |  | toggle | LABEL_ONLY | IsMainClone; IsMainCloneEnabled |
| `主号施法速度` | 0 |  | toggle | LABEL_ONLY | IsMainCastSpeed; IsMainCastSpeedEnabled |
| `主号高级暴击` | 1 | Y | toggle | LABEL_ONLY | IsMainAdvCrit; IsMainAdvCritEnabled |
| `全屏吸怪` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `冰咆哮切割` | 0 |  | toggle | LABEL_ONLY | IsIceStormCutting |
| `冰咆哮固定增伤` | 0 |  | toggle | LABEL_ONLY | IsIceStormFixDmg |
| `切换暴击报文` | 0 |  | toggle | LABEL_ONLY | IsSwitchCritMsg; IsSwitchCritMsgEnabled |
| `嗜血术范围` | 0 |  | toggle | LABEL_ONLY | IsBloodRange |
| `宝宝自动叛变` | 0 |  | toggle | LABEL_ONLY | IsPetAutoRebel; IsPetAutoRebelEnabled |
| `战队职业限制` | 0 |  | toggle | LABEL_ONLY | IsGamePartnerLimit; IsGamePartnerLimitEnabled |
| `技能等级突破` | 1 | Y | toggle | LABEL_ONLY | IsLevelBreak |
| `技能等级突破_最大值` | 255 | Y | value | LABEL_ONLY | IsLevelBreakMax; IsLevelBreakMaxEnabled |
| `技能触发脚本` | 0 |  | toggle | LABEL_ONLY | IsSkillTrigger |
| `新呼唤宝宝` | 0 |  | toggle | LABEL_ONLY | IsNewCallPet; IsNewCallPetEnabled |
| `火墙切割` | 0 |  | toggle | LABEL_ONLY | IsFireWallCutting |
| `火墙固定增伤` | 0 |  | toggle | LABEL_ONLY | IsFireWallFixDmg |
| `火符切割` | 0 |  | toggle | LABEL_ONLY | IsAmuletCutting |
| `火符固定增伤` | 0 |  | toggle | LABEL_ONLY | IsAmuletFixDmg |
| `烈火切割` | 0 |  | toggle | LABEL_ONLY | IsFireCutting |
| `烈火固定增伤` | 0 |  | toggle | LABEL_ONLY | IsFireFixDmg |
| `穿戴触发_plus` | 0 |  | toggle | LABEL_ONLY | IsWearPlusTrigger |
| `自定义伤害` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `自定义元素` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `英雄千分比免伤` | 0 |  | toggle | LABEL_ONLY | IsHeroDmgReduction; IsHeroDmgReductionEnabled |
| `英雄自动开盾` | 0 |  | toggle | LABEL_ONLY | IsHeroAutoShield |
| `英雄读取极品` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `获取沙城归属` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `装备多职业` | 0 |  | toggle | LABEL_ONLY | IsMultiJob; IsMultiJobEquip |
| `装备转生穿戴判定a` | 0 |  | toggle | LABEL_ONLY | IsRebirthWear; IsRebirthWearCheck |
| `角色多阵营` | 0 |  | toggle | LABEL_ONLY | IsMultiFaction; IsMultiFactionEnabled |
| `诱惑之光触发脚本a` | 0 |  | toggle | LABEL_ONLY | IsLureTrigger |
| `雷电术切割` | 0 |  | toggle | LABEL_ONLY | IsLightningCutting |
| `高级英雄倍功暴击` | 0 |  | toggle | LABEL_ONLY | IsHeroAdvancedPowerCrit; IsHeroAdvancedPowerCritEnabled |

### 眼神2(第2页)（26）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `super攻击触发` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `伤害触发脚本_plus` | 0 |  | toggle | LABEL_ONLY | IsDmgScriptPlus |
| `全局循环函数` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenCommands.cs; GameSvr/Plugins/YanshenRecycleDriver.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `千分比经验倍数` | 0 |  | toggle | LABEL_ONLY | IsExpMultiplier |
| `循环时间_值` | 2000 | Y | value | IMPLEMENTED | GameSvr/Plugins/YanshenRecycleDriver.cs |
| `怪物爆率A_值` | 0 |  | value | LABEL_ONLY | IsMonsterDropA |
| `怪物爆率B_值` | 0 |  | value | LABEL_ONLY | IsMonsterDropB |
| `怪物爆率K_值` | 0 |  | value | LABEL_ONLY | IsMonsterDropK |
| `攻击吸血` | 0 |  | toggle | IMPLEMENTED | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs; GameSvr/Services/NativeType2StdItemSnapshotState.cs |
| `新怪物爆率` | 0 |  | toggle | LABEL_ONLY | IsNewMonsterDrop; IsNewMonsterDropEnabled |
| `毫秒级cd记录` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `火墙不吸血` | 0 |  | toggle | LABEL_ONLY | IsFireWallNoVamp |
| `眼神特殊函数` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Players/TPlayObject.NativeSocialSlots.cs; GameSvr/Plugins/YanshenCommands.cs; GameSvr/Plugins/YanshenPoisonKeys.cs |
| `自定义伤害_plus` | 0 |  | toggle | IMPLEMENTED | GameSvr/Players/TPlayObject.NativeSocialSlots.cs; GameSvr/Plugins/YanshenCommands.cs; GameSvr/Plugins/YanshenPoisonKeys.cs |
| `英雄物理攻击触发` | 0 |  | toggle | LABEL_ONLY | IsHeroPhysTrigger; IsHeroPhysTriggerEnabled |
| `英雄野蛮` | 0 |  | toggle | LABEL_ONLY | IsHeroBarbarian; IsHeroBarbarianEnabled |
| `英雄魔法攻击触发` | 0 |  | toggle | LABEL_ONLY | IsHeroMagicTrigger; IsHeroMagicTriggerEnabled |
| `道士合击系数` | 0 |  | toggle | LABEL_ONLY | IsTaoComboFactor; IsTaoComboFactorEnabled |
| `道士合击系数_数值1` | "" |  | value | LABEL_ONLY | TaoComboV1 |
| `道士合击系数_数值2` | "" |  | value | LABEL_ONLY | TaoComboV2 |
| `道士合击系数_数值3` | "" |  | value | LABEL_ONLY | TaoComboV3 |
| `道士合击系数_数值4` | "" |  | value | LABEL_ONLY | TaoComboV4 |
| `道士合击系数_数值5` | "" |  | value | LABEL_ONLY | TaoComboV5 |
| `高级回收` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Players/TPlayObject.MemberRoster.cs; GameSvr/Players/TPlayObject.Message.cs; GameSvr/Plugins/YanshenCommands.cs |
| `高级物理攻击触发` | 0 |  | toggle | LABEL_ONLY | IsAdvancedPhysTrigger |
| `高级魔法攻击触发` | 0 |  | toggle | LABEL_ONLY | IsAdvancedMagicTrigger |

### 扩展/技能相关（9）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `多元伤害` | 0 |  | toggle | LABEL_ONLY | IsMultiDmg |
| `星耀专属切割a` | 0 |  | toggle | LABEL_ONLY | IsXyCutting |
| `星耀攻击反伤a` | 0 |  | toggle | LABEL_ONLY | IsXyReflect |
| `概率格挡a` | 0 |  | toggle | LABEL_ONLY | IsProbBlock |
| `自定义召唤怪物a` | 0 |  | toggle | LABEL_ONLY | IsCustomCall; IsCustomCallEnabled |
| `雷电术自定义伤害` | 0 |  | toggle | LABEL_ONLY | IsLightningCustom |
| `雷电术自定义伤害_系数A` | 0 |  | toggle | LABEL_ONLY | IsLightningCustomA |
| `雷电术自定义伤害_系数B` | 0 |  | toggle | LABEL_ONLY | IsLightningCustomB |
| `麻痹中不被麻痹a` | 0 |  | toggle | LABEL_ONLY | IsParaImmune |

### 扩展/物品相关（4）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `大背包` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `投保报文` | 0 |  | toggle | LABEL_ONLY | IsInsuranceMsg; SendInsuranceMsg |
| `英雄修装备a` | 0 |  | toggle | LABEL_ONLY | IsHeroRepairEquip; RepairAllEquip |
| `装备投保` | 0 |  | toggle | LABEL_ONLY | GiveItemBind; InsuranceItem; IsEquipInsurance |

### 扩展/脚本相关（5）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `护身触发报文a` | 0 |  | toggle | LABEL_ONLY | IsHolyShieldMsg |
| `护身触发概率a` | 0 |  | toggle | LABEL_ONLY | IsHolyShieldChance |
| `星耀倍功与暴击a` | 0 |  | toggle | LABEL_ONLY | IsXyPowerCrit |
| `格位刺杀免伤a` | 0 |  | toggle | LABEL_ONLY | IsLuckBlock |
| `获取玩家对象函数` | 1 | Y | toggle | MISSING | - |

### 扩展/角色相关（2）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `宝宝叛变属性a` | 0 |  | toggle | LABEL_ONLY | IsPetRebelAttr; IsPetRebelAttrEnabled |
| `宠物吸血a` | 0 |  | toggle | LABEL_ONLY | IsPetVampire; IsPetVampireEnabled |

## 生产 config 无但转储里有（合法，非臆造）

- `怪物伤害触发技能特效` @ `0x102bcfdc` — GameSvr/Plugins/YanshenApi.cs:2118, GameSvr/Plugins/YanshenApi.cs:2147
- `指定英雄放技能` @ `0x102b9048` — GameSvr/Plugins/YanshenApi.cs:2185
- `火墙修改` @ `0x102bd07c` — GameSvr/Plugins/YanshenApi.cs:4048

## INVENTED（生产 config 和转储字符串里都没有）

- `道士合击系数_数值` — GameSvr/Plugins/YanshenFixedReplicaPanels.cs:484
