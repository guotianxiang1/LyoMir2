# 眼神 2.0.8 GUI 功能总表

生成脚本 `tools/ys_gui_matrix.py`；配置 `D:\光头卧龙\mud2.0\Mir200\Gs1\config.json`（gbk，380 键）。

| 状态 | 计数 |
|---|---|
| IMPLEMENTED | 73 |
| SCRIPT_ONLY | 22 |
| LABEL_ONLY | 284 |
| MISSING | 1 |
| INVENTED | 1 |

生产已开启（值 != 0）215 键；其中无行为的 172 键。

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
| `噬魂沼泽绿毒修复` | 1 | 盘古2 | LABEL_ONLY | `0x102b0558` | 2 | `0x691e2e` |
| `攻城修改` | 1 | 盘古2 | LABEL_ONLY | `0x102b059c` | 2 | `0x65bc09` `0x65be2c` `0x65c3b1` |
| `攻城修改_天数` | 3 | 盘古2 | LABEL_ONLY | `0x102b0a78` | 3 | - |
| `攻城修改_小时` | 20 | 盘古2 | LABEL_ONLY | `0x102b0a88` | 3 | - |
| `攻城时长_分钟` | 120 | 盘古2 | LABEL_ONLY | `0x102b0aa8` | 3 | - |
| `武器绿毒` | 1 | 盘古2 | LABEL_ONLY | `0x102b054c` | 2 | `0x76e2bc` |
| `火墙_时间` | 120 | 盘古2 | LABEL_ONLY | `0x102b0ab8` | 3 | - |
| `盘古冰咆哮的范围` | 1 | 盘古2 | LABEL_ONLY | `0x102b0608` | 2 | `0x76f301` |
| `盘古冰咆哮的范围_范围值` | 3 | 盘古2 | LABEL_ONLY | `0x102b07d8` | 3 | - |
| `盘古地狱雷光范围` | 1 | 盘古2 | LABEL_ONLY | `0x102b05f4` | 2 | `0x76f643` |
| `盘古地狱雷光范围_范围值` | 3 | 盘古2 | LABEL_ONLY | `0x102b07c0` | 3 | - |
| `盘古流星火雨范围` | 1 | 盘古2 | LABEL_ONLY | `0x102b061c` | 2 | `0x76f3be` |
| `盘古流星火雨范围_范围值` | 4 | 盘古2 | LABEL_ONLY | `0x102b07f0` | 3 | - |
| `盘古爆裂火焰范围` | 1 | 盘古2 | LABEL_ONLY | `0x102b05e0` | 2 | `0x76f271` |
| `盘古爆裂火焰范围_范围值` | 2 | 盘古2 | LABEL_ONLY | `0x102b07a8` | 3 | - |
| `等级禁言` | 1 | 盘古2 | LABEL_ONLY | `0x102b1688` | 2 | - |
| `中毒时间上限` | 1 | 盘古3 | LABEL_ONLY | `0x102b06dc` | 2 | `0x76e5ce` `0x76e675` |
| `中毒时间上限_秒` | "60" | 盘古3 | LABEL_ONLY | `0x102b08c8` | 3 | - |
| `人物等级1_值` | 40 | 盘古3 | LABEL_ONLY | `0x102b1494` | 3 | - |
| `人物等级2_值` | 45 | 盘古3 | LABEL_ONLY | `0x102b14a4` | 3 | - |
| `人物等级3_值` | 48 | 盘古3 | LABEL_ONLY | `0x102b14b4` | 3 | - |
| `修改召唤神兽` | 1 | 盘古3 | LABEL_ONLY | `0x102b1658` | 2 | - |
| `怪物名字1_值` | "强化神兽" | 盘古3 | LABEL_ONLY | `0x102b14c4` | 3 | - |
| `怪物名字2_值` | "强化神兽" | 盘古3 | LABEL_ONLY | `0x102b14d4` | 3 | - |
| `怪物名字3_值` | "白虎" | 盘古3 | LABEL_ONLY | `0x102b14e4` | 3 | - |
| `怪物数量1_值` | 1 | 盘古3 | LABEL_ONLY | `0x102b14f4` | 3 | - |
| `怪物数量2_值` | 2 | 盘古3 | LABEL_ONLY | `0x102b1504` | 3 | - |
| `怪物数量3_值` | 1 | 盘古3 | LABEL_ONLY | `0x102b1514` | 3 | - |
| `战士合击` | 1 | 盘古3 | LABEL_ONLY | `0x102b06f4` | 2 | `0x7d341c` `0x7d3420` |
| `战士合击_数值1` | "1.5" | 盘古3 | LABEL_ONLY | `0x102b08e8` | 3 | - |
| `战士合击_数值2` | "2" | 盘古3 | LABEL_ONLY | `0x102b08f8` | 3 | - |
| `战士合击_数值3` | "2.4" | 盘古3 | LABEL_ONLY | `0x102b0908` | 3 | - |
| `战士合击_数值4` | "2.6" | 盘古3 | LABEL_ONLY | `0x102b0918` | 3 | - |
| `战士合击_数值5` | "2.8" | 盘古3 | LABEL_ONLY | `0x102b0928` | 3 | - |
| `施毒术_公式值` | "10" | 盘古3 | LABEL_ONLY | `0x102b08d8` | 3 | - |
| `无极真气` | 1 | 盘古3 | LABEL_ONLY | `0x102b06d0` | 2 | `0x74587c` |
| `无极真气_A值` | "10" | 盘古3 | LABEL_ONLY | `0x102b08a8` | 3 | - |
| `无极真气_时间` | "10" | 盘古3 | LABEL_ONLY | `0x102b08b8` | 3 | - |
| `法道合击` | 1 | 盘古3 | LABEL_ONLY | `0x102b0700` | 2 | `0x7d3298` `0x7d329c` |
| `装备吸血` | 1 | 盘古3 | LABEL_ONLY | `0x102b0630` | 2 | `0x76e2a3` |
| `装备提升人物爆率` | 1 | 盘古3 | LABEL_ONLY | `0x102b0718` | 2 | `0x71fd37` |
| `装备提升人物爆率_A值` | "10" | 盘古3 | LABEL_ONLY | `0x102b0988` | 3 | - |
| `装备提升人物爆率_B值` | "10" | 盘古3 | LABEL_ONLY | `0x102b09a0` | 3 | - |
| `头盔属性几率_准确_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0f44` | 3 | `0x7612d2` |
| `头盔属性几率_攻击_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0e24` | 3 | `0x761237` |
| `头盔属性几率_攻速_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0efc` | 3 | `0x7612af` |
| `头盔属性几率_道术_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0eb4` | 3 | `0x76128c` |
| `头盔属性几率_魔法_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0e6c` | 3 | `0x761269` |
| `头盔最随机性_极品_值` | "80" | 盘古4 | LABEL_ONLY | `0x102b0f5c` | 3 | `0x7611fb` |
| `头盔最高点数_准确_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0f14` | 3 | `0x7612c6` |
| `头盔最高点数_攻击_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0df4` | 3 | `0x76122b` |
| `头盔最高点数_攻速_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0ecc` | 3 | `0x7612a3` |
| `头盔最高点数_道术_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0e84` | 3 | `0x761280` |
| `头盔最高点数_魔法_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0e3c` | 3 | `0x76125d` |
| `头盔点数几率_准确_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0f2c` | 3 | `0x7612c1` |
| `头盔点数几率_攻击_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0e0c` | 3 | `0x761226` |
| `头盔点数几率_攻速_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0ee4` | 3 | `0x76129e` |
| `头盔点数几率_道术_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0e9c` | 3 | `0x76127b` |
| `头盔点数几率_魔法_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0e54` | 3 | `0x761258` |
| `戒指属性几率_准确_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b13c4` | 3 | `0x761dba` |
| `戒指属性几率_攻击_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b12a4` | 3 | `0x761d2e` |
| `戒指属性几率_攻速_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b137c` | 3 | `0x761d97` |
| `戒指属性几率_道术_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b1334` | 3 | `0x761d74` |
| `戒指属性几率_魔法_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b12ec` | 3 | `0x761d51` |
| `戒指最随机性_极品_值` | "80" | 盘古4 | LABEL_ONLY | `0x102b13dc` | 3 | `0x761cf0` |
| `戒指最高点数_准确_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b1394` | 3 | `0x761dae` |
| `戒指最高点数_攻速_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b134c` | 3 | `0x761d8b` |
| `戒指最高点数_道术_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b1304` | 3 | `0x761d68` |
| `戒指点数几率_准确_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b13ac` | 3 | `0x761da9` |
| `戒指点数几率_攻击_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b128c` | 3 | `0x761d1d` |
| `戒指点数几率_攻速_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b1364` | 3 | `0x761d86` |
| `戒指点数几率_道术_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b131c` | 3 | `0x761d63` |
| `戒指点数几率_魔法_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b12d4` | 3 | `0x761d40` |
| `手镯属性几率_准确_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b1244` | 3 | `0x7626b2` |
| `手镯属性几率_攻击_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b1124` | 3 | `0x762626` |
| `手镯属性几率_攻速_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b11fc` | 3 | `0x76268f` |
| `手镯属性几率_道术_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b11b4` | 3 | `0x76266c` |
| `手镯属性几率_魔法_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b116c` | 3 | `0x762649` |
| `手镯最随机性_极品_值` | "80" | 盘古4 | LABEL_ONLY | `0x102b125c` | 3 | `0x7625e8` |
| `手镯最高点数_准确_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b1214` | 3 | `0x7626a6` |
| `手镯最高点数_攻速_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b11cc` | 3 | `0x762683` |
| `手镯最高点数_道术_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b1184` | 3 | `0x762660` |
| `手镯点数几率_准确_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b122c` | 3 | `0x7626a1` |
| `手镯点数几率_攻击_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b110c` | 3 | `0x762615` |
| `手镯点数几率_攻速_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b11e4` | 3 | `0x76267e` |
| `手镯点数几率_道术_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b119c` | 3 | `0x76265b` |
| `手镯点数几率_魔法_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b1154` | 3 | `0x762638` |
| `武器属性几率_准确_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0c44` | 3 | `0x7609d8` |
| `武器属性几率_攻击_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0b24` | 3 | `0x760920` |
| `武器属性几率_攻速_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0bfc` | 3 | `0x76098f` |
| `武器属性几率_道术_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0bb4` | 3 | `0x76096a` |
| `武器属性几率_魔法_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0b6c` | 3 | `0x760945` |
| `武器最随机性_极品_值` | "80" | 盘古4 | LABEL_ONLY | `0x102b0c5c` | 3 | `0x7608f0` |
| `武器最高点数_准确_值` | "7" | 盘古4 | LABEL_ONLY | `0x102b0c14` | 3 | `0x7609cc` |
| `武器最高点数_攻速_值` | "7" | 盘古4 | LABEL_ONLY | `0x102b0bcc` | 3 | `0x760983` |
| `武器最高点数_道术_值` | "7" | 盘古4 | LABEL_ONLY | `0x102b0b84` | 3 | `0x76095e` |
| `武器最高点数_魔法_值` | "7" | 盘古4 | LABEL_ONLY | `0x102b0b3c` | 3 | `0x760939` |
| `武器点数几率_准确_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0c2c` | 3 | `0x7609c7` |
| `武器点数几率_攻击_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0b0c` | 3 | `0x76090f` |
| `武器点数几率_攻速_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0be4` | 3 | `0x76097e` |
| `武器点数几率_道术_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0b9c` | 3 | `0x760959` |
| `武器点数几率_魔法_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0b54` | 3 | `0x760934` |
| `衣服属性几率_准确_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0dc4` | 3 | `0x783ff2` |
| `衣服属性几率_攻击_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0ca4` | 3 | `0x783f66` |
| `衣服属性几率_攻速_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0d7c` | 3 | `0x783fcf` |
| `衣服属性几率_道术_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0d34` | 3 | `0x783fac` |
| `衣服属性几率_魔法_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0cec` | 3 | `0x783f89` |
| `衣服最随机性_极品_值` | "80" | 盘古4 | LABEL_ONLY | `0x102b0ddc` | 3 | `0x7639f3` |
| `衣服最高点数_准确_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0d94` | 3 | `0x783fe6` |
| `衣服最高点数_攻击_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0c74` | 3 | `0x783f5a` |
| `衣服最高点数_攻速_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0d4c` | 3 | `0x783fc3` |
| `衣服最高点数_道术_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0d04` | 3 | `0x783fa0` |
| `衣服最高点数_魔法_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0cbc` | 3 | `0x783f7d` |
| `衣服点数几率_准确_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0dac` | 3 | `0x783fe1` |
| `衣服点数几率_攻击_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0c8c` | 3 | `0x783f55` |
| `衣服点数几率_攻速_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0d64` | 3 | `0x783fbe` |
| `衣服点数几率_道术_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0d1c` | 3 | `0x783f9b` |
| `衣服点数几率_魔法_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b0cd4` | 3 | `0x783f78` |
| `随机极品` | 1 | 盘古4 | LABEL_ONLY | `0x102b073c` | 8 | - |
| `项链属性几率_准确_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b10c4` | 3 | `0x76186e` |
| `项链属性几率_攻击_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0fa4` | 3 | `0x7617e2` |
| `项链属性几率_攻速_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b107c` | 3 | `0x76184b` |
| `项链属性几率_道术_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b1034` | 3 | `0x761828` |
| `项链属性几率_魔法_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0fec` | 3 | `0x761805` |
| `项链最随机性_极品_值` | "80" | 盘古4 | LABEL_ONLY | `0x102b10dc` | 3 | `0x7617a3` |
| `项链最高点数_准确_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b1094` | 3 | `0x761862` |
| `项链最高点数_攻击_值` | "3" | 盘古4 | LABEL_ONLY | `0x102b0f74` | 3 | `0x7617d6` |
| `项链最高点数_攻速_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b104c` | 3 | `0x76183f` |
| `项链最高点数_道术_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b1004` | 3 | `0x76181c` |
| `项链最高点数_魔法_值` | "2" | 盘古4 | LABEL_ONLY | `0x102b0fbc` | 3 | `0x7617f9` |
| `项链点数几率_准确_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b10ac` | 3 | `0x76185d` |
| `项链点数几率_攻击_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0f8c` | 3 | `0x7617d1` |
| `项链点数几率_攻速_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b1064` | 3 | `0x76183a` |
| `项链点数几率_道术_值` | "5" | 盘古4 | LABEL_ONLY | `0x102b101c` | 3 | `0x761817` |
| `项链点数几率_魔法_值` | "10" | 盘古4 | LABEL_ONLY | `0x102b0fd4` | 3 | `0x7617f4` |
| `主号高级暴击` | 1 | 眼神2(第1页) | LABEL_ONLY | `0x102b1578` | 2 | - |
| `技能等级突破` | 1 | 眼神2(第1页) | LABEL_ONLY | `0x102b0210` | 2 | - |
| `技能等级突破_最大值` | 255 | 眼神2(第1页) | LABEL_ONLY | `0x102b0a40` | 2 | - |
| `循环时间_值` | 2000 | 眼神2(第2页) | LABEL_ONLY | `0x102b1488` | 3 | - |
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
| `关闭摆摊` | 0 |  | toggle | LABEL_ONLY | IsCloseStall |
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
| `盘古魔法攻击触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `神兽_序号` | 0 |  | value | LABEL_ONLY | ShenShouIdx |
| `神兽_数量` | 1 | Y | value | LABEL_ONLY | NativeSlaveCountImm8 |
| `禁止交易地图` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Players/TPlayObject.Operate.cs |
| `禁止宝宝休息` | 0 |  | toggle | IMPLEMENTED | GameSvr/Command/Commands/ChangeSalveStatusCommand.cs |
| `穿人穿怪` | 0 |  | toggle | LABEL_ONLY | IsPassThrough |
| `脚本控制头发外显` | 1 | Y | toggle | LABEL_ONLY | IsScriptHair; IsScriptHairEnabled |
| `行会显示` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Players/TPlayObject.Base.cs; GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `踢玩家下线` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
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
| `半月带毒` | 0 |  | toggle | LABEL_ONLY | IsHalfMoonPoison |
| `半月弯刀` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `半月弯刀_A值` | "2" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `半月弯刀_B值` | "15" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `名字变色` | 1 | Y | toggle | LABEL_ONLY | IsNameColor |
| `噬魂沼泽绿毒修复` | 1 | Y | toggle | LABEL_ONLY | IsZhaoZeFix |
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
| `武器绿毒` | 1 | Y | toggle | LABEL_ONLY | IsWeaponGreenPoison |
| `法师群毒` | 0 |  | toggle | LABEL_ONLY | IsMageGroupPoison; IsMageGroupPoisonEnabled |
| `火墙_时间` | 120 | Y | value | LABEL_ONLY | FireWallTime |
| `火墙设置时间上限` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `烈火剑法` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `烈火剑法_A值` | "4" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `烈火剑法_B值` | "3" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `物功带毒` | 0 |  | toggle | LABEL_ONLY | IsPhysicalPoison |
| `盘古冰咆哮的范围` | 1 | Y | toggle | LABEL_ONLY | IsPgIceStormRange |
| `盘古冰咆哮的范围_范围值` | 3 | Y | value | LABEL_ONLY | PgIceStormRangeVal |
| `盘古地狱雷光范围` | 1 | Y | toggle | LABEL_ONLY | IsPgHellLightRange |
| `盘古地狱雷光范围_范围值` | 3 | Y | value | LABEL_ONLY | PgHellLightRangeVal |
| `盘古流星火雨范围` | 1 | Y | toggle | LABEL_ONLY | IsPgFireRainRange |
| `盘古流星火雨范围_范围值` | 4 | Y | value | LABEL_ONLY | PgFireRainRangeVal |
| `盘古爆裂火焰范围` | 1 | Y | toggle | LABEL_ONLY | IsPgBlastFlameRange |
| `盘古爆裂火焰范围_范围值` | 2 | Y | value | LABEL_ONLY | PgBlastFlameRangeVal |
| `破复活` | 0 |  | toggle | LABEL_ONLY | IsBreakRevival |
| `等级禁言` | 1 | Y | toggle | LABEL_ONLY | IsLevelMute; IsMailAntiSpamEnabled |
| `设置玩家称号函数` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `逐日剑法` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `逐日剑法_A值` | "6" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `逐日剑法_B值` | "3" | Y | value | IMPLEMENTED | GameSvr/Actors/TBaseObject.Attack.cs |
| `野蛮麻痹` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `雷电带毒` | 0 |  | toggle | LABEL_ONLY | IsLightningPoison |

### 盘古3（39）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `中毒时间上限` | 1 | Y | toggle | LABEL_ONLY | IsPoisonTimeLimit |
| `中毒时间上限_秒` | "60" | Y | value | LABEL_ONLY | PoisonTimeLimitSec |
| `人物爆率调整` | 0 |  | toggle | IMPLEMENTED | GameSvr/Actors/TBaseObject.Base.cs; GameSvr/Players/TPlayObject.Message.cs |
| `人物等级1_值` | 40 | Y | value | LABEL_ONLY | IsCustomDmgPlus; PlayerLv1 |
| `人物等级2_值` | 45 | Y | value | LABEL_ONLY | IsCustomDmgPlus; PlayerLv2 |
| `人物等级3_值` | 48 | Y | value | LABEL_ONLY | IsCustomDmgPlus; PlayerLv3 |
| `修改召唤神兽` | 1 | Y | toggle | LABEL_ONLY | IsModifyShenShou; IsModifyShenShouEnabled |
| `屏蔽排行榜` | 0 |  | toggle | LABEL_ONLY | IsHideRank; ShouldHideRank |
| `怪物名字1_值` | "强化神兽" | Y | value | LABEL_ONLY | IsCustomDmgPlus; MonsterName1 |
| `怪物名字2_值` | "强化神兽" | Y | value | LABEL_ONLY | IsCustomDmgPlus; MonsterName2 |
| `怪物名字3_值` | "白虎" | Y | value | LABEL_ONLY | IsCustomDmgPlus; MonsterName3 |
| `怪物数量1_值` | 1 | Y | value | LABEL_ONLY | IsCustomDmgPlus; MonsterCount1; MonsterName3 |
| `怪物数量2_值` | 2 | Y | value | LABEL_ONLY | IsCustomDmgPlus; MonsterCount2 |
| `怪物数量3_值` | 1 | Y | value | LABEL_ONLY | IsCustomDmgPlus; MonsterCount3 |
| `战士合击` | 1 | Y | toggle | LABEL_ONLY | IsWarriorCombo; IsWarriorComboEnabled |
| `战士合击_数值1` | "1.5" | Y | value | LABEL_ONLY | WarriorComboV1 |
| `战士合击_数值2` | "2" | Y | value | LABEL_ONLY | WarriorComboV2 |
| `战士合击_数值3` | "2.4" | Y | value | LABEL_ONLY | WarriorComboV3 |
| `战士合击_数值4` | "2.6" | Y | value | LABEL_ONLY | WarriorComboV4 |
| `战士合击_数值5` | "2.8" | Y | value | LABEL_ONLY | WarriorComboV5 |
| `施毒术` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `施毒术_公式值` | "10" | Y | value | LABEL_ONLY | PoisonFormulaVal |
| `无极真气` | 1 | Y | toggle | LABEL_ONLY | IsZhenQi |
| `无极真气_A值` | "10" | Y | value | LABEL_ONLY | ZhenQiA |
| `无极真气_时间` | "10" | Y | value | LABEL_ONLY | ZhenQiTime |
| `最大装备数量` | "" |  | value | LABEL_ONLY | MaxEquipCount |
| `法道合击` | 1 | Y | toggle | LABEL_ONLY | IsWizTaoCombo; IsWizTaoComboEnabled |
| `法道合击_数值1` | "" |  | value | LABEL_ONLY | WizTaoComboV1 |
| `法道合击_数值2` | "" |  | value | LABEL_ONLY | WizTaoComboV2 |
| `法道合击_数值3` | "" |  | value | LABEL_ONLY | WizTaoComboV3 |
| `法道合击_数值4` | "" |  | value | LABEL_ONLY | WizTaoComboV4 |
| `法道合击_数值5` | "" |  | value | LABEL_ONLY | WizTaoComboV5 |
| `红名K值` | "" |  | value | LABEL_ONLY | RedNameK |
| `脚本控制人物爆率` | 0 |  | toggle | LABEL_ONLY | IsScriptDropRate; IsScriptDropRateEnabled |
| `装备吸血` | 1 | Y | toggle | LABEL_ONLY | IsEquipSteal |
| `装备提升人物爆率` | 1 | Y | toggle | LABEL_ONLY | IsBoostDropRate |
| `装备提升人物爆率_A值` | "10" | Y | value | LABEL_ONLY | BoostDropRateA |
| `装备提升人物爆率_B值` | "10" | Y | value | LABEL_ONLY | BoostDropRateB |
| `非红名K值` | "" |  | value | LABEL_ONLY | NormalK |

### 盘古4（98）

| 键 | 值 | 开 | 控件 | 状态 | C# 行为落点 / API 成员 |
|---|---|---|---|---|---|
| `头盔属性几率_准确_值` | "10" | Y | value | LABEL_ONLY | HelmetAttrChance_Acc |
| `头盔属性几率_攻击_值` | "10" | Y | value | LABEL_ONLY | HelmetAttrChance_Atk |
| `头盔属性几率_攻速_值` | "10" | Y | value | LABEL_ONLY | HelmetAttrChance_Spd |
| `头盔属性几率_道术_值` | "10" | Y | value | LABEL_ONLY | HelmetAttrChance_Tao |
| `头盔属性几率_魔法_值` | "10" | Y | value | LABEL_ONLY | HelmetAttrChance_Mgc |
| `头盔最随机性_极品_值` | "80" | Y | value | LABEL_ONLY | HelmetRandExtreme |
| `头盔最高点数_准确_值` | "10" | Y | value | LABEL_ONLY | HelmetMaxPts_Acc |
| `头盔最高点数_攻击_值` | "10" | Y | value | LABEL_ONLY | HelmetMaxPts_Atk |
| `头盔最高点数_攻速_值` | "10" | Y | value | LABEL_ONLY | HelmetMaxPts_Spd |
| `头盔最高点数_道术_值` | "10" | Y | value | LABEL_ONLY | HelmetMaxPts_Tao |
| `头盔最高点数_魔法_值` | "10" | Y | value | LABEL_ONLY | HelmetMaxPts_Mgc |
| `头盔点数几率_准确_值` | "5" | Y | value | LABEL_ONLY | HelmetPtsChance_Acc |
| `头盔点数几率_攻击_值` | "5" | Y | value | LABEL_ONLY | HelmetPtsChance_Atk |
| `头盔点数几率_攻速_值` | "5" | Y | value | LABEL_ONLY | HelmetPtsChance_Spd |
| `头盔点数几率_道术_值` | "5" | Y | value | LABEL_ONLY | HelmetPtsChance_Tao |
| `头盔点数几率_魔法_值` | "5" | Y | value | LABEL_ONLY | HelmetPtsChance_Mgc |
| `屏蔽自动绑定` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `戒指属性几率_准确_值` | "10" | Y | value | LABEL_ONLY | RingAttrChance_Acc |
| `戒指属性几率_攻击_值` | "10" | Y | value | LABEL_ONLY | RingAttrChance_Atk |
| `戒指属性几率_攻速_值` | "10" | Y | value | LABEL_ONLY | RingAttrChance_Spd |
| `戒指属性几率_道术_值` | "10" | Y | value | LABEL_ONLY | RingAttrChance_Tao |
| `戒指属性几率_魔法_值` | "10" | Y | value | LABEL_ONLY | RingAttrChance_Mgc |
| `戒指最随机性_极品_值` | "80" | Y | value | LABEL_ONLY | RingRandExtreme |
| `戒指最高点数_准确_值` | "10" | Y | value | LABEL_ONLY | RingMaxPts_Acc |
| `戒指最高点数_攻击_值` | "" |  | value | LABEL_ONLY | RingMaxPts_Atk |
| `戒指最高点数_攻速_值` | "10" | Y | value | LABEL_ONLY | RingMaxPts_Spd |
| `戒指最高点数_道术_值` | "10" | Y | value | LABEL_ONLY | RingMaxPts_Tao |
| `戒指最高点数_魔法_值` | "" |  | value | LABEL_ONLY | RingMaxPts_Mgc |
| `戒指点数几率_准确_值` | "5" | Y | value | LABEL_ONLY | RingPtsChance_Acc |
| `戒指点数几率_攻击_值` | "5" | Y | value | LABEL_ONLY | RingPtsChance_Atk |
| `戒指点数几率_攻速_值` | "5" | Y | value | LABEL_ONLY | RingPtsChance_Spd |
| `戒指点数几率_道术_值` | "5" | Y | value | LABEL_ONLY | RingPtsChance_Tao |
| `戒指点数几率_魔法_值` | "5" | Y | value | LABEL_ONLY | RingPtsChance_Mgc |
| `手镯属性几率_准确_值` | "10" | Y | value | LABEL_ONLY | BraceletAttrChance_Acc |
| `手镯属性几率_攻击_值` | "10" | Y | value | LABEL_ONLY | BraceletAttrChance_Atk |
| `手镯属性几率_攻速_值` | "10" | Y | value | LABEL_ONLY | BraceletAttrChance_Spd |
| `手镯属性几率_道术_值` | "10" | Y | value | LABEL_ONLY | BraceletAttrChance_Tao |
| `手镯属性几率_魔法_值` | "10" | Y | value | LABEL_ONLY | BraceletAttrChance_Mgc |
| `手镯最随机性_极品_值` | "80" | Y | value | LABEL_ONLY | BraceletRandExtreme |
| `手镯最高点数_准确_值` | "10" | Y | value | LABEL_ONLY | BraceletMaxPts_Acc |
| `手镯最高点数_攻击_值` | "" |  | value | LABEL_ONLY | BraceletMaxPts_Atk |
| `手镯最高点数_攻速_值` | "10" | Y | value | LABEL_ONLY | BraceletMaxPts_Spd |
| `手镯最高点数_道术_值` | "10" | Y | value | LABEL_ONLY | BraceletMaxPts_Tao |
| `手镯最高点数_魔法_值` | "" |  | value | LABEL_ONLY | BraceletMaxPts_Mgc |
| `手镯点数几率_准确_值` | "5" | Y | value | LABEL_ONLY | BraceletPtsChance_Acc |
| `手镯点数几率_攻击_值` | "5" | Y | value | LABEL_ONLY | BraceletPtsChance_Atk |
| `手镯点数几率_攻速_值` | "5" | Y | value | LABEL_ONLY | BraceletPtsChance_Spd |
| `手镯点数几率_道术_值` | "5" | Y | value | LABEL_ONLY | BraceletPtsChance_Tao |
| `手镯点数几率_魔法_值` | "5" | Y | value | LABEL_ONLY | BraceletPtsChance_Mgc |
| `武器属性几率_准确_值` | "10" | Y | value | LABEL_ONLY | WeaponAttrChance_Acc |
| `武器属性几率_攻击_值` | "10" | Y | value | LABEL_ONLY | WeaponAttrChance_Atk |
| `武器属性几率_攻速_值` | "10" | Y | value | LABEL_ONLY | WeaponAttrChance_Spd |
| `武器属性几率_道术_值` | "10" | Y | value | LABEL_ONLY | WeaponAttrChance_Tao |
| `武器属性几率_魔法_值` | "10" | Y | value | LABEL_ONLY | WeaponAttrChance_Mgc |
| `武器最随机性_极品_值` | "80" | Y | value | LABEL_ONLY | WeaponRandExtreme |
| `武器最高点数_准确_值` | "7" | Y | value | LABEL_ONLY | WeaponMaxPts_Acc |
| `武器最高点数_攻击_值` | "" |  | value | LABEL_ONLY | WeaponMaxPts_Atk |
| `武器最高点数_攻速_值` | "7" | Y | value | LABEL_ONLY | WeaponMaxPts_Spd |
| `武器最高点数_道术_值` | "7" | Y | value | LABEL_ONLY | WeaponMaxPts_Tao |
| `武器最高点数_魔法_值` | "7" | Y | value | LABEL_ONLY | WeaponMaxPts_Mgc |
| `武器点数几率_准确_值` | "5" | Y | value | LABEL_ONLY | WeaponPtsChance_Acc |
| `武器点数几率_攻击_值` | "5" | Y | value | LABEL_ONLY | WeaponPtsChance_Atk |
| `武器点数几率_攻速_值` | "5" | Y | value | LABEL_ONLY | WeaponPtsChance_Spd |
| `武器点数几率_道术_值` | "5" | Y | value | LABEL_ONLY | WeaponPtsChance_Tao |
| `武器点数几率_魔法_值` | "5" | Y | value | LABEL_ONLY | WeaponPtsChance_Mgc |
| `衣服属性几率_准确_值` | "10" | Y | value | LABEL_ONLY | ArmorAttrChance_Acc |
| `衣服属性几率_攻击_值` | "10" | Y | value | LABEL_ONLY | ArmorAttrChance_Atk |
| `衣服属性几率_攻速_值` | "10" | Y | value | LABEL_ONLY | ArmorAttrChance_Spd |
| `衣服属性几率_道术_值` | "10" | Y | value | LABEL_ONLY | ArmorAttrChance_Tao |
| `衣服属性几率_魔法_值` | "10" | Y | value | LABEL_ONLY | ArmorAttrChance_Mgc |
| `衣服最随机性_极品_值` | "80" | Y | value | LABEL_ONLY | ArmorRandExtreme |
| `衣服最高点数_准确_值` | "10" | Y | value | LABEL_ONLY | ArmorMaxPts_Acc |
| `衣服最高点数_攻击_值` | "10" | Y | value | LABEL_ONLY | ArmorMaxPts_Atk |
| `衣服最高点数_攻速_值` | "10" | Y | value | LABEL_ONLY | ArmorMaxPts_Spd |
| `衣服最高点数_道术_值` | "10" | Y | value | LABEL_ONLY | ArmorMaxPts_Tao |
| `衣服最高点数_魔法_值` | "10" | Y | value | LABEL_ONLY | ArmorMaxPts_Mgc |
| `衣服点数几率_准确_值` | "5" | Y | value | LABEL_ONLY | ArmorPtsChance_Acc |
| `衣服点数几率_攻击_值` | "5" | Y | value | LABEL_ONLY | ArmorPtsChance_Atk |
| `衣服点数几率_攻速_值` | "5" | Y | value | LABEL_ONLY | ArmorPtsChance_Spd |
| `衣服点数几率_道术_值` | "5" | Y | value | LABEL_ONLY | ArmorPtsChance_Tao |
| `衣服点数几率_魔法_值` | "5" | Y | value | LABEL_ONLY | ArmorPtsChance_Mgc |
| `随机极品` | 1 | Y | toggle | LABEL_ONLY | IsRandomExtreme |
| `项链属性几率_准确_值` | "10" | Y | value | LABEL_ONLY | NecklaceAttrChance_Acc |
| `项链属性几率_攻击_值` | "10" | Y | value | LABEL_ONLY | NecklaceAttrChance_Atk |
| `项链属性几率_攻速_值` | "10" | Y | value | LABEL_ONLY | NecklaceAttrChance_Spd |
| `项链属性几率_道术_值` | "10" | Y | value | LABEL_ONLY | NecklaceAttrChance_Tao |
| `项链属性几率_魔法_值` | "10" | Y | value | LABEL_ONLY | NecklaceAttrChance_Mgc |
| `项链最随机性_极品_值` | "80" | Y | value | LABEL_ONLY | NecklaceRandExtreme |
| `项链最高点数_准确_值` | "10" | Y | value | LABEL_ONLY | NecklaceMaxPts_Acc |
| `项链最高点数_攻击_值` | "3" | Y | value | LABEL_ONLY | NecklaceMaxPts_Atk |
| `项链最高点数_攻速_值` | "10" | Y | value | LABEL_ONLY | NecklaceMaxPts_Spd |
| `项链最高点数_道术_值` | "10" | Y | value | LABEL_ONLY | NecklaceMaxPts_Tao |
| `项链最高点数_魔法_值` | "2" | Y | value | LABEL_ONLY | NecklaceMaxPts_Mgc |
| `项链点数几率_准确_值` | "5" | Y | value | LABEL_ONLY | NecklacePtsChance_Acc |
| `项链点数几率_攻击_值` | "10" | Y | value | LABEL_ONLY | NecklacePtsChance_Atk |
| `项链点数几率_攻速_值` | "5" | Y | value | LABEL_ONLY | NecklacePtsChance_Spd |
| `项链点数几率_道术_值` | "5" | Y | value | LABEL_ONLY | NecklacePtsChance_Tao |
| `项链点数几率_魔法_值` | "10" | Y | value | LABEL_ONLY | NecklacePtsChance_Mgc |

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
| `全屏拾取` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `刀刀切割` | 0 |  | toggle | IMPLEMENTED | GameSvr/Players/TPlayObject.NativeSocialSlots.cs; GameSvr/Plugins/YanshenCommands.cs; GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `千分比免伤` | 0 |  | toggle | LABEL_ONLY | IsDmgReduction |
| `复活戒指改cd` | 0 |  | toggle | LABEL_ONLY | IsReviveCD; IsReviveCDEnabled |
| `复活戒指概率` | 0 |  | toggle | LABEL_ONLY | IsReviveChance; IsReviveChanceEnabled |
| `复活触发脚本` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `捡物触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `攻击反伤` | 0 |  | toggle | LABEL_ONLY | IsReflectEnabled |
| `攻击触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `新倍攻和暴击` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `新穿戴触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `永久属性` | 0 |  | toggle | LABEL_ONLY | IsPermAttr |
| `永久攻速` | 0 |  | toggle | LABEL_ONLY | IsPermSpeed |
| `特殊宝宝` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `特殊属性` | 0 |  | toggle | LABEL_ONLY | IsPetSpecial |
| `禁止装备自动绑定` | 0 |  | toggle | LABEL_ONLY | IsBindDisabled |
| `移动速度` | 0 |  | toggle | LABEL_ONLY | IsMoveSpeed |
| `英雄倍攻和暴击` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `英雄攻速移速` | 0 |  | toggle | LABEL_ONLY | IsHeroSpeed; IsHeroSpeedEnabled |
| `英雄施法速度` | 0 |  | toggle | LABEL_ONLY | IsHeroCastSpeed |
| `英雄穿戴触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `被击杀触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `装备来源` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `读取英雄装备` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `魔法攻击触发` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenTriggerDispatch.cs |
| `麻痹概率` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |

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
| `激光命中概率` | 0 |  | toggle | LABEL_ONLY | IsLaserHitRate |
| `激光电影可换主属性` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
| `激光范围及系数` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenSkillPatches.cs |
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
| `全屏吸怪` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
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
| `英雄读取极品` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
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
| `super攻击触发` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `伤害触发脚本_plus` | 0 |  | toggle | LABEL_ONLY | IsDmgScriptPlus |
| `全局循环函数` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `千分比经验倍数` | 0 |  | toggle | LABEL_ONLY | IsExpMultiplier |
| `循环时间_值` | 2000 | Y | value | LABEL_ONLY | IsLoopTimeVal; IsLoopTimeValEnabled |
| `怪物爆率A_值` | 0 |  | value | LABEL_ONLY | IsMonsterDropA |
| `怪物爆率B_值` | 0 |  | value | LABEL_ONLY | IsMonsterDropB |
| `怪物爆率K_值` | 0 |  | value | LABEL_ONLY | IsMonsterDropK |
| `攻击吸血` | 0 |  | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs; GameSvr/Services/NativeType2StdItemSnapshotState.cs |
| `新怪物爆率` | 0 |  | toggle | LABEL_ONLY | IsNewMonsterDrop; IsNewMonsterDropEnabled |
| `毫秒级cd记录` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `火墙不吸血` | 0 |  | toggle | LABEL_ONLY | IsFireWallNoVamp |
| `眼神特殊函数` | 1 | Y | toggle | IMPLEMENTED | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs; SystemModule/Grobal2.cs |
| `自定义伤害_plus` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
| `英雄物理攻击触发` | 0 |  | toggle | LABEL_ONLY | IsHeroPhysTrigger; IsHeroPhysTriggerEnabled |
| `英雄野蛮` | 0 |  | toggle | LABEL_ONLY | IsHeroBarbarian; IsHeroBarbarianEnabled |
| `英雄魔法攻击触发` | 0 |  | toggle | LABEL_ONLY | IsHeroMagicTrigger; IsHeroMagicTriggerEnabled |
| `道士合击系数` | 0 |  | toggle | LABEL_ONLY | IsTaoComboFactor; IsTaoComboFactorEnabled |
| `道士合击系数_数值1` | "" |  | value | LABEL_ONLY | TaoComboV1 |
| `道士合击系数_数值2` | "" |  | value | LABEL_ONLY | TaoComboV2 |
| `道士合击系数_数值3` | "" |  | value | LABEL_ONLY | TaoComboV3 |
| `道士合击系数_数值4` | "" |  | value | LABEL_ONLY | TaoComboV4 |
| `道士合击系数_数值5` | "" |  | value | LABEL_ONLY | TaoComboV5 |
| `高级回收` | 1 | Y | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
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
| `大背包` | 0 |  | toggle | SCRIPT_ONLY | GameSvr/Plugins/YanshenCommands.cs; GameSvr/ScriptSystem/PasEngine/PasApiBridge.Yanshen.cs |
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

- `怪物伤害触发技能特效` @ `0x102bcfdc` — GameSvr/Plugins/YanshenApi.cs:1502, GameSvr/Plugins/YanshenApi.cs:1531
- `指定英雄放技能` @ `0x102b9048` — GameSvr/Plugins/YanshenApi.cs:1569
- `火墙修改` @ `0x102bd07c` — GameSvr/Plugins/YanshenApi.cs:3140

## INVENTED（生产 config 和转储字符串里都没有）

- `道士合击系数_数值` — GameSvr/Plugins/YanshenFixedReplicaPanels.cs:484
