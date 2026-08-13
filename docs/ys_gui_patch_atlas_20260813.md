# 眼神 2.0.8 -> M2Server patch atlas

memcpy primitive 0x10033340(src,len,va,va) ; immediate store = inline VirtualProtect + mov [va],reg
features: 121   memcpy sites: 306   immediate sites: 145

| feature | memcpy targets (VA/len) | immediate targets (VA) |
|---|---|---|
| <unlabelled> | - | 0x65b6dc 0x65bbfe 0x65bdf6 0x65be25 0x65c6b2 0x73c4fa 0x73fcc9 0x7608f0 0x76090f 0x760914 0x760920 0x760934 0x760939 0x760945 0x760959 0x76095e 0x76096a 0x76097e 0x760983 0x76098f 0x7609c7 0x7609cc 0x7609d8 0x7611fb 0x761226 0x76122b 0x761237 0x761258 0x76125d 0x761269 0x76127b 0x761280 0x76128c 0x76129e 0x7612a3 0x7612af 0x7612c1 0x7612c6 0x7612d2 0x7617a3 0x7617d1 0x7617d6 0x7617e2 0x7617f4 0x7617f9 0x761805 0x761817 0x76181c 0x761828 0x76183a 0x76183f 0x76184b 0x76185d 0x761862 0x76186e 0x761d1d 0x761d22 0x761d2e 0x761d40 0x761d45 0x761d51 0x761d63 0x761d68 0x761d74 0x761d86 0x761d8b 0x761d97 0x761da9 0x761dae 0x7625e8 0x762615 0x76261a 0x762626 0x762638 0x76263d 0x762649 0x76265b 0x762660 0x76266c 0x76267e 0x762683 0x76268f 0x7626a1 0x7626a6 0x7626b2 0x7639f3 0x76f86d 0x76f8a4 0x783f55 0x783f5a 0x783f66 0x783f78 0x783f7d 0x783f89 0x783f9b 0x783fa0 0x783fac 0x783fbe 0x783fc3 0x783fcf 0x783fe1 0x783fe6 0x783ff2 0x7d3278 0x7d327c 0x7d3280 0x7d3284 0x7d3288 0x7d328c 0x7d3290 0x7d3294 0x7d33fc 0x7d3400 0x7d3404 0x7d3408 0x7d340c 0x7d3410 0x7d3414 0x7d3418 |
| AddLimLF函数修改 | 0x6de8e3/8 | - |
| BB杀怪触发 | 0x71f467/5 | - |
| BB死亡触发 | 0x76631c/5 | - |
| IncActivePoint函数修改 | 0x6f91ba/6 | - |
| ServerSay函数 | 0x728913/12 | - |
| give极品 | 0x6c89ae/8 | - |
| 上线触发 | 0x6548bd/5 | - |
| 下线宝宝死亡 | 0x6b5ba1/6 | - |
| 中毒时间上限 | 0x76e5ce/5 0x76e675/5 | - |
| 中毒飘血 | 0x767e10/6 | - |
| 人物爆率调整 | - | 0x73fcbb 0x73ff6c |
| 修复卡防御 | 0x767910/1 | - |
| 免毒符 | 0x6ed945/4 0x6ed9d1/6 0x6ed9eb/10 0x6eda85/2 0x6edab1/2 0x6edadf/2 0x6edb11/6 0x6edb3e/2 0x6edb65/2 0x6edb8c/2 0x6edc58/2 0x6ede1d/2 | - |
| 全屏拾取 | 0x6b795c/12 0x6b796b/12 0x6b7a25/6 0x6b7a2f/6 | - |
| 关闭摆摊 | 0x6e7c38/1 | - |
| 冰咆哮主属性切换 | 0x76f2cb/14 | - |
| 冰咆哮范围 | 0x76f300/9 | - |
| 刀刀切割 | 0x767bae/6 | - |
| 删除技能不提示 | 0x6c7797/5 | - |
| 刺杀剑术 | 0x771c25/2 | 0x771c50 0x771d24 |
| 升级技能不提示 | 0x73f5ee/6 | - |
| 半月带毒 | 0x7720fb/6 | - |
| 半月弯刀 | - | 0x772046 0x772148 |
| 召唤神兽 | 0x76ee99/1 0x76eeec/4 | - |
| 召唤神兽触发 | 0x6edc5e/5 | - |
| 召唤骷髅 | 0x76ee1f/1 | - |
| 召唤骷髅触发 | 0x6edb44/5 | - |
| 嗜血术倍数 | 0x76fc2b/6 | - |
| 噬魂沼泽绿毒修复 | 0x691e2e/6 | - |
| 回城按钮触发 | 0x6dbb80/5 | - |
| 土城摆摊 | 0x6e7930/8 0x6e7c1c/10 0x6e7c5f/2 | - |
| 地狱雷光可换主属性 | 0x76f5fb/14 | - |
| 地狱雷光系数 | 0x76f61a/5 | - |
| 地狱雷光范围 | 0x76f63d/9 | - |
| 地面物品消失时间 | 0x77a3ff/4 | - |
| 基本剑术 | 0x76af96/3 0x76afa9/6 | - |
| 复活戒指改cd | 0x73c47a/5 0x73c4f2/6 0x743751/5 | - |
| 复活戒指概率 | 0x74373a/7 | - |
| 复活戒指重设 | - | 0x73c480 0x743758 |
| 复活触发脚本 | 0x73c484/6 | - |
| 安全区禁止丢物 | 0x73cc98/6 | - |
| 屏蔽元宝增减信息 | 0x6f8288/3 0x6f82c5/3 0x6f8b25/3 0x6f8b62/3 0x6f8bcf/7 0x6f8c02/7 0x6f9002/2 0x6f9047/2 | - |
| 屏蔽元宝数据库日志 | 0x70f6dc/1 | - |
| 屏蔽发言频繁禁言功能 | 0x6bb56a/6 0x6bb579/6 | - |
| 屏蔽属性提升提示 | 0x741a21/5 0x741a5c/5 0x741a97/5 0x741ad2/5 0x741b0d/5 0x741b48/5 0x741b83/5 0x741bbe/5 0x741bf9/5 0x741c34/5 0x741c6f/5 0x741caa/5 0x741ce5/5 0x741d20/5 0x741d5b/5 0x741dfd/5 0x74281d/9 0x742835/9 0x74284d/9 0x742865/9 0x74287d/9 0x742895/9 0x7428ad/9 0x7428c5/9 0x7428dd/9 0x74290d/9 0x742925/9 0x74293d/9 0x742955/9 0x74296d/9 0x74298c/9 | - |
| 屏蔽排行榜 | 0x6cba88/1 | - |
| 屏蔽自动绑定 | 0x74dc57/1 0x74dd59/1 0x74ddc3/1 0x74ddef/1 | - |
| 心灵启示触发 | 0x6edc2b/5 | - |
| 战士合击 | - | 0x7d341c 0x7d3420 |
| 指定地图编号摆摊 | 0x6e7930/4 0x6e7930/8 0x6e7934/None 0x6e7c1c/10 0x6e7c5f/2 | - |
| 挖矿触发 | 0x6ec111/5 | - |
| 捡物触发 | 0x6b770c/5 | - |
| 摆摊穿人 | 0x77931d/3 | - |
| 攻击反伤 | 0x767bb4/5 | - |
| 攻击触发 | 0x76e35d/5 | - |
| 攻城修改 | - | 0x65bc09 0x65be2c 0x65c3b1 |
| 攻杀剑术 | - | 0x76b02d |
| 攻沙脚本控制 | 0x65c6b6/6 0x65c76d/2 0x65c785/2 | - |
| 新倍攻和暴击 | 0x76c88b/5 | - |
| 新穿戴触发 | 0x75ea37/5 0x75f085/7 | - |
| 施毒术 | 0x76e599/31 | - |
| 无极真气 | - | 0x74587c |
| 武器绿毒 | 0x76e2bc/7 | - |
| 死亡触发 | 0x6c09b5/5 | - |
| 永久属性 | 0x73d9cf/8 0x73d9d7/9 0x73d9e0/9 0x73d9e9/9 0x73d9f2/9 0x73d9fb/9 0x73da04/9 0x73da16/9 0x73da1f/9 0x73da28/9 0x73da31/9 0x73da3a/9 | - |
| 永久攻速 | 0x73d9a0/11 | - |
| 法师群毒 | 0x76e1a9/6 | - |
| 法道合击 | - | 0x7d3298 0x7d329c |
| 激光命中概率 | 0x76ea14/5 | - |
| 激光电影可换主属性 | 0x76e9b5/14 | - |
| 激光范围及系数 | 0x76ea07/6 | - |
| 火墙设置时间上限 | 0x7706b6/6 | - |
| 火球主属性切换 | 0x76e3e6/14 | - |
| 火球自定义范围 | 0x76e425/9 | - |
| 火雨主属切换 | 0x76f365/14 | - |
| 烈火剑法 | 0x76b0ec/3 0x77231d/2 | 0x76b0f0 |
| 爆物随机极品 | - | 0x761cf0 0x761dba |
| 爆裂火焰可换主属性 | 0x76f23b/14 | - |
| 爆裂火焰范围及系数 | 0x76f26b/7 | - |
| 物功带毒 | 0x76e2bc/7 | - |
| 特殊宝宝 | 0x7671a2/6 0x76de42/10 | - |
| 特殊属性 | 0x6e41bd/5 0x73d951/7 | - |
| 盘古冰咆哮的范围 | - | 0x76f301 |
| 盘古地狱雷光范围 | - | 0x76f643 |
| 盘古流星火雨范围 | - | 0x76f3be |
| 盘古爆裂火焰范围 | - | 0x76f271 |
| 盘古穿戴触发 | 0x6d8e35/5 0x6d8e4d/5 | - |
| 盘古高级属性 | 0x6ba718/5 0x6ba72d/20 0x6ba72d/23 0x6f9ab0/43 | - |
| 盘古魔法攻击触发 | 0x76dec0/7 0x76e1af/7 | - |
| 禁止交易地图 | 0x6c3f00/5 | - |
| 禁止发言不提示 | 0x6bb5cd/6 0x6bb625/6 0x6c94a9/6 | - |
| 禁止宝宝休息 | 0x623a73/7 | - |
| 禁止装备自动绑定 | 0x784351/7 | - |
| 移动速度 | 0x73d983/7 | - |
| 穿人穿怪 | 0x6b30a3/10 0x768454/3 | - |
| 脚本控制人物爆率 | 0x6df2cc/9 0x73d578/7 0x73dac5/6 | - |
| 脚本控制头发外显 | 0x740f85/5 | - |
| 英雄倍攻和暴击 | 0x76c816/7 | - |
| 英雄攻速移速 | 0x73da43/9 | - |
| 英雄施法速度 | 0x68dd60/5 | - |
| 英雄穿戴触发 | 0x75ea31/6 0x75f08c/7 | - |
| 获取玩家对象函数 | 0x646f40/72 0x647d24/84 0x736b28/9 0x736ef8/12 | - |
| 行会显示 | 0x6c5bcb/2 0x6c5bf7/2 | - |
| 被击杀触发 | 0x766624/5 | - |
| 装备吸血 | 0x76e2a3/6 | - |
| 装备提升人物爆率 | 0x71fd37/6 | - |
| 装备来源 | 0x6c8aaa/5 0x71fe90/5 | - |
| 设置玩家称号函数_支持80字符 | 0x6df754/69 | - |
| 读取英雄装备 | 0x6e04e7/5 | - |
| 逐日剑法 | 0x76b13e/2 0x76b14c/6 | 0x76b14d 0x771da4 |
| 邮件防刷 | 0x6e7810/5 | - |
| 野蛮等级 | 0x768f67/7 | - |
| 野蛮麻痹 | 0x6bc9e2/5 | - |
| 防0拆分 | 0x6e0ff3/6 | - |
| 随身仓库 | 0x6c2ab9/6 0x6c2dc9/6 0x6e087c/45 | - |
| 雷电主属性切换 | 0x76ea8a/14 | - |
| 雷电带毒 | 0x76eb1d/7 | - |
| 雷电自定义范围 | 0x76eb06/9 | - |
| 魔法攻击触发 | 0x76de84/6 | - |
| 麻痹概率 | 0x76e2d2/5 | - |


## per-site detail (memcpy)


### AddLimLF函数修改
- `0x100d3a30` (fn `0x100cebd0`) -> `0x6de8e3` len 8  new `8D45F8E8`  dumped `8D45F8E8156CD2FF`  label `AddLimLF函数修改(未启动)`

### BB杀怪触发
- `0x100d4945` (fn `0x100cebd0`) -> `0x71f467` len 5  new `5E5B595D`  dumped `5E5B595DC3`  label `BB杀怪触发(未启动)`

### BB死亡触发
- `0x100d4cb7` (fn `0x100cebd0`) -> `0x76631c` len 5  new `558BEC53`  dumped `558BEC5356`  label `BB死亡触发(未启动)`

### IncActivePoint函数修改
- `0x100d3b55` (fn `0x100cebd0`) -> `0x6f91ba` len 6  new `0190E40A`  dumped `0190E40A0000`  label `IncActivePoint函数修改(未启动)`

### ServerSay函数
- `0x100b4d4c` (fn `0x100b1fc0`) -> `0x728913` len 12  new `81E2FFFF`  dumped `83FA057749FF24951F897200`  label `ServerSay函数(已启动)`
- `0x100b4de1` (fn `0x100b1fc0`) -> `0x728913` len 12  new `83FA0577`  dumped `83FA057749FF24951F897200`  label `ServerSay函数(未启动)`

### give极品
- `0x100d3e12` (fn `0x100cebd0`) -> `0x6c89ae` len 8  new `8B55F4E8`  dumped `8B55F4E89E540800`  label `give极品(未启动)`

### 上线触发
- `0x100d5a94` (fn `0x100cebd0`) -> `0x6548bd` len 5  new `5F5E5B8B`  dumped `5F5E5B8BE5`  label `上线触发(未启动)`

### 下线宝宝死亡
- `0x100ab10b` (fn `0x100a96c0`) -> `0x6b5ba1` len 6  new `E9A60600`  dumped `0F84A5060000`  label `下线宝宝死亡(已启动)`
- `0x100ab19b` (fn `0x100a96c0`) -> `0x6b5ba1` len 6  new `0F84A506`  dumped `0F84A5060000`  label `下线宝宝死亡(未启动)`

### 中毒时间上限
- `0x100b88b5` (fn `0x100b7f40`) -> `0x76e675` len 5  new `8B45F850`  dumped `8B45F85053`  label `中毒时间上限(待重设)`
- `0x100b88f0` (fn `0x100b7f40`) -> `0x76e5ce` len 5  new `518BD352`  dumped `518BD35250`  label `中毒时间上限(待重设)`

### 中毒飘血
- `0x100db388` (fn `0x100d8810`) -> `0x767e10` len 6  new `29B3AC02`  dumped `29B3AC020000`  label `中毒飘血(未启动)`

### 修复卡防御
- `0x100aaebd` (fn `0x100a96c0`) -> `0x767910` len 1  new `EB`  dumped `7E`  label `修复卡防御(已启动)`
- `0x100aaf41` (fn `0x100a96c0`) -> `0x767910` len 1  new `7E`  dumped `7E`  label `修复卡防御(未启动)`

### 免毒符
- `0x100da719` (fn `0x100d8810`) -> `0x6ed945` len 4  new `EB769090`  dumped `C645FA01`  label `免毒符(已启动)`
- `0x100da756` (fn `0x100d8810`) -> `0x6ed9d1` len 6  new `90909090`  dumped `EB183C027514`  label `免毒符(已启动)`
- `0x100da793` (fn `0x100d8810`) -> `0x6ed9eb` len 10  new `90909090`  dumped `8B55F48BC3E823F20400`  label `免毒符(已启动)`
- `0x100da7d0` (fn `0x100d8810`) -> `0x6eda85` len 2  new `9090`  dumped `7413`  label `免毒符(已启动)`
- `0x100da80d` (fn `0x100d8810`) -> `0x6edab1` len 2  new `9090`  dumped `7415`  label `免毒符(已启动)`
- `0x100da84a` (fn `0x100d8810`) -> `0x6edadf` len 2  new `9090`  dumped `7415`  label `免毒符(已启动)`
- `0x100da884` (fn `0x100d8810`) -> `0x6edb11` len 6  new `90909090`  dumped `0F8434050000`  label `免毒符(已启动)`
- `0x100da8c1` (fn `0x100d8810`) -> `0x6edb3e` len 2  new `9090`  dumped `740E`  label `免毒符(已启动)`
- `0x100da8fe` (fn `0x100d8810`) -> `0x6edb65` len 2  new `9090`  dumped `740E`  label `免毒符(已启动)`
- `0x100da93b` (fn `0x100d8810`) -> `0x6edb8c` len 2  new `9090`  dumped `7415`  label `免毒符(已启动)`
- `0x100da975` (fn `0x100d8810`) -> `0x6edc58` len 2  new `9090`  dumped `740E`  label `免毒符(已启动)`
- `0x100da9af` (fn `0x100d8810`) -> `0x6ede1d` len 2  new `9090`  dumped `7411`  label `免毒符(已启动)`
- `0x100dab1e` (fn `0x100d8810`) -> `0x6ed945` len 4  new `C645FA01`  dumped `C645FA01`  label `免毒符(未启动)`
- `0x100dab55` (fn `0x100d8810`) -> `0x6ed9d1` len 6  new `EB183C02`  dumped `EB183C027514`  label `免毒符(未启动)`
- `0x100dab92` (fn `0x100d8810`) -> `0x6ed9eb` len 10  new `8B55F48B`  dumped `8B55F48BC3E823F20400`  label `免毒符(未启动)`
- `0x100dabcc` (fn `0x100d8810`) -> `0x6eda85` len 2  new `7413`  dumped `7413`  label `免毒符(未启动)`
- `0x100dac06` (fn `0x100d8810`) -> `0x6edab1` len 2  new `7415`  dumped `7415`  label `免毒符(未启动)`
- `0x100dac40` (fn `0x100d8810`) -> `0x6edadf` len 2  new `7415`  dumped `7415`  label `免毒符(未启动)`
- `0x100dac77` (fn `0x100d8810`) -> `0x6edb11` len 6  new `0F843405`  dumped `0F8434050000`  label `免毒符(未启动)`
- `0x100dacb1` (fn `0x100d8810`) -> `0x6edb3e` len 2  new `740E`  dumped `740E`  label `免毒符(未启动)`
- `0x100daceb` (fn `0x100d8810`) -> `0x6edb65` len 2  new `740E`  dumped `740E`  label `免毒符(未启动)`
- `0x100dad28` (fn `0x100d8810`) -> `0x6edb8c` len 2  new `7415`  dumped `7415`  label `免毒符(未启动)`
- `0x100dad62` (fn `0x100d8810`) -> `0x6edc58` len 2  new `740E`  dumped `740E`  label `免毒符(未启动)`
- `0x100dad9f` (fn `0x100d8810`) -> `0x6ede1d` len 2  new `7411`  dumped `7411`  label `免毒符(未启动)`

### 全屏拾取
- `0x100cf23f` (fn `0x100cebd0`) -> `0x6b795c` len 12  new `3B982C01`  dumped `3B982C0100000F85ED000000`  label `全屏拾取(未启动)`
- `0x100cf280` (fn `0x100cebd0`) -> `0x6b796b` len 12  new `3BB03001`  dumped `3BB0300100000F85DE000000`  label `全屏拾取(未启动)`
- `0x100cf2c1` (fn `0x100cebd0`) -> `0x6b7a25` len 6  new `8B803001`  dumped `8B8030010000`  label `全屏拾取(未启动)`
- `0x100cf302` (fn `0x100cebd0`) -> `0x6b7a2f` len 6  new `8B882C01`  dumped `8B882C010000`  label `全屏拾取(未启动)`

### 关闭摆摊
- `0x100ad12a` (fn `0x100a96c0`) -> `0x6e7c38` len 1  new `C3`  dumped `55`  label `关闭摆摊(已关闭)`
- `0x100ad1ae` (fn `0x100a96c0`) -> `0x6e7c38` len 1  new `55`  dumped `55`  label `关闭摆摊(未关闭)`

### 冰咆哮主属性切换
- `0x100d9f4e` (fn `0x100d8810`) -> `0x76f2cb` len 14  new `8BBB9402`  dumped `8BBB9402000003D78B8B98020000`  label `冰咆哮主属性切换(未启动)`
- `0x100da095` (fn `0x100d8810`) -> `0x76f2cb` len 14  new `8BBB9402`  dumped `8BBB9402000003D78B8B98020000`  label `冰咆哮主属性切换(未启动)`

### 冰咆哮范围
- `0x100da2f3` (fn `0x100d8810`) -> `0x76f300` len 9  new `6A016A03`  dumped `6A016A03A038F37600`  label `冰咆哮范围(未启动)`

### 刀刀切割
- `0x100cf496` (fn `0x100cebd0`) -> `0x767bae` len 6  new `53565789`  dumped `535657894DF8`  label `刀刀切割(未启动)`

### 删除技能不提示
- `0x100db451` (fn `0x100d8810`) -> `0x6c7797` len 5  new `E9810000`  dumped `6850786C00`  label `删除技能不提示(已启动)`
- `0x100db542` (fn `0x100d8810`) -> `0x6c7797` len 5  new `6850786C`  dumped `6850786C00`  label `删除技能不提示(未启动)`

### 刺杀剑术
- `0x100b417c` (fn `0x100b1fc0`) -> `0x771c25` len 2  new `EB17`  dumped `7517`  label `刺杀剑术(已重设)`

### 升级技能不提示
- `0x100db61c` (fn `0x100d8810`) -> `0x73f5ee` len 6  new `EB3A9090`  dumped `57687CF67300`  label `升级技能不提示(已启动)`
- `0x100db70f` (fn `0x100d8810`) -> `0x73f5ee` len 6  new `57687CF6`  dumped `57687CF67300`  label `升级技能不提示(未启动)`

### 半月带毒
- `0x100b2ef8` (fn `0x100b1fc0`) -> `0x7720fb` len 6  new `8B8BAC00`  dumped `8B8BAC000000`  label `半月带毒(未启动)`

### 召唤神兽
- `0x100a9e9b` (fn `0x100a96c0`) -> `0x76ee99` len 1  new `None`  dumped `01`  label `召唤神兽(已启动)`
- `0x100a9ed5` (fn `0x100a96c0`) -> `0x76eeec` len 4  new `D4C2C1E9`  dumped `C9F1CADE`  label `召唤神兽(已启动)`
- `0x100a9f6b` (fn `0x100a96c0`) -> `0x76ee99` len 1  new `01`  dumped `01`  label `召唤神兽(未启动)`
- `0x100a9fa5` (fn `0x100a96c0`) -> `0x76eeec` len 4  new `C9F1CADE`  dumped `C9F1CADE`  label `召唤神兽(未启动)`

### 召唤神兽触发
- `0x100ae668` (fn `0x100a96c0`) -> `0x6edc5e` len 5  new `E8191208`  dumped `E819120800`  label `召唤神兽触发(未启动)`

### 召唤骷髅
- `0x100aa04b` (fn `0x100a96c0`) -> `0x76ee1f` len 1  new `None`  dumped `01`  label `召唤骷髅(已启动)`
- `0x100aa0ea` (fn `0x100a96c0`) -> `0x76ee1f` len 1  new `01`  dumped `01`  label `召唤骷髅(未启动)`

### 召唤骷髅触发
- `0x100ae438` (fn `0x100a96c0`) -> `0x6edb44` len 5  new `E8B31208`  dumped `E8B3120800`  label `召唤骷髅触发(未启动)`

### 嗜血术倍数
- `0x100da5c9` (fn `0x100d8810`) -> `0x76fc2b` len 6  new `FF93CC00`  dumped `FF93CC000000`  label `嗜血术倍数(未启动)`

### 噬魂沼泽绿毒修复
- `0x100b2516` (fn `0x100b1fc0`) -> `0x691e2e` len 6  new `8BCF8BD6`  dumped `8BCF8BD68BC3`  label `噬魂沼泽绿毒修复(未启动)`

### 回城按钮触发
- `0x100ad768` (fn `0x100a96c0`) -> `0x6dbb80` len 5  new `E8E7D601`  dumped `E8E7D60100`  label `回城按钮触发(未启动)`

### 土城摆摊
- `0x100a981e` (fn `0x100a96c0`) -> `0x6e7930` len 8  new `01000000`  dumped `0300000047413000`  label `土城摆摊(已启动)`
- `0x100a9858` (fn `0x100a96c0`) -> `0x6e7c1c` len 10  new `C7EBD4DA`  dumped `C7EBD4DAD7AFD4B0C4DA`  label `土城摆摊(已启动)`
- `0x100a9895` (fn `0x100a96c0`) -> `0x6e7c5f` len 2  new `EB1D`  dumped `751D`  label `土城摆摊(已启动)`
- `0x100a994b` (fn `0x100a96c0`) -> `0x6e7930` len 8  new `03000000`  dumped `0300000047413000`  label `土城摆摊(未启动)`
- `0x100a9985` (fn `0x100a96c0`) -> `0x6e7c1c` len 10  new `C7EBD4DA`  dumped `C7EBD4DAD7AFD4B0C4DA`  label `土城摆摊(未启动)`
- `0x100a99c2` (fn `0x100a96c0`) -> `0x6e7c5f` len 2  new `751D`  dumped `751D`  label `土城摆摊(未启动)`

### 地狱雷光可换主属性
- `0x100d9164` (fn `0x100d8810`) -> `0x76f5fb` len 14  new `8BBB9402`  dumped `8BBB9402000003D78B8B98020000`  label `地狱雷光可换主属性(未启动)`

### 地狱雷光系数
- `0x100d8ae8` (fn `0x100d8810`) -> `0x76f61a` len 5  new `B90A0000`  dumped `B90A000000`  label `地狱雷光系数(未启动)`

### 地狱雷光范围
- `0x100d8da0` (fn `0x100d8810`) -> `0x76f63d` len 9  new `68C80000`  dumped `68C80000006A026A03`  label `地狱雷光范围(未启动)`

### 地面物品消失时间
- `0x100ab009` (fn `0x100a96c0`) -> `0x77a3ff` len 4  new `None`  dumped `C0270900`  label `地面物品消失时间(已启动)`
- `0x100ab090` (fn `0x100a96c0`) -> `0x77a3ff` len 4  new `C0270900`  dumped `C0270900`  label `地面物品消失时间(未启动)`

### 基本剑术
- `0x100b49d9` (fn `0x100b1fc0`) -> `0x76af96` len 3  new `8D04`  dumped `8D0440`  label `基本剑术(已重设)`
- `0x100b4a10` (fn `0x100b1fc0`) -> `0x76afa9` len 6  new `E9500300`  dumped `0F854F030000`  label `基本剑术(已重设)`

### 复活戒指改cd
- `0x100d23bf` (fn `0x100cebd0`) -> `0x73c4f2` len 6  new `85C0740E`  dumped `85C0740E2BF0`  label `复活戒指改cd(未启动)`
- `0x100d23fd` (fn `0x100cebd0`) -> `0x73c47a` len 5  new `6884C773`  dumped `6884C77300`  label `复活戒指改cd(未启动)`
- `0x100d243b` (fn `0x100cebd0`) -> `0x743751` len 5  new `8B55FC2B`  dumped `8B55FC2BD0`  label `复活戒指改cd(未启动)`

### 复活戒指概率
- `0x100d2684` (fn `0x100cebd0`) -> `0x74373a` len 7  new `80BEB801`  dumped `80BEB801000000`  label `复活戒指概率(未启动)`

### 复活触发脚本
- `0x100d1f0d` (fn `0x100cebd0`) -> `0x73c484` len 6  new `33D25250`  dumped `33D252508BC6`  label `复活触发脚本(未启动)`

### 安全区禁止丢物
- `0x100aa52d` (fn `0x100a96c0`) -> `0x73cc98` len 6  new `558BEC83`  dumped `558BEC83C4EC`  label `安全区禁止丢物(未启动)`

### 屏蔽元宝增减信息
- `0x100ac889` (fn `0x100a96c0`) -> `0x6f9002` len 2  new `EB2C`  dumped `742C`  label `屏蔽元宝增减信息(已启动)`
- `0x100ac8c3` (fn `0x100a96c0`) -> `0x6f9047` len 2  new `EB38`  dumped `7709`  label `屏蔽元宝增减信息(已启动)`
- `0x100ac8fd` (fn `0x100a96c0`) -> `0x6f8bcf` len 7  new `E98A0200`  dumped `8D458C508B4340`  label `屏蔽元宝增减信息(已启动)`
- `0x100ac937` (fn `0x100a96c0`) -> `0x6f8c02` len 7  new `E9570200`  dumped `8D4580508B4340`  label `屏蔽元宝增减信息(已启动)`
- `0x100ac974` (fn `0x100a96c0`) -> `0x6f8b25` len 3  new `EB76`  dumped `8D45A8`  label `屏蔽元宝增减信息(已启动)`
- `0x100ac9b1` (fn `0x100a96c0`) -> `0x6f8b62` len 3  new `EB39`  dumped `8D4594`  label `屏蔽元宝增减信息(已启动)`
- `0x100ac9ee` (fn `0x100a96c0`) -> `0x6f8288` len 3  new `EB76`  dumped `8D45AC`  label `屏蔽元宝增减信息(已启动)`
- `0x100aca2b` (fn `0x100a96c0`) -> `0x6f82c5` len 3  new `EB39`  dumped `8D4598`  label `屏蔽元宝增减信息(已启动)`
- `0x100acb2d` (fn `0x100a96c0`) -> `0x6f9002` len 2  new `742C`  dumped `742C`  label `屏蔽元宝增减信息(未启动)`
- `0x100acb67` (fn `0x100a96c0`) -> `0x6f9047` len 2  new `7709`  dumped `7709`  label `屏蔽元宝增减信息(未启动)`
- `0x100acba1` (fn `0x100a96c0`) -> `0x6f8bcf` len 7  new `8D458C50`  dumped `8D458C508B4340`  label `屏蔽元宝增减信息(未启动)`
- `0x100acbd8` (fn `0x100a96c0`) -> `0x6f8c02` len 7  new `8D458050`  dumped `8D4580508B4340`  label `屏蔽元宝增减信息(未启动)`
- `0x100acc15` (fn `0x100a96c0`) -> `0x6f8b25` len 3  new `8D45`  dumped `8D45A8`  label `屏蔽元宝增减信息(未启动)`
- `0x100acc52` (fn `0x100a96c0`) -> `0x6f8b62` len 3  new `8D45`  dumped `8D4594`  label `屏蔽元宝增减信息(未启动)`
- `0x100acc8f` (fn `0x100a96c0`) -> `0x6f8288` len 3  new `8D45`  dumped `8D45AC`  label `屏蔽元宝增减信息(未启动)`
- `0x100acccc` (fn `0x100a96c0`) -> `0x6f82c5` len 3  new `8D45`  dumped `8D4598`  label `屏蔽元宝增减信息(未启动)`

### 屏蔽元宝数据库日志
- `0x100acd37` (fn `0x100a96c0`) -> `0x70f6dc` len 1  new `C3`  dumped `55`  label `屏蔽元宝数据库日志(已启动)`
- `0x100acdbb` (fn `0x100a96c0`) -> `0x70f6dc` len 1  new `55`  dumped `55`  label `屏蔽元宝数据库日志(未启动)`

### 屏蔽发言频繁禁言功能
- `0x100ac678` (fn `0x100a96c0`) -> `0x6bb56a` len 6  new `90909090`  dumped `FF83740A0000`  label `屏蔽发言频繁禁言功能(已启动)`
- `0x100ac6b2` (fn `0x100a96c0`) -> `0x6bb579` len 6  new `90909090`  dumped `FE8382060000`  label `屏蔽发言频繁禁言功能(已启动)`
- `0x100ac75d` (fn `0x100a96c0`) -> `0x6bb56a` len 6  new `FF83740A`  dumped `FF83740A0000`  label `屏蔽发言频繁禁言功能(未启动)`
- `0x100ac797` (fn `0x100a96c0`) -> `0x6bb579` len 6  new `FE838206`  dumped `FE8382060000`  label `屏蔽发言频繁禁言功能(未启动)`

### 屏蔽属性提升提示
- `0x100ab4ac` (fn `0x100a96c0`) -> `0x741a21` len 5  new `E91C1200`  dumped `68782C7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab4e6` (fn `0x100a96c0`) -> `0x741a5c` len 5  new `E9E11100`  dumped `68A02C7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab523` (fn `0x100a96c0`) -> `0x741a97` len 5  new `E9A61100`  dumped `68BC2C7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab560` (fn `0x100a96c0`) -> `0x741ad2` len 5  new `E96B1100`  dumped `68D82C7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab59d` (fn `0x100a96c0`) -> `0x741b0d` len 5  new `E9301100`  dumped `68F42C7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab5da` (fn `0x100a96c0`) -> `0x741b48` len 5  new `E9F51000`  dumped `68102D7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab617` (fn `0x100a96c0`) -> `0x741b83` len 5  new `E9BA1000`  dumped `682C2D7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab654` (fn `0x100a96c0`) -> `0x741bbe` len 5  new `E97F1000`  dumped `68482D7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab691` (fn `0x100a96c0`) -> `0x741bf9` len 5  new `E9441000`  dumped `68602D7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab6ce` (fn `0x100a96c0`) -> `0x741c34` len 5  new `E9091000`  dumped `68782D7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab70b` (fn `0x100a96c0`) -> `0x741c6f` len 5  new `E9CE0F00`  dumped `68902D7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab748` (fn `0x100a96c0`) -> `0x741caa` len 5  new `E9930F00`  dumped `68A82D7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab785` (fn `0x100a96c0`) -> `0x741ce5` len 5  new `E9580F00`  dumped `68C02D7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab7c2` (fn `0x100a96c0`) -> `0x741d20` len 5  new `E91D0F00`  dumped `68DC2D7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab7ff` (fn `0x100a96c0`) -> `0x741d5b` len 5  new `E9E20E00`  dumped `68F42D7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab83c` (fn `0x100a96c0`) -> `0x741dfd` len 5  new `E9400E00`  dumped `68702E7400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab879` (fn `0x100a96c0`) -> `0x74281d` len 9  new `E9200400`  dumped `66B9DBFFBA78327400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab8b6` (fn `0x100a96c0`) -> `0x742835` len 9  new `E9080400`  dumped `66B9DBFFBA90327400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab8f3` (fn `0x100a96c0`) -> `0x74284d` len 9  new `E9F00300`  dumped `66B9DBFFBAA8327400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab930` (fn `0x100a96c0`) -> `0x742865` len 9  new `E9D80300`  dumped `66B9DBFFBAC0327400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab96d` (fn `0x100a96c0`) -> `0x74287d` len 9  new `E9C00300`  dumped `66B9DBFFBAD8327400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab9aa` (fn `0x100a96c0`) -> `0x742895` len 9  new `E9A80300`  dumped `66B9DBFFBAF0327400`  label `屏蔽属性提升提示(已启动)`
- `0x100ab9e7` (fn `0x100a96c0`) -> `0x7428ad` len 9  new `E9900300`  dumped `66B9DBFFBA08337400`  label `屏蔽属性提升提示(已启动)`
- `0x100aba24` (fn `0x100a96c0`) -> `0x7428c5` len 9  new `E9780300`  dumped `66B9DBFFBA24337400`  label `屏蔽属性提升提示(已启动)`
- `0x100aba61` (fn `0x100a96c0`) -> `0x7428dd` len 9  new `E9600300`  dumped `66B9DBFFBA3C337400`  label `屏蔽属性提升提示(已启动)`
- `0x100aba9e` (fn `0x100a96c0`) -> `0x74290d` len 9  new `E9300300`  dumped `66B9DBFFBA70337400`  label `屏蔽属性提升提示(已启动)`
- `0x100abadb` (fn `0x100a96c0`) -> `0x742925` len 9  new `E9180300`  dumped `66B9DBFFBA88337400`  label `屏蔽属性提升提示(已启动)`
- `0x100abb15` (fn `0x100a96c0`) -> `0x74293d` len 9  new `E9000300`  dumped `66B9DBFFBAA0337400`  label `屏蔽属性提升提示(已启动)`
- `0x100abb4f` (fn `0x100a96c0`) -> `0x742955` len 9  new `E9E80200`  dumped `66B9DBFFBABC337400`  label `屏蔽属性提升提示(已启动)`
- `0x100abb89` (fn `0x100a96c0`) -> `0x74296d` len 9  new `E9D00200`  dumped `66B9DBFFBAD8337400`  label `屏蔽属性提升提示(已启动)`
- `0x100abbc6` (fn `0x100a96c0`) -> `0x74298c` len 9  new `E9B10200`  dumped `66B9DBFFBAF0337400`  label `屏蔽属性提升提示(已启动)`
- `0x100abed8` (fn `0x100a96c0`) -> `0x741a21` len 5  new `68782C74`  dumped `68782C7400`  label `屏蔽属性提升提示(未启动)`
- `0x100abf12` (fn `0x100a96c0`) -> `0x741a5c` len 5  new `68A02C74`  dumped `68A02C7400`  label `屏蔽属性提升提示(未启动)`
- `0x100abf4c` (fn `0x100a96c0`) -> `0x741a97` len 5  new `68BC2C74`  dumped `68BC2C7400`  label `屏蔽属性提升提示(未启动)`
- `0x100abf86` (fn `0x100a96c0`) -> `0x741ad2` len 5  new `68D82C74`  dumped `68D82C7400`  label `屏蔽属性提升提示(未启动)`
- `0x100abfc3` (fn `0x100a96c0`) -> `0x741b0d` len 5  new `68F42C74`  dumped `68F42C7400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac000` (fn `0x100a96c0`) -> `0x741b48` len 5  new `68102D74`  dumped `68102D7400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac03d` (fn `0x100a96c0`) -> `0x741b83` len 5  new `682C2D74`  dumped `682C2D7400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac07a` (fn `0x100a96c0`) -> `0x741bbe` len 5  new `68482D74`  dumped `68482D7400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac0b7` (fn `0x100a96c0`) -> `0x741bf9` len 5  new `68602D74`  dumped `68602D7400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac0f4` (fn `0x100a96c0`) -> `0x741c34` len 5  new `68782D74`  dumped `68782D7400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac131` (fn `0x100a96c0`) -> `0x741c6f` len 5  new `68902D74`  dumped `68902D7400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac16b` (fn `0x100a96c0`) -> `0x741caa` len 5  new `68A82D74`  dumped `68A82D7400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac1a5` (fn `0x100a96c0`) -> `0x741ce5` len 5  new `68C02D74`  dumped `68C02D7400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac1df` (fn `0x100a96c0`) -> `0x741d20` len 5  new `68DC2D74`  dumped `68DC2D7400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac219` (fn `0x100a96c0`) -> `0x741d5b` len 5  new `68F42D74`  dumped `68F42D7400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac253` (fn `0x100a96c0`) -> `0x741dfd` len 5  new `68702E74`  dumped `68702E7400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac290` (fn `0x100a96c0`) -> `0x74281d` len 9  new `66B9DBFF`  dumped `66B9DBFFBA78327400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac2cd` (fn `0x100a96c0`) -> `0x742835` len 9  new `66B9DBFF`  dumped `66B9DBFFBA90327400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac30a` (fn `0x100a96c0`) -> `0x74284d` len 9  new `66B9DBFF`  dumped `66B9DBFFBAA8327400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac347` (fn `0x100a96c0`) -> `0x742865` len 9  new `66B9DBFF`  dumped `66B9DBFFBAC0327400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac384` (fn `0x100a96c0`) -> `0x74287d` len 9  new `66B9DBFF`  dumped `66B9DBFFBAD8327400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac3c1` (fn `0x100a96c0`) -> `0x742895` len 9  new `66B9DBFF`  dumped `66B9DBFFBAF0327400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac3fe` (fn `0x100a96c0`) -> `0x7428ad` len 9  new `66B9DBFF`  dumped `66B9DBFFBA08337400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac43b` (fn `0x100a96c0`) -> `0x7428c5` len 9  new `66B9DBFF`  dumped `66B9DBFFBA24337400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac478` (fn `0x100a96c0`) -> `0x7428dd` len 9  new `66B9DBFF`  dumped `66B9DBFFBA3C337400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac4b5` (fn `0x100a96c0`) -> `0x74290d` len 9  new `66B9DBFF`  dumped `66B9DBFFBA70337400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac4f2` (fn `0x100a96c0`) -> `0x742925` len 9  new `66B9DBFF`  dumped `66B9DBFFBA88337400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac52f` (fn `0x100a96c0`) -> `0x74293d` len 9  new `66B9DBFF`  dumped `66B9DBFFBAA0337400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac56c` (fn `0x100a96c0`) -> `0x742955` len 9  new `66B9DBFF`  dumped `66B9DBFFBABC337400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac5a9` (fn `0x100a96c0`) -> `0x74296d` len 9  new `66B9DBFF`  dumped `66B9DBFFBAD8337400`  label `屏蔽属性提升提示(未启动)`
- `0x100ac5e6` (fn `0x100a96c0`) -> `0x74298c` len 9  new `66B9DBFF`  dumped `66B9DBFFBAF0337400`  label `屏蔽属性提升提示(未启动)`

### 屏蔽排行榜
- `0x100b91a8` (fn `0x100b7f40`) -> `0x6cba88` len 1  new `C3`  dumped `55`  label `屏蔽排行榜(已启动)`
- `0x100b9226` (fn `0x100b7f40`) -> `0x6cba88` len 1  new `55`  dumped `55`  label `屏蔽排行榜(未启动)`

### 屏蔽自动绑定
- `0x100bf540` (fn `0x100bf430`) -> `0x74dc57` len 1  new `00`  dumped `01`  label `屏蔽自动绑定(已启动)`
- `0x100bf56e` (fn `0x100bf430`) -> `0x74dd59` len 1  new `00`  dumped `01`  label `屏蔽自动绑定(已启动)`
- `0x100bf59c` (fn `0x100bf430`) -> `0x74ddc3` len 1  new `00`  dumped `01`  label `屏蔽自动绑定(已启动)`
- `0x100bf5ca` (fn `0x100bf430`) -> `0x74ddef` len 1  new `00`  dumped `01`  label `屏蔽自动绑定(已启动)`
- `0x100bf644` (fn `0x100bf430`) -> `0x74dc57` len 1  new `01`  dumped `01`  label `屏蔽自动绑定(未启用)`
- `0x100bf672` (fn `0x100bf430`) -> `0x74dd59` len 1  new `01`  dumped `01`  label `屏蔽自动绑定(未启用)`
- `0x100bf6a0` (fn `0x100bf430`) -> `0x74ddc3` len 1  new `01`  dumped `01`  label `屏蔽自动绑定(未启用)`
- `0x100bf6ce` (fn `0x100bf430`) -> `0x74ddef` len 1  new `01`  dumped `01`  label `屏蔽自动绑定(未启用)`

### 心灵启示触发
- `0x100ae93d` (fn `0x100a96c0`) -> `0x6edc2b` len 5  new `E8F46708`  dumped `E8F4670800`  label `心灵启示触发(未启动)`

### 指定地图编号摆摊
- `0x100acec7` (fn `0x100a96c0`) -> `0x6e7930` len 4  new `None`  dumped `03000000`  label `指定地图编号摆摊(已启动)`
- `0x100acf06` (fn `0x100a96c0`) -> `0x6e7934` len None  new `None`  dumped `None`  label `指定地图编号摆摊(已启动)`
- `0x100acf40` (fn `0x100a96c0`) -> `0x6e7c5f` len 2  new `EB1D`  dumped `751D`  label `指定地图编号摆摊(已启动)`
- `0x100acf7a` (fn `0x100a96c0`) -> `0x6e7c1c` len 10  new `C7EBD4DA`  dumped `C7EBD4DAD7AFD4B0C4DA`  label `指定地图编号摆摊(已启动)`
- `0x100ad04b` (fn `0x100a96c0`) -> `0x6e7930` len 8  new `03000000`  dumped `0300000047413000`  label `指定地图编号摆摊(未启动)`
- `0x100ad085` (fn `0x100a96c0`) -> `0x6e7c1c` len 10  new `C7EBD4DA`  dumped `C7EBD4DAD7AFD4B0C4DA`  label `指定地图编号摆摊(未启动)`
- `0x100ad0bf` (fn `0x100a96c0`) -> `0x6e7c5f` len 2  new `751D`  dumped `751D`  label `指定地图编号摆摊(未启动)`

### 挖矿触发
- `0x100ae20f` (fn `0x100a96c0`) -> `0x6ec111` len 5  new `66837E26`  dumped `66837E2600`  label `挖矿触发(未启动)`

### 捡物触发
- `0x100d2cd4` (fn `0x100cebd0`) -> `0x6b770c` len 5  new `8B55FC8B`  dumped `8B55FC8BC3`  label `捡物触发(未启动)`

### 摆摊穿人
- `0x100a9a3a` (fn `0x100a96c0`) -> `0x77931d` len 3  new `C600`  dumped `C60002`  label `摆摊穿人(已启动)`
- `0x100a9ac7` (fn `0x100a96c0`) -> `0x77931d` len 3  new `C600`  dumped `C60002`  label `摆摊穿人(未启动)`

### 攻击反伤
- `0x100d2b26` (fn `0x100cebd0`) -> `0x767bb4` len 5  new `894DFC85`  dumped `894DFC85D2`  label `攻击反伤(未启动)`

### 攻击触发
- `0x100d2e7f` (fn `0x100cebd0`) -> `0x76e35d` len 5  new `68C80000`  dumped `68C8000000`  label `攻击触发(未启动)`

### 攻沙脚本控制
- `0x100aa19a` (fn `0x100a96c0`) -> `0x65c6b6` len 6  new `90909090`  dumped `0F82D2000000`  label `攻沙脚本控制(未启动)`
- `0x100aa1d4` (fn `0x100a96c0`) -> `0x65c76d` len 2  new `9090`  dumped `741F`  label `攻沙脚本控制(未启动)`
- `0x100aa211` (fn `0x100a96c0`) -> `0x65c785` len 2  new `EB07`  dumped `7F07`  label `攻沙脚本控制(未启动)`
- `0x100aa26b` (fn `0x100a96c0`) -> `0x65c6b6` len 6  new `0F82D200`  dumped `0F82D2000000`  label `攻沙脚本控制(未启动)`
- `0x100aa2a5` (fn `0x100a96c0`) -> `0x65c76d` len 2  new `741F`  dumped `741F`  label `攻沙脚本控制(未启动)`
- `0x100aa2e2` (fn `0x100a96c0`) -> `0x65c785` len 2  new `7F07`  dumped `7F07`  label `攻沙脚本控制(未启动)`

### 新倍攻和暴击
- `0x100d3cec` (fn `0x100cebd0`) -> `0x76c88b` len 5  new `8BC65F5E`  dumped `8BC65F5E5B`  label `新倍攻和暴击(未启动)`

### 新穿戴触发
- `0x100d371c` (fn `0x100cebd0`) -> `0x75f085` len 7  new `89748308`  dumped `897483088B550C`  label `新穿戴触发(未启动)`
- `0x100d375a` (fn `0x100cebd0`) -> `0x75ea37` len 5  new `5F5E5B59`  dumped `5F5E5B595D`  label `新穿戴触发(未启动)`

### 施毒术
- `0x100b8581` (fn `0x100b7f40`) -> `0x76e599` len 31  new `B9`  dumped `8BC6E8CCA3D5FF3C047507B808000000EB0D8BC6E8BAA3D5FF25FF00000040`  label `施毒术(已重设)`

### 武器绿毒
- `0x100b224e` (fn `0x100b1fc0`) -> `0x76e2bc` len 7  new `80BBB401`  dumped `80BBB401000000`  label `武器绿毒(未启动)`

### 死亡触发
- `0x100ad555` (fn `0x100a96c0`) -> `0x6c09b5` len 5  new `5F5E5B59`  dumped `5F5E5B5959`  label `死亡触发(未启动)`

### 永久属性
- `0x100d1768` (fn `0x100cebd0`) -> `0x73d9cf` len 8  new `8B070186`  dumped `8B070186B0020000`  label `永久属性(未启动)`
- `0x100d17a9` (fn `0x100cebd0`) -> `0x73d9d7` len 9  new `8B470401`  dumped `8B47040186B8020000`  label `永久属性(未启动)`
- `0x100d17ea` (fn `0x100cebd0`) -> `0x73d9e0` len 9  new `8B470C01`  dumped `8B470C01867C020000`  label `永久属性(未启动)`
- `0x100d182b` (fn `0x100cebd0`) -> `0x73d9e9` len 9  new `8B471401`  dumped `8B4714018684020000`  label `永久属性(未启动)`
- `0x100d186c` (fn `0x100cebd0`) -> `0x73d9f2` len 9  new `8B471C01`  dumped `8B471C01868C020000`  label `永久属性(未启动)`
- `0x100d18ad` (fn `0x100cebd0`) -> `0x73d9fb` len 9  new `8B472401`  dumped `8B4724018694020000`  label `永久属性(未启动)`
- `0x100d18ee` (fn `0x100cebd0`) -> `0x73da04` len 9  new `8B472C01`  dumped `8B472C01869C020000`  label `永久属性(未启动)`
- `0x100d192f` (fn `0x100cebd0`) -> `0x73da16` len 9  new `8B471001`  dumped `8B4710018680020000`  label `永久属性(未启动)`
- `0x100d1970` (fn `0x100cebd0`) -> `0x73da1f` len 9  new `8B471801`  dumped `8B4718018688020000`  label `永久属性(未启动)`
- `0x100d19b1` (fn `0x100cebd0`) -> `0x73da28` len 9  new `8B472001`  dumped `8B4720018690020000`  label `永久属性(未启动)`
- `0x100d19f2` (fn `0x100cebd0`) -> `0x73da31` len 9  new `8B472801`  dumped `8B4728018698020000`  label `永久属性(未启动)`
- `0x100d1a33` (fn `0x100cebd0`) -> `0x73da3a` len 9  new `8B473001`  dumped `8B47300186A0020000`  label `永久属性(未启动)`

### 永久攻速
- `0x100d5cdd` (fn `0x100cebd0`) -> `0x73d9a0` len 11  new `668B474C`  dumped `668B474C66018674020000`  label `永久攻速(未启动)`

### 法师群毒
- `0x100b2a07` (fn `0x100b1fc0`) -> `0x76e1a9` len 6  new `0F848000`  dumped `0F8480000000`  label `法师群毒(未启动)`

### 激光命中概率
- `0x100d9665` (fn `0x100d8810`) -> `0x76ea14` len 5  new `B8030000`  dumped `B803000000`  label `激光命中概率(未启动)`

### 激光电影可换主属性
- `0x100d92ab` (fn `0x100d8810`) -> `0x76e9b5` len 14  new `8BBB9402`  dumped `8BBB9402000003D78B8B98020000`  label `激光电影可换主属性(未启动)`

### 激光范围及系数
- `0x100d93d6` (fn `0x100d8810`) -> `0x76ea07` len 6  new `6A018BCF`  dumped `6A018BCF33D2`  label `激光范围及系数(未启动)`

### 火墙设置时间上限
- `0x100b3a61` (fn `0x100b1fc0`) -> `0x7706b6` len 6  new `69FEE803`  dumped `69FEE8030000`  label `火墙设置时间上限(未启动)`

### 火球主属性切换
- `0x100d97ac` (fn `0x100d8810`) -> `0x76e3e6` len 14  new `8BBB9402`  dumped `8BBB9402000003D78B8B98020000`  label `火球主属性切换(未启动)`

### 火球自定义范围
- `0x100d9a36` (fn `0x100d8810`) -> `0x76e425` len 9  new `6A026A01`  dumped `6A026A01A06CE47600`  label `火球自定义范围(未启动)`

### 火雨主属切换
- `0x100da43a` (fn `0x100d8810`) -> `0x76f365` len 14  new `8BBB9402`  dumped `8BBB9402000003D78B8B98020000`  label `火雨主属切换(未启动)`

### 烈火剑法
- `0x100b45a3` (fn `0x100b1fc0`) -> `0x76b0ec` len 3  new `D1`  dumped `C1E002`  label `烈火剑法(已重设)`
- `0x100b45da` (fn `0x100b1fc0`) -> `0x77231d` len 2  new `EB15`  dumped `7515`  label `烈火剑法(已重设)`

### 爆裂火焰可换主属性
- `0x100d8ee7` (fn `0x100d8810`) -> `0x76f23b` len 14  new `8BBB9402`  dumped `8BBB9402000003D78B8B98020000`  label `爆裂火焰可换主属性(未启动)`

### 爆裂火焰范围及系数
- `0x100d901d` (fn `0x100d8810`) -> `0x76f26b` len 7  new `68580200`  dumped `68580200006A01`  label `爆裂火焰范围及系数(未启动)`

### 物功带毒
- `0x100b278a` (fn `0x100b1fc0`) -> `0x76e2bc` len 7  new `80BBB401`  dumped `80BBB401000000`  label `物功带毒(未启动)`

### 特殊宝宝
- `0x100d503d` (fn `0x100cebd0`) -> `0x7671a2` len 6  new `89934403`  dumped `899344030000`  label `特殊宝宝(未启动)`
- `0x100d507e` (fn `0x100cebd0`) -> `0x76de42` len 10  new `8B45FC80`  dumped `8B45FC80B87801000032`  label `特殊宝宝(未启动)`

### 特殊属性
- `0x100d1c6c` (fn `0x100cebd0`) -> `0x6e41bd` len 5  new `8B068B55`  dumped `8B068B55FC`  label `特殊属性(未启动)`
- `0x100d1cad` (fn `0x100cebd0`) -> `0x73d951` len 7  new `0FB79664`  dumped `0FB79664020000`  label `特殊属性(未启动)`

### 盘古穿戴触发
- `0x100adac7` (fn `0x100a96c0`) -> `0x6d8e35` len 5  new `E9F22D00`  dumped `E9F22D0000`  label `盘古穿戴触发(未启动)`
- `0x100adb05` (fn `0x100a96c0`) -> `0x6d8e4d` len 5  new `E9DA2D00`  dumped `E9DA2D0000`  label `盘古穿戴触发(未启动)`

### 盘古高级属性
- `0x100aede2` (fn `0x100a96c0`) -> `0x6ba72d` len 20  new `0FB7500C`  dumped `8B500C0153440FBE500701531C0FBE4008014324`  label `盘古高级属性(已启动)`
- `0x100aee1c` (fn `0x100a96c0`) -> `0x6f9ab0` len 43  new `558BEC83`  dumped `558BEC6A006A005356578BF98BF28BD833C05568889B6F0064FF3064892080BB3E190000000F848D000000`  label `盘古高级属性(已启动)`
- `0x100aefad` (fn `0x100a96c0`) -> `0x6ba718` len 5  new `0FBE5004`  dumped `0FBE500401`  label `盘古高级属性(未启动)`
- `0x100aefe7` (fn `0x100a96c0`) -> `0x6ba72d` len 23  new `8B500C01`  dumped `8B500C0153440FBE500701531C0FBE40080143248B434C`  label `盘古高级属性(未启动)`
- `0x100af021` (fn `0x100a96c0`) -> `0x6f9ab0` len 43  new `558BEC6A`  dumped `558BEC6A006A005356578BF98BF28BD833C05568889B6F0064FF3064892080BB3E190000000F848D000000`  label `盘古高级属性(未启动)`

### 盘古魔法攻击触发
- `0x100adfbe` (fn `0x100a96c0`) -> `0x76e1af` len 7  new `80BEB601`  dumped `80BEB601000000`  label `盘古魔法攻击触发(未启动)`
- `0x100adffc` (fn `0x100a96c0`) -> `0x76dec0` len 7  new `80BBB601`  dumped `80BBB601000000`  label `盘古魔法攻击触发(未启动)`

### 禁止交易地图
- `0x100aab1b` (fn `0x100a96c0`) -> `0x6c3f00` len 5  new `558BEC6A`  dumped `558BEC6A00`  label `禁止交易地图(未启动)`

### 禁止发言不提示
- `0x100db803` (fn `0x100d8810`) -> `0x6bb5cd` len 6  new `90909090`  dumped `FF93D4000000`  label `禁止发言不提示(已启动)`
- `0x100db83a` (fn `0x100d8810`) -> `0x6bb625` len 6  new `90909090`  dumped `FF93D4000000`  label `禁止发言不提示(已启动)`
- `0x100db874` (fn `0x100d8810`) -> `0x6c94a9` len 6  new `90909090`  dumped `FF93D4000000`  label `禁止发言不提示(已启动)`
- `0x100db981` (fn `0x100d8810`) -> `0x6bb5cd` len 6  new `FF93D400`  dumped `FF93D4000000`  label `禁止发言不提示(未启动)`
- `0x100db9b8` (fn `0x100d8810`) -> `0x6bb625` len 6  new `FF93D400`  dumped `FF93D4000000`  label `禁止发言不提示(未启动)`
- `0x100db9f2` (fn `0x100d8810`) -> `0x6c94a9` len 6  new `FF93D400`  dumped `FF93D4000000`  label `禁止发言不提示(未启动)`

### 禁止宝宝休息
- `0x100aac71` (fn `0x100a96c0`) -> `0x623a73` len 7  new `80B0C704`  dumped `80B0C704000001`  label `禁止宝宝休息(未启动)`

### 禁止装备自动绑定
- `0x100d390a` (fn `0x100cebd0`) -> `0x784351` len 7  new `80BEFC00`  dumped `80BEFC00000000`  label `禁止装备自动绑定(未启动)`

### 移动速度
- `0x100d29f9` (fn `0x100cebd0`) -> `0x73d983` len 7  new `66018668`  dumped `66018668020000`  label `移动速度(未启动)`

### 穿人穿怪
- `0x100aa8b2` (fn `0x100a96c0`) -> `0x6b30a3` len 10  new `C681FE03`  dumped `8891FE03000084D2741B`  label `穿人穿怪(已启动)`
- `0x100aa8ec` (fn `0x100a96c0`) -> `0x768454` len 3  new `B001`  dumped `558BEC`  label `穿人穿怪(已启动)`
- `0x100aa99e` (fn `0x100a96c0`) -> `0x6b30a3` len 10  new `8891FE03`  dumped `8891FE03000084D2741B`  label `穿人穿怪(未启动)`
- `0x100aa9d8` (fn `0x100a96c0`) -> `0x768454` len 3  new `558B`  dumped `558BEC`  label `穿人穿怪(未启动)`

### 脚本控制人物爆率
- `0x100b9a35` (fn `0x100b7f40`) -> `0x73dac5` len 6  new `90909090`  dumped `89868C010000`  label `脚本控制人物爆率(已启动)`
- `0x100b9a6c` (fn `0x100b7f40`) -> `0x73d578` len 7  new `90909090`  dumped `C6867905000000`  label `脚本控制人物爆率(已启动)`
- `0x100b9b32` (fn `0x100b7f40`) -> `0x6df2cc` len 9  new `8945FC8D`  dumped `8945FC8D9308080000`  label `脚本控制人物爆率(未启用)`
- `0x100b9b69` (fn `0x100b7f40`) -> `0x73dac5` len 6  new `89868C01`  dumped `89868C010000`  label `脚本控制人物爆率(未启用)`
- `0x100b9ba0` (fn `0x100b7f40`) -> `0x73d578` len 7  new `C6867905`  dumped `C6867905000000`  label `脚本控制人物爆率(未启用)`

### 脚本控制头发外显
- `0x100aebd1` (fn `0x100a96c0`) -> `0x740f85` len 5  new `730F8856`  dumped `730F885670`  label `脚本控制头发外显(未启动)`

### 英雄倍攻和暴击
- `0x100d4af9` (fn `0x100cebd0`) -> `0x76c816` len 7  new `83BB8400`  dumped `83BB8400000000`  label `英雄倍攻和暴击(未启动)`

### 英雄攻速移速
- `0x100d47a5` (fn `0x100cebd0`) -> `0x73da43` len 9  new `8B473801`  dumped `8B47380186A8020000`  label `英雄攻速移速(未启动)`

### 英雄施法速度
- `0x100d51a1` (fn `0x100cebd0`) -> `0x68dd60` len 5  new `057E0400`  dumped `057E040000`  label `英雄施法速度(未启动)`

### 英雄穿戴触发
- `0x100d4633` (fn `0x100cebd0`) -> `0x75f08c` len 7  new `8BC68B08`  dumped `8BC68B08FF516C`  label `英雄穿戴触发(未启动)`
- `0x100d4674` (fn `0x100cebd0`) -> `0x75ea31` len 6  new `33C9894C`  dumped `33C9894C9608`  label `英雄穿戴触发(未启动)`

### 获取玩家对象函数
- `0x100b9595` (fn `0x100b7f40`) -> `0x646f40` len 72  new `558BEC51`  dumped `558BEC6A006A005356578BFA8BF033C055680970640064FF3064892033DBF68655040000100F848300000080BF2A0D000000756C80BF2D0D00000A7D2E8BC7E8E8650900487C14B3`  label `获取玩家对象函数(已启动)`
- `0x100b95c9` (fn `0x100b7f40`) -> `0x647d24` len 84  new `558BEC51`  dumped `558BEC51538955FC8B45FCE88CDCDBFF33C055686B7D640064FF30648920A128677D008B0033C98B55FCE8493AFBFF8BD833C05A595964891068727D64008D45FCE896D7DBFFC3E9C8D0DBFFEBF08BC35B595DC3`  label `获取玩家对象函数(已启动)`
- `0x100b9600` (fn `0x100b7f40`) -> `0x736ef8` len 12  new `2054506C`  dumped `20496E74656765723B000000`  label `获取玩家对象函数(已启动)`
- `0x100b9637` (fn `0x100b7f40`) -> `0x736b28` len 9  new `6D3A2073`  dumped `3A2054506C61796572`  label `获取玩家对象函数(已启动)`
- `0x100b9833` (fn `0x100b7f40`) -> `0x647d24` len 84  new `558BEC51`  dumped `558BEC51538955FC8B45FCE88CDCDBFF33C055686B7D640064FF30648920A128677D008B0033C98B55FCE8493AFBFF8BD833C05A595964891068727D64008D45FCE896D7DBFFC3E9C8D0DBFFEBF08BC35B595DC3`  label `获取玩家对象函数(未启用)`
- `0x100b986a` (fn `0x100b7f40`) -> `0x736ef8` len 12  new `20496E74`  dumped `20496E74656765723B000000`  label `获取玩家对象函数(未启用)`
- `0x100b98a1` (fn `0x100b7f40`) -> `0x646f40` len 72  new `558BEC6A`  dumped `558BEC6A006A005356578BFA8BF033C055680970640064FF3064892033DBF68655040000100F848300000080BF2A0D000000756C80BF2D0D00000A7D2E8BC7E8E8650900487C14B3`  label `获取玩家对象函数(未启用)`
- `0x100b98d8` (fn `0x100b7f40`) -> `0x736b28` len 9  new `3A205450`  dumped `3A2054506C61796572`  label `获取玩家对象函数(未启用)`

### 行会显示
- `0x100aacef` (fn `0x100a96c0`) -> `0x6c5bcb` len 2  new `9090`  dumped `7449`  label `行会显示(已启动)`
- `0x100aad29` (fn `0x100a96c0`) -> `0x6c5bf7` len 2  new `9090`  dumped `741D`  label `行会显示(已启动)`
- `0x100aadc0` (fn `0x100a96c0`) -> `0x6c5bcb` len 2  new `7449`  dumped `7449`  label `行会显示(未启动)`
- `0x100aadfa` (fn `0x100a96c0`) -> `0x6c5bf7` len 2  new `741D`  dumped `741D`  label `行会显示(未启动)`

### 被击杀触发
- `0x100d282c` (fn `0x100cebd0`) -> `0x766624` len 5  new `8B45FC8B`  dumped `8B45FC8B10`  label `被击杀触发(未启动)`

### 装备吸血
- `0x100b93af` (fn `0x100b7f40`) -> `0x76e2a3` len 6  new `8BD38BC6`  dumped `8BD38BC68B38`  label `装备吸血(未启用)`

### 装备提升人物爆率
- `0x100ba0ae` (fn `0x100b7f40`) -> `0x71fd37` len 6  new `8B4014F7`  dumped `8B4014F76DD4`  label `装备提升人物爆率(待重设)`

### 装备来源
- `0x100d57bc` (fn `0x100cebd0`) -> `0x71fe90` len 5  new `8B08FF51`  dumped `8B08FF5138`  label `装备来源(未启动)`
- `0x100d57fa` (fn `0x100cebd0`) -> `0x6c8aaa` len 5  new `E8B9BA0B`  dumped `E8B9BA0B00`  label `装备来源(未启动)`

### 设置玩家称号函数_支持80字符
- `0x100b4b98` (fn `0x100b1fc0`) -> `0x6df754` len 69  new `558BEC53`  dumped `558BEC53568BF28BD885F6743483BBF807000000750F8D83F80700008BD6E8DD5DD2FFEB1CFFB3F807000068A4F76D00568D83F8070000BA03000000E8FB60D2FF5E5B5DC3`  label `设置玩家称号函数_支持80字符(已启动)`
- `0x100b4cc8` (fn `0x100b1fc0`) -> `0x6df754` len 69  new `558BEC53`  dumped `558BEC53568BF28BD885F6743483BBF807000000750F8D83F80700008BD6E8DD5DD2FFEB1CFFB3F807000068A4F76D00568D83F8070000BA03000000E8FB60D2FF5E5B5DC3`  label `设置玩家称号函数_支持80字符(未启动)`

### 读取英雄装备
- `0x100d53e8` (fn `0x100cebd0`) -> `0x6e04e7` len 5  new `E834E707`  dumped `E834E70700`  label `读取英雄装备(未启动)`

### 逐日剑法
- `0x100b4750` (fn `0x100b1fc0`) -> `0x76b14c` len 6  new `04079090`  dumped `8A80284B7D00`  label `逐日剑法(已重设)`
- `0x100b4787` (fn `0x100b1fc0`) -> `0x76b13e` len 2  new `9090`  dumped `731D`  label `逐日剑法(已重设)`

### 邮件防刷
- `0x100aa6c0` (fn `0x100a96c0`) -> `0x6e7810` len 5  new `558BEC53`  dumped `558BEC5356`  label `邮件防刷(未启动)`

### 野蛮等级
- `0x100db1d7` (fn `0x100d8810`) -> `0x768f67` len 7  new `0FB78078`  dumped `0FB78078020000`  label `野蛮等级(未启动)`

### 野蛮麻痹
- `0x100b38ca` (fn `0x100b1fc0`) -> `0x6bc9e2` len 5  new `B8030000`  dumped `B803000000`  label `野蛮麻痹(未启动)`

### 防0拆分
- `0x100aa819` (fn `0x100a96c0`) -> `0x6e0ff3` len 6  new `51B90700`  dumped `51B907000000`  label `防0拆分(未启动)`

### 随身仓库
- `0x100a9bc2` (fn `0x100a96c0`) -> `0x6e087c` len 45  new `558BEC53`  dumped `558BEC53B30183B860070000057C166A056A006A00B97427000066BA7D00E8F52DFFFFEB0233DB8BC35B5DC355`  label `随身仓库(已启动)`
- `0x100a9bf9` (fn `0x100a96c0`) -> `0x6c2ab9` len 6  new `EB429090`  dumped `8BB3D80C0000`  label `随身仓库(已启动)`
- `0x100a9c33` (fn `0x100a96c0`) -> `0x6c2dc9` len 6  new `EB429090`  dumped `8BBBD80C0000`  label `随身仓库(已启动)`
- `0x100a9d2c` (fn `0x100a96c0`) -> `0x6e087c` len 45  new `558BEC53`  dumped `558BEC53B30183B860070000057C166A056A006A00B97427000066BA7D00E8F52DFFFFEB0233DB8BC35B5DC355`  label `随身仓库(未启动)`
- `0x100a9d63` (fn `0x100a96c0`) -> `0x6c2ab9` len 6  new `8BB3D80C`  dumped `8BB3D80C0000`  label `随身仓库(未启动)`
- `0x100a9d9d` (fn `0x100a96c0`) -> `0x6c2dc9` len 6  new `8BBBD80C`  dumped `8BBBD80C0000`  label `随身仓库(未启动)`

### 雷电主属性切换
- `0x100d9b7d` (fn `0x100d8810`) -> `0x76ea8a` len 14  new `8BBB9402`  dumped `8BBB9402000003D78B8B98020000`  label `雷电主属性切换(未启动)`

### 雷电带毒
- `0x100b2c7b` (fn `0x100b1fc0`) -> `0x76eb1d` len 7  new `80BE7801`  dumped `80BE7801000032`  label `雷电带毒(未启动)`

### 雷电自定义范围
- `0x100d9e07` (fn `0x100d8810`) -> `0x76eb06` len 9  new `6A026A01`  dumped `6A026A01A050EB7600`  label `雷电自定义范围(未启动)`

### 魔法攻击触发
- `0x100d305f` (fn `0x100cebd0`) -> `0x76de84` len 6  new `8BF085F6`  dumped `8BF085F67E2C`  label `魔法攻击触发(未启动)`

### 麻痹概率
- `0x100d3f3f` (fn `0x100cebd0`) -> `0x76e2d2` len 5  new `BA050000`  dumped `BA05000000`  label `麻痹概率(未启动)`


## per-site detail (immediate)


### <unlabelled>
- `0x100b30a9` -> `[0x65b6dc]` w4  host `0x65b6db 00 00  add byte ptr [eax], al`  plugin-reset `None`
- `0x100b3117` -> `[0x65bbfe]` w4  host `0x65bbfd 3D 40 19 01 00  cmp eax, 0x11940`  plugin-reset `None`
- `0x100b3185` -> `[0x65be25]` w4  host `0x65be24 3D 40 19 01 00  cmp eax, 0x11940`  plugin-reset `None`
- `0x100b31f3` -> `[0x65c6b2]` w4  host `0x65c6b1 08 98 1B 01 00 0F  or byte ptr [eax + 0xf00011b], bl`  plugin-reset `None`
- `0x100b3261` -> `[0x65bdf6]` w4  host `0x65bdf5 08 00  or byte ptr [eax], al`  plugin-reset `None`
- `0x100b350d` -> `[0x73c4fa]` w4  host `0x73c4f8 81 FE 60 EA 00 00  cmp esi, 0xea60`  plugin-reset `None`
- `0x100b8279` -> `[0x76f86d]` w4  host `0x76f86c B9 70 17 00 00  mov ecx, 0x1770`  plugin-reset `None`
- `0x100b82e7` -> `[0x76f8a4]` w4  host `0x76f8a3 00 00  add byte ptr [eax], al`  plugin-reset `None`
- `0x100b89d5` -> `[0x7d33fc]` w4  host `0x7d33fb 00 00  add byte ptr [eax], al`  plugin-reset `None`
- `0x100b89e1` -> `[0x7d3400]` w4  host `0x7d33ff 00 00  add byte ptr [eax], al`  plugin-reset `None`
- `0x100b8a4f` -> `[0x7d3404]` w4  host `None`  plugin-reset `None`
- `0x100b8a5b` -> `[0x7d3408]` w4  host `0x7d3407 00 00  add byte ptr [eax], al`  plugin-reset `None`
- `0x100b8ac9` -> `[0x7d340c]` w4  host `0x7d340a 00 40 33  add byte ptr [eax + 0x33], al`  plugin-reset `None`
- `0x100b8ad5` -> `[0x7d3410]` w4  host `0x7d340f 33 33  xor esi, dword ptr [ebx]`  plugin-reset `None`
- `0x100b8b43` -> `[0x7d3414]` w4  host `0x7d3412 03 40 CD  add eax, dword ptr [eax - 0x33]`  plugin-reset `None`
- `0x100b8b4f` -> `[0x7d3418]` w4  host `None`  plugin-reset `None`
- `0x100b8df9` -> `[0x7d3278]` w4  host `0x7d3277 00 CD  add ch, cl`  plugin-reset `None`
- `0x100b8e05` -> `[0x7d327c]` w4  host `None`  plugin-reset `None`
- `0x100b8e73` -> `[0x7d3280]` w4  host `None`  plugin-reset `None`
- `0x100b8e7f` -> `[0x7d3284]` w4  host `0x7d3283 00 00  add byte ptr [eax], al`  plugin-reset `None`
- `0x100b8eed` -> `[0x7d3288]` w4  host `None`  plugin-reset `None`
- `0x100b8ef9` -> `[0x7d328c]` w4  host `0x7d328b 66 66 66 0A 40 CD  or al, byte ptr [eax - 0x33]`  plugin-reset `None`
- `0x100b8f67` -> `[0x7d3290]` w4  host `0x7d328e 0A 40 CD  or al, byte ptr [eax - 0x33]`  plugin-reset `None`
- `0x100b8f73` -> `[0x7d3294]` w4  host `None`  plugin-reset `None`
- `0x100b9c5e` -> `[0x73fcc9]` w1  host `0x73fcc8 C0 5A 89 45  rcr byte ptr [edx - 0x77], 0x45`  plugin-reset `None`
- `0x100bfdc9` -> `[0x760914]` w4  host `0x760913 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100bfe7c` -> `[0x76090f]` w4  host `0x76090e BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100bff2c` -> `[0x760920]` w4  host `0x76091f B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `30`
- `0x100bffdc` -> `[0x760939]` w4  host `0x760938 B8 0C 00 00 00  mov eax, 0xc`  plugin-reset `12`
- `0x100c008c` -> `[0x760934]` w4  host `0x760933 BA 0F 00 00 00  mov edx, 0xf`  plugin-reset `15`
- `0x100c013c` -> `[0x760945]` w4  host `0x760944 B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `30`
- `0x100c01e6` -> `[0x76095e]` w4  host `0x76095d B8 0C 00 00 00  mov eax, 0xc`  plugin-reset `12`
- `0x100c0285` -> `[0x760959]` w4  host `0x760958 BA 0F 00 00 00  mov edx, 0xf`  plugin-reset `15`
- `0x100c0324` -> `[0x76096a]` w4  host `0x760969 B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `30`
- `0x100c03c3` -> `[0x760983]` w4  host `0x760982 B8 0C 00 00 00  mov eax, 0xc`  plugin-reset `12`
- `0x100c0463` -> `[0x76097e]` w4  host `0x76097d BA 0F 00 00 00  mov edx, 0xf`  plugin-reset `15`
- `0x100c0503` -> `[0x76098f]` w4  host `0x76098e B8 14 00 00 00  mov eax, 0x14`  plugin-reset `30`
- `0x100c05a3` -> `[0x7609cc]` w4  host `0x7609cb B8 0C 00 00 00  mov eax, 0xc`  plugin-reset `12`
- `0x100c0643` -> `[0x7609c7]` w4  host `0x7609c6 BA 0F 00 00 00  mov edx, 0xf`  plugin-reset `15`
- `0x100c06e3` -> `[0x7609d8]` w4  host `0x7609d7 B8 18 00 00 00  mov eax, 0x18`  plugin-reset `24`
- `0x100c0783` -> `[0x7608f0]` w4  host `0x7608ef B8 0A 00 00 00  mov eax, 0xa`  plugin-reset `10`
- `0x100c0823` -> `[0x783f5a]` w4  host `0x783f59 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c08c3` -> `[0x783f55]` w4  host `0x783f54 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c0969` -> `[0x783f66]` w4  host `0x783f65 B8 14 00 00 00  mov eax, 0x14`  plugin-reset `30`
- `0x100c0a1c` -> `[0x783f7d]` w4  host `0x783f7c B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c0acc` -> `[0x783f78]` w4  host `0x783f77 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c0b7c` -> `[0x783f89]` w4  host `0x783f88 B8 14 00 00 00  mov eax, 0x14`  plugin-reset `30`
- `0x100c0c2c` -> `[0x783fa0]` w4  host `0x783f9f B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c0cdc` -> `[0x783f9b]` w4  host `0x783f9a BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c0d8c` -> `[0x783fac]` w4  host `0x783fab B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `30`
- `0x100c0e3c` -> `[0x783fc3]` w4  host `0x783fc2 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c0eec` -> `[0x783fbe]` w4  host `0x783fbd BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c0f9c` -> `[0x783fcf]` w4  host `0x783fce B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `20`
- `0x100c104c` -> `[0x783fe6]` w4  host `0x783fe5 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c10fc` -> `[0x783fe1]` w4  host `0x783fe0 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c11ac` -> `[0x783ff2]` w4  host `0x783ff1 B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `20`
- `0x100c125c` -> `[0x7639f3]` w4  host `0x7639f2 B8 0A 00 00 00  mov eax, 0xa`  plugin-reset `10`
- `0x100c130c` -> `[0x76122b]` w4  host `0x76122a B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c13bc` -> `[0x761226]` w4  host `0x761225 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c146c` -> `[0x761237]` w4  host `0x761236 B8 14 00 00 00  mov eax, 0x14`  plugin-reset `30`
- `0x100c151c` -> `[0x76125d]` w4  host `0x76125c B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c15cc` -> `[0x761258]` w4  host `0x761257 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c167c` -> `[0x761269]` w4  host `0x761268 B8 14 00 00 00  mov eax, 0x14`  plugin-reset `30`
- `0x100c172c` -> `[0x761280]` w4  host `0x76127f B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c17dc` -> `[0x76127b]` w4  host `0x76127a BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c188c` -> `[0x76128c]` w4  host `0x76128b B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `30`
- `0x100c193c` -> `[0x7612a3]` w4  host `0x7612a2 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c19ec` -> `[0x76129e]` w4  host `0x76129d BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c1a9c` -> `[0x7612af]` w4  host `0x7612ae B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `20`
- `0x100c1b4c` -> `[0x7612c6]` w4  host `0x7612c5 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c1bfc` -> `[0x7612c1]` w4  host `0x7612c0 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c1cac` -> `[0x7612d2]` w4  host `0x7612d1 B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `20`
- `0x100c1d5c` -> `[0x7611fb]` w4  host `0x7611fa B8 0A 00 00 00  mov eax, 0xa`  plugin-reset `10`
- `0x100c1e0c` -> `[0x7617d6]` w4  host `0x7617d5 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c1ebc` -> `[0x7617d1]` w4  host `0x7617d0 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c1f6c` -> `[0x7617e2]` w4  host `0x7617e1 B8 28 00 00 00  mov eax, 0x28`  plugin-reset `30`
- `0x100c201c` -> `[0x7617f9]` w4  host `0x7617f8 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c20cc` -> `[0x7617f4]` w4  host `0x7617f3 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c217c` -> `[0x761805]` w4  host `0x761804 B8 28 00 00 00  mov eax, 0x28`  plugin-reset `30`
- `0x100c222c` -> `[0x76181c]` w4  host `0x76181b B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c22dc` -> `[0x761817]` w4  host `0x761816 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c238c` -> `[0x761828]` w4  host `0x761827 B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `30`
- `0x100c243c` -> `[0x76183f]` w4  host `0x76183e B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c24ec` -> `[0x76183a]` w4  host `0x761839 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c259c` -> `[0x76184b]` w4  host `0x76184a B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `40`
- `0x100c264c` -> `[0x761862]` w4  host `0x761861 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c26fc` -> `[0x76185d]` w4  host `0x76185c BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c27ac` -> `[0x76186e]` w4  host `0x76186d B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `40`
- `0x100c285c` -> `[0x7617a3]` w4  host `0x7617a2 B8 0A 00 00 00  mov eax, 0xa`  plugin-reset `10`
- `0x100c290c` -> `[0x76261a]` w4  host `0x762619 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c29bc` -> `[0x762615]` w4  host `0x762614 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c2a6c` -> `[0x762626]` w4  host `0x762625 B8 14 00 00 00  mov eax, 0x14`  plugin-reset `30`
- `0x100c2b1c` -> `[0x76263d]` w4  host `0x76263c B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c2bcc` -> `[0x762638]` w4  host `0x762637 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c2c7c` -> `[0x762649]` w4  host `0x762648 B8 14 00 00 00  mov eax, 0x14`  plugin-reset `30`
- `0x100c2d2c` -> `[0x762660]` w4  host `0x76265f B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c2ddc` -> `[0x76265b]` w4  host `0x76265a BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c2e8c` -> `[0x76266c]` w4  host `0x76266b B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `30`
- `0x100c2f3c` -> `[0x762683]` w4  host `0x762682 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c2fec` -> `[0x76267e]` w4  host `0x76267d BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c309c` -> `[0x76268f]` w4  host `0x76268e B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `20`
- `0x100c314c` -> `[0x7626a6]` w4  host `0x7626a5 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c31fc` -> `[0x7626a1]` w4  host `0x7626a0 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c32ac` -> `[0x7626b2]` w4  host `0x7626b1 B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `20`
- `0x100c335c` -> `[0x7625e8]` w4  host `0x7625e7 B8 0A 00 00 00  mov eax, 0xa`  plugin-reset `10`
- `0x100c340c` -> `[0x761d22]` w4  host `0x761d21 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c34bc` -> `[0x761d1d]` w4  host `0x761d1c BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c356c` -> `[0x761d2e]` w4  host `0x761d2d B8 14 00 00 00  mov eax, 0x14`  plugin-reset `30`
- `0x100c361c` -> `[0x761d45]` w4  host `0x761d44 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c36cc` -> `[0x761d40]` w4  host `0x761d3f BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c377c` -> `[0x761d51]` w4  host `0x761d50 B8 14 00 00 00  mov eax, 0x14`  plugin-reset `30`
- `0x100c382c` -> `[0x761d68]` w4  host `0x761d67 B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c38dc` -> `[0x761d63]` w4  host `0x761d62 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c398c` -> `[0x761d74]` w4  host `0x761d73 B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `30`
- `0x100c3a3c` -> `[0x761d8b]` w4  host `0x761d8a B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c3aec` -> `[0x761d86]` w4  host `0x761d85 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`
- `0x100c3b9c` -> `[0x761d97]` w4  host `0x761d96 B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `20`
- `0x100c3c4c` -> `[0x761dae]` w4  host `0x761dad B8 06 00 00 00  mov eax, 6`  plugin-reset `6`
- `0x100c3cfc` -> `[0x761da9]` w4  host `0x761da8 BA 14 00 00 00  mov edx, 0x14`  plugin-reset `20`

### 人物爆率调整
- `0x100b9ccc` -> `[0x73fcbb]` w4  host `0x73fcb8 C7 45 F8 15 00 00 00  mov dword ptr [ebp - 8], 0x15`  plugin-reset `None`
- `0x100b9d3a` -> `[0x73ff6c]` w1  host `0x73ff69 83 7D F4 02  cmp dword ptr [ebp - 0xc], 2`  plugin-reset `None`

### 刺杀剑术
- `0x100b40bb` -> `[0x771c50]` w1  host `0x771c4f C0 02 89  rol byte ptr [edx], 0x89`  plugin-reset `None`
- `0x100b4129` -> `[0x771d24]` w4  host `0x771d23 00 00  add byte ptr [eax], al`  plugin-reset `None`

### 半月弯刀
- `0x100b42f2` -> `[0x772046]` w1  host `0x772045 C0 02 89  rol byte ptr [edx], 0x89`  plugin-reset `None`
- `0x100b4360` -> `[0x772148]` w4  host `0x772147 00 00  add byte ptr [eax], al`  plugin-reset `None`

### 复活戒指重设
- `0x100b357b` -> `[0x743758]` w4  host `0x743756 81 FA 60 EA 00 00  cmp edx, 0xea60`  plugin-reset `None`
- `0x100b35e9` -> `[0x73c480]` w4  host `0x73c47f B8 3C 00 00 00  mov eax, 0x3c`  plugin-reset `None`

### 战士合击
- `0x100b8bbd` -> `[0x7d341c]` w4  host `None`  plugin-reset `None`
- `0x100b8bc9` -> `[0x7d3420]` w4  host `0x7d341f 66 66 66 06  push es`  plugin-reset `None`

### 攻城修改
- `0x100b32c9` -> `[0x65c3b1]` w4  host `0x65c3b0 B8 58 2E 01 00  mov eax, 0x12e58`  plugin-reset `None`
- `0x100b3332` -> `[0x65bc09]` w4  host `0x65bc08 3D 58 2E 01 00  cmp eax, 0x12e58`  plugin-reset `None`
- `0x100b339b` -> `[0x65be2c]` w4  host `0x65be2b 3D 58 2E 01 00  cmp eax, 0x12e58`  plugin-reset `None`

### 攻杀剑术
- `0x100b3f5a` -> `[0x76b02d]` w1  host `0x76b02c 04 05  add al, 5`  plugin-reset `None`

### 无极真气
- `0x100b83c9` -> `[0x74587c]` w4  host `0x74587b FF 0B  dec dword ptr [ebx]`  plugin-reset `None`

### 法道合击
- `0x100b8fe1` -> `[0x7d3298]` w4  host `None`  plugin-reset `None`
- `0x100b8fed` -> `[0x7d329c]` w4  host `0x7d329b 33 33  xor esi, dword ptr [ebx]`  plugin-reset `None`

### 烈火剑法
- `0x100b4550` -> `[0x76b0f0]` w1  host `0x76b0ef 04 04  add al, 4`  plugin-reset `None`

### 爆物随机极品
- `0x100c3dac` -> `[0x761dba]` w4  host `0x761db9 B8 1E 00 00 00  mov eax, 0x1e`  plugin-reset `20`
- `0x100c3e5c` -> `[0x761cf0]` w4  host `0x761cef B8 09 00 00 00  mov eax, 9`  plugin-reset `10`

### 盘古冰咆哮的范围
- `0x100b3d34` -> `[0x76f301]` w1  host `0x76f300 6A 01  push 1`  plugin-reset `None`

### 盘古地狱雷光范围
- `0x100b3c21` -> `[0x76f643]` w1  host `0x76f642 6A 02  push 2`  plugin-reset `None`

### 盘古流星火雨范围
- `0x100b3e47` -> `[0x76f3be]` w1  host `0x76f3bd 6A 01  push 1`  plugin-reset `None`

### 盘古爆裂火焰范围
- `0x100b3b06` -> `[0x76f271]` w1  host `0x76f270 6A 01  push 1`  plugin-reset `None`

### 逐日剑法
- `0x100b47d9` -> `[0x76b14d]` w1  host `0x76b14c 8A 80 28 4B 7D 00  mov al, byte ptr [eax + 0x7d4b28]`  plugin-reset `None`
- `0x100b4847` -> `[0x771da4]` w4  host `0x771da3 B9 0A 00 00 00  mov ecx, 0xa`  plugin-reset `None`
