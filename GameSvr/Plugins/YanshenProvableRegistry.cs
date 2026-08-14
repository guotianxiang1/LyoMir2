using GameSvr;

namespace GameSvr.Plugins
{
    /// <summary>
    /// gap/ys-provable sweep: close remaining LABEL_ONLY keys with byte evidence.
    /// EQUIVALENT_BY_ABSENCE / PLUGIN_SIDE_ONLY = PatchToggleOn only (1:1, no engine fiction).
    /// BLOCKED methods are registry anchors only — tramp1/tramp2/long-payload/Themida.
    /// </summary>
    internal static class YanshenProvableRegistry
    {
        static bool On(string key)
        {
            var pm = M2Share.PluginManager;
            return pm != null && new YanshenApi(null, null, pm).PatchToggleOn(key);
        }

        // --- EQUIVALENT_BY_ABSENCE (53): zero host/plugin fourth-path consumer ---
        /// <summary>专职变性: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_专职变性() => On("专职变性");
        /// <summary>临时大背包: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_临时大背包() => On("临时大背包");
        /// <summary>主号全局法速: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_主号全局法速() => On("主号全局法速");
        /// <summary>主号施法速度: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_主号施法速度() => On("主号施法速度");
        /// <summary>修复刺杀位麻痹: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_修复刺杀位麻痹() => On("修复刺杀位麻痹");
        /// <summary>全服击杀提示: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_全服击杀提示() => On("全服击杀提示");
        /// <summary>冰咆哮固定增伤: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_冰咆哮固定增伤() => On("冰咆哮固定增伤");
        /// <summary>切换暴击报文: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_切换暴击报文() => On("切换暴击报文");
        /// <summary>千分比免伤: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_千分比免伤() => On("千分比免伤");
        /// <summary>双毒时间_最低: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_双毒时间_最低() => On("双毒时间_最低");
        /// <summary>嗜血术范围: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_嗜血术范围() => On("嗜血术范围");
        /// <summary>多元伤害: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_多元伤害() => On("多元伤害");
        /// <summary>宝宝自动叛变: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_宝宝自动叛变() => On("宝宝自动叛变");
        /// <summary>怪物爆率A_值: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_怪物爆率A_值() => On("怪物爆率A_值");
        /// <summary>怪物爆率B_值: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_怪物爆率B_值() => On("怪物爆率B_值");
        /// <summary>怪物爆率K_值: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_怪物爆率K_值() => On("怪物爆率K_值");
        /// <summary>战队职业限制: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_战队职业限制() => On("战队职业限制");
        /// <summary>技能等级突破: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_技能等级突破() => On("技能等级突破");
        /// <summary>技能等级突破_最大值: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_技能等级突破_最大值() => On("技能等级突破_最大值");
        /// <summary>技能触发脚本: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_技能触发脚本() => On("技能触发脚本");
        /// <summary>新呼唤宝宝: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_新呼唤宝宝() => On("新呼唤宝宝");
        /// <summary>新怪物爆率: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_新怪物爆率() => On("新怪物爆率");
        /// <summary>火墙固定增伤: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_火墙固定增伤() => On("火墙固定增伤");
        /// <summary>火符固定增伤: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_火符固定增伤() => On("火符固定增伤");
        /// <summary>烈火固定增伤: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_烈火固定增伤() => On("烈火固定增伤");
        /// <summary>盘古击杀触发: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_盘古击杀触发() => On("盘古击杀触发");
        /// <summary>盘古物理攻击触发: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_盘古物理攻击触发() => On("盘古物理攻击触发");
        /// <summary>盘古给与封号: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_盘古给与封号() => On("盘古给与封号");
        /// <summary>神兽_序号: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_神兽_序号() => On("神兽_序号");
        /// <summary>神兽_数量: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_神兽_数量() => On("神兽_数量");
        /// <summary>穿戴触发_plus: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_穿戴触发_plus() => On("穿戴触发_plus");
        /// <summary>红毒_A: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_红毒_A() => On("红毒_A");
        /// <summary>红毒_B: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_红毒_B() => On("红毒_B");
        /// <summary>绿毒_A: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_绿毒_A() => On("绿毒_A");
        /// <summary>绿毒_B: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_绿毒_B() => On("绿毒_B");
        /// <summary>绿毒_最低: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_绿毒_最低() => On("绿毒_最低");
        /// <summary>群毒: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_群毒() => On("群毒");
        /// <summary>群毒值: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_群毒值() => On("群毒值");
        /// <summary>英雄自动开盾: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_英雄自动开盾() => On("英雄自动开盾");
        /// <summary>装备多职业: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_装备多职业() => On("装备多职业");
        /// <summary>装备转生穿戴判定a: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_装备转生穿戴判定a() => On("装备转生穿戴判定a");
        /// <summary>角色多阵营: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_角色多阵营() => On("角色多阵营");
        /// <summary>诱惑之光触发脚本a: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_诱惑之光触发脚本a() => On("诱惑之光触发脚本a");
        /// <summary>道士合击系数: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_道士合击系数() => On("道士合击系数");
        /// <summary>道士合击系数_数值1: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_道士合击系数_数值1() => On("道士合击系数_数值1");
        /// <summary>道士合击系数_数值2: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_道士合击系数_数值2() => On("道士合击系数_数值2");
        /// <summary>道士合击系数_数值3: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_道士合击系数_数值3() => On("道士合击系数_数值3");
        /// <summary>道士合击系数_数值4: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_道士合击系数_数值4() => On("道士合击系数_数值4");
        /// <summary>道士合击系数_数值5: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_道士合击系数_数值5() => On("道士合击系数_数值5");
        /// <summary>雷电术自定义伤害: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_雷电术自定义伤害() => On("雷电术自定义伤害");
        /// <summary>雷电术自定义伤害_系数A: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_雷电术自定义伤害_系数A() => On("雷电术自定义伤害_系数A");
        /// <summary>雷电术自定义伤害_系数B: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_雷电术自定义伤害_系数B() => On("雷电术自定义伤害_系数B");
        /// <summary>魔法盾修正: native 45MB mirror zero consumer.</summary>
        internal static bool Equiv_魔法盾修正() => On("魔法盾修正");

        // --- PLUGIN_SIDE_ONLY (26): plugin .text consumer, no M2Server patch ---
        /// <summary>主号分身术a: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_主号分身术a() => On("主号分身术a");
        /// <summary>主号高级暴击: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_主号高级暴击() => On("主号高级暴击");
        /// <summary>伤害触发脚本_plus: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_伤害触发脚本_plus() => On("伤害触发脚本_plus");
        /// <summary>千分比经验倍数: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_千分比经验倍数() => On("千分比经验倍数");
        /// <summary>宝宝叛变属性a: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_宝宝叛变属性a() => On("宝宝叛变属性a");
        /// <summary>宠物吸血a: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_宠物吸血a() => On("宠物吸血a");
        /// <summary>投保报文: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_投保报文() => On("投保报文");
        /// <summary>护身触发报文a: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_护身触发报文a() => On("护身触发报文a");
        /// <summary>护身触发概率a: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_护身触发概率a() => On("护身触发概率a");
        /// <summary>星耀专属切割a: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_星耀专属切割a() => On("星耀专属切割a");
        /// <summary>星耀倍功与暴击a: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_星耀倍功与暴击a() => On("星耀倍功与暴击a");
        /// <summary>星耀攻击反伤a: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_星耀攻击反伤a() => On("星耀攻击反伤a");
        /// <summary>格位刺杀免伤a: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_格位刺杀免伤a() => On("格位刺杀免伤a");
        /// <summary>概率格挡a: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_概率格挡a() => On("概率格挡a");
        /// <summary>盘古杀死宝宝: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_盘古杀死宝宝() => On("盘古杀死宝宝");
        /// <summary>自定义召唤怪物a: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_自定义召唤怪物a() => On("自定义召唤怪物a");
        /// <summary>英雄修装备a: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_英雄修装备a() => On("英雄修装备a");
        /// <summary>英雄千分比免伤: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_英雄千分比免伤() => On("英雄千分比免伤");
        /// <summary>英雄物理攻击触发: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_英雄物理攻击触发() => On("英雄物理攻击触发");
        /// <summary>英雄野蛮: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_英雄野蛮() => On("英雄野蛮");
        /// <summary>英雄魔法攻击触发: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_英雄魔法攻击触发() => On("英雄魔法攻击触发");
        /// <summary>装备投保: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_装备投保() => On("装备投保");
        /// <summary>高级物理攻击触发: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_高级物理攻击触发() => On("高级物理攻击触发");
        /// <summary>高级英雄倍功暴击: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_高级英雄倍功暴击() => On("高级英雄倍功暴击");
        /// <summary>高级魔法攻击触发: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_高级魔法攻击触发() => On("高级魔法攻击触发");
        /// <summary>麻痹中不被麻痹a: PLUGIN_SIDE_ONLY.</summary>
        internal static bool Plugin_麻痹中不被麻痹a() => On("麻痹中不被麻痹a");

    }
}
