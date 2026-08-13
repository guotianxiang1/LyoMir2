using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using SystemModule;
using GameSvr;

namespace GameSvr.Plugins
{
    public sealed class YanshenApiUnavailableException : Exception
    {
        public string FunctionName { get; }
        public string FeatureName { get; }
        public string FailureReason { get; }

        public YanshenApiUnavailableException(string functionName, string featureName, string failureReason)
            : base($"Yanshen API '{functionName}' is unavailable: {failureReason}.")
        {
            FunctionName = functionName;
            FeatureName = featureName;
            FailureReason = failureReason;
        }
    }

    /// <summary>
    /// Yanshen API — 一比一复刻眼神插件全部 Pascal 封装函数。
    /// 对应 AllFuc.pas + common.pas + NpcFuc.pas 的所有公开函数。
    ///
    /// 架构: Pascal 脚本 → !!!! 隧道 → YanshenCommandEngine → YanshenApi (本类)
    ///        Pascal 脚本 → PasInterpreter 内置函数 → YanshenApi (直接, 更快)
    /// </summary>
    public class YanshenApi
    {
        [ThreadStatic] private static string _strictDirectCallName;
        [ThreadStatic] private static PluginInfo _initializingPlugin;
        [ThreadStatic] private static int _initializationDepth;
        private static readonly ConcurrentDictionary<string, int> _volatileInts = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _volatileStrings = new(StringComparer.OrdinalIgnoreCase);

        private readonly TPlayObject _player;
        private readonly NormNpc _npc;
        private readonly PluginManager _pluginManager;

        public YanshenApi(TPlayObject player, NormNpc npc, PluginManager pm = null)
        {
            _player = player; _npc = npc; _pluginManager = pm;
        }

        public static IDisposable BeginStrictDirectCall(string functionName)
        {
            var previous = _strictDirectCallName;
            _strictDirectCallName = functionName;
            return new StrictDirectCallScope(previous);
        }

        public static void EnsureDirectCallReady(PluginManager manager, string functionName)
        {
            var plugin = manager?.GetPlugin("YanshenCompat");
            if (plugin?.State != PluginState.Running)
                throw new YanshenApiUnavailableException(functionName, null, "插件未运行");
            if (!plugin.IsInitialized && !IsInitializing(plugin))
                throw new YanshenApiUnavailableException(functionName, null,
                    "未初始化（必须先执行 initys）");
        }

        internal static void EnterInitialization(PluginInfo plugin)
        {
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));
            if (_initializationDepth == 0)
                _initializingPlugin = plugin;
            else if (!ReferenceEquals(_initializingPlugin, plugin))
                throw new InvalidOperationException("A different Yanshen plugin is already initializing on this thread.");
            _initializationDepth++;
        }

        internal static void ExitInitialization(PluginInfo plugin)
        {
            if (_initializationDepth <= 0 || !ReferenceEquals(_initializingPlugin, plugin))
                return;
            _initializationDepth--;
            if (_initializationDepth == 0) _initializingPlugin = null;
        }

        private static bool IsInitializing(PluginInfo plugin) =>
            _initializationDepth > 0 && ReferenceEquals(_initializingPlugin, plugin);

        private sealed class StrictDirectCallScope : IDisposable
        {
            private readonly string _previous;

            public StrictDirectCallScope(string previous) => _previous = previous;

            public void Dispose() => _strictDirectCallName = _previous;
        }

        // ═══════════════════════════════════════════════════════════════
        // 完整 379 键映射: 226 toggles + 153 params from config.json
        // ═══════════════════════════════════════════════════════════════
        static readonly Dictionary<string, string> _keyMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // ── 技能 toggles ──
            ["skills.stabSword"] = "刺杀剑术",        ["skills.halfMoon"] = "半月弯刀",
            ["skills.fireSword"] = "烈火剑法",         ["skills.oneSword"] = "基本剑术",
            ["skills.thrusting"] = "攻杀剑术",         ["skills.sunSword"] = "逐日剑法",
            ["skills.zhenQi"] = "无极真气",            ["skills.bloodSuck"] = "嗜血术倍数",
            ["skills.bloodRange"] = "嗜血术范围",       ["skills.summonShenShou"] = "召唤神兽",
            ["skills.summonKuLou"] = "召唤骷髅",        ["skills.modifyShenShou"] = "修改召唤神兽",
            ["skills.shenShouCount"] = "神兽_数量",     ["skills.kuLouCount"] = "召唤骷髅_数量",
            ["skills.shenShouIdx"] = "神兽_序号",
            // ── 法师技能 toggles ──
            ["skills.fireBallSwitch"] = "火球主属性切换",   ["skills.fireBallRange"] = "火球自定义范围",
            ["skills.fireRainSwitch"] = "火雨主属切换",     ["skills.fireWallDuration"] = "火墙_时间",
            ["skills.fireWallNoVamp"] = "火墙不吸血",       ["skills.fireWallCutting"] = "火墙切割",
            ["skills.fireWallFixDmg"] = "火墙固定增伤",     ["skills.fireWallTimeLimit"] = "火墙设置时间上限",
            ["skills.hellLightSwitch"] = "地狱雷光可换主属性", ["skills.hellLightFactor"] = "地狱雷光系数",
            ["skills.hellLightRange"] = "地狱雷光范围",     ["skills.blastFlameSwitch"] = "爆裂火焰可换主属性",
            ["skills.blastFlameRange"] = "爆裂火焰范围及系数", ["skills.iceStormSwitch"] = "冰咆哮主属性切换",
            ["skills.iceStormCutting"] = "冰咆哮切割",      ["skills.iceStormFixDmg"] = "冰咆哮固定增伤",
            ["skills.iceStormRange"] = "冰咆哮范围",        ["skills.laserSwitch"] = "激光电影可换主属性",
            ["skills.laserHitRate"] = "激光命中概率",       ["skills.laserRange"] = "激光范围及系数",
            ["skills.lightningSwitch"] = "雷电主属性切换",  ["skills.lightningPoison"] = "雷电带毒",
            ["skills.lightningCutting"] = "雷电术切割",     ["skills.lightningCustom"] = "雷电术自定义伤害",
            ["skills.lightningCustomA"] = "雷电术自定义伤害_系数A", ["skills.lightningCustomB"] = "雷电术自定义伤害_系数B",
            ["skills.lightningRange"] = "雷电自定义范围",
            ["skills.fireCutting"] = "烈火切割",            ["skills.fireFixDmg"] = "烈火固定增伤",
            ["skills.amuletCutting"] = "火符切割",          ["skills.amuletFixDmg"] = "火符固定增伤",
            ["skills.halfMoonPoison"] = "半月带毒",         ["skills.weaponGreenPoison"] = "武器绿毒",
            ["skills.physicalPoison"] = "物功带毒",         ["skills.mageGroupPoison"] = "法师群毒",
            ["skills.groupPoison"] = "群毒",                ["skills.groupPoisonVal"] = "群毒值",
            ["skills.zhaoZeFix"] = "噬魂沼泽绿毒修复",
            // ── 基础伤害 toggles ──
            ["damage.cuttingEnabled"] = "刀刀切割",         ["damage.reflectEnabled"] = "攻击反伤",
            ["damage.lifeSteal"] = "攻击吸血",             ["damage.equipSteal"] = "装备吸血",
            ["damage.poisonEnabled"] = "施毒术",           ["damage.paralysisEnabled"] = "麻痹概率",
            ["damage.paralysisImmune"] = "麻痹中不被麻痹a", ["damage.breakRevival"] = "破复活",
            ["damage.antiPoison"] = "免毒符",              ["damage.multiDmg"] = "多元伤害",
            ["damage.dmgReduction"] = "千分比免伤",        ["damage.expMultiplier"] = "千分比经验倍数",
            ["damage.luckBlock"] = "格位刺杀免伤a",        ["damage.probBlock"] = "概率格挡a",
            ["damage.fixStabParalysis"] = "修复刺杀位麻痹", ["damage.fixDefense"] = "修复卡防御",
            ["damage.zeroDefSplit"] = "防0拆分",           ["damage.magicShieldFix"] = "魔法盾修正",
            ["damage.holyShieldMsg"] = "护身触发报文a",    ["damage.holyShieldChance"] = "护身触发概率a",
            ["damage.poisonTimeLimit"] = "中毒时间上限",    ["damage.poisonBleed"] = "中毒飘血",
            ["damage.dualPoisonMin"] = "双毒时间_最低",    ["damage.redPoisonA"] = "红毒_A",
            ["damage.redPoisonB"] = "红毒_B",              ["damage.greenPoisonA"] = "绿毒_A",
            ["damage.greenPoisonB"] = "绿毒_B",            ["damage.greenPoisonMin"] = "绿毒_最低",
            // ── 倍攻/暴击 toggles ──
            ["power.newMultCrit"] = "新倍攻和暴击",         ["power.permAttr"] = "永久属性",
            ["power.permSpeed"] = "永久攻速",              ["power.moveSpeed"] = "移动速度",
            ["power.passThrough"] = "穿人穿怪",            ["power.switchCritMsg"] = "切换暴击报文",
            // ── 星耀 toggles ──
            ["xingyao.cutting"] = "星耀专属切割a",         ["xingyao.powerCrit"] = "星耀倍功与暴击a",
            ["xingyao.reflect"] = "星耀攻击反伤a",
            // ── 盘古 toggles ──
            ["panggu.iceStormRange"] = "盘古冰咆哮的范围",  ["panggu.hellLightRange"] = "盘古地狱雷光范围",
            ["panggu.fireRainRange"] = "盘古流星火雨范围",   ["panggu.blastFlameRange"] = "盘古爆裂火焰范围",
            ["panggu.killTrigger"] = "盘古击杀触发",         ["panggu.physTrigger"] = "盘古物理攻击触发",
            ["panggu.magicTrigger"] = "盘古魔法攻击触发",     ["panggu.wearTrigger"] = "盘古穿戴触发",
            ["panggu.killPet"] = "盘古杀死宝宝",             ["panggu.giveTitle"] = "盘古给与封号",
            ["panggu.advancedAttr"] = "盘古高级属性",
            // ── 召唤 toggles ──
            ["summon.newCallPet"] = "新呼唤宝宝",           ["summon.customCall"] = "自定义召唤怪物a",
            ["summon.petEnabled"] = "特殊宝宝",             ["summon.petSpecial"] = "特殊属性",
            ["summon.petVampire"] = "宠物吸血a",            ["summon.petNoRest"] = "禁止宝宝休息",
            ["summon.petRebelAttr"] = "宝宝叛变属性a",      ["summon.petAutoRebel"] = "宝宝自动叛变",
            ["summon.petDieOffline"] = "下线宝宝死亡",
            // ── 英雄 toggles ──
            ["hero.autoShield"] = "英雄自动开盾",           ["hero.powerCrit"] = "英雄倍攻和暴击",
            ["hero.advancedPowerCrit"] = "高级英雄倍功暴击",  ["hero.barbarian"] = "英雄野蛮",
            ["hero.speed"] = "英雄攻速移速",               ["hero.castSpeed"] = "英雄施法速度",
            ["hero.castSkill"] = "指定英雄放技能",
            ["hero.dmgReduction"] = "英雄千分比免伤",       ["hero.readExtreme"] = "英雄读取极品",
            ["hero.repairEquip"] = "英雄修装备a",           ["hero.readEquip"] = "读取英雄装备",
            ["hero.physTrigger"] = "英雄物理攻击触发",       ["hero.magicTrigger"] = "英雄魔法攻击触发",
            ["hero.wearTrigger"] = "英雄穿戴触发",
            // ── 触发系统 toggles ──
            ["trigger.attack"] = "攻击触发",              ["trigger.magicAttack"] = "魔法攻击触发",
            ["trigger.superAttack"] = "super攻击触发",     ["trigger.advancedPhys"] = "高级物理攻击触发",
            ["trigger.advancedMagic"] = "高级魔法攻击触发", ["trigger.beKilled"] = "被击杀触发",
            ["trigger.death"] = "死亡触发",               ["trigger.reviveScript"] = "复活触发脚本",
            ["trigger.petKill"] = "BB杀怪触发",            ["trigger.petDeath"] = "BB死亡触发",
            ["trigger.skill"] = "技能触发脚本",             ["trigger.dmgScript"] = "伤害触发脚本_plus",
            ["trigger.pickup"] = "捡物触发",               ["trigger.mine"] = "挖矿触发",
            ["trigger.login"] = "上线触发",                ["trigger.wear"] = "新穿戴触发",
            ["trigger.wearPlus"] = "穿戴触发_plus",        ["trigger.mindReveal"] = "心灵启示触发",
            ["trigger.returnBtn"] = "回城按钮触发",         ["trigger.lure"] = "诱惑之光触发脚本a",
            ["trigger.shenShou"] = "召唤神兽触发",          ["trigger.kuLou"] = "召唤骷髅触发",
            // ── 物品/装备 toggles ──
            ["item.autoPickup"] = "全屏拾取",             ["item.autoRecycle"] = "高级回收",
            ["item.bindDisabled"] = "禁止装备自动绑定",    ["item.autoBindOff"] = "屏蔽自动绑定",
            ["item.elements"] = "自定义元素",              ["item.source"] = "装备来源",
            ["item.insurance"] = "装备投保",              ["item.insuranceMsg"] = "投保报文",
            ["item.multiJob"] = "装备多职业",             ["item.rebirthWear"] = "装备转生穿戴判定a",
            ["item.boostDropRate"] = "装备提升人物爆率",    ["item.bigBag"] = "大背包",
            ["item.tempBag"] = "临时大背包",               ["item.portableStorage"] = "随身仓库",
            ["item.randomExtreme"] = "随机极品",           ["item.giveExtreme"] = "give极品",
            ["item.maxEquipCount"] = "最大装备数量",
            // ── 服务器系统 toggles ──
            ["sys.addLimLF"] = "AddLimLF函数修改",         ["sys.incActivePoint"] = "IncActivePoint函数修改",
            ["sys.serverSay"] = "ServerSay函数",           ["sys.noKillMapLv"] = "SetNoKillMapLv脚本触发",
            ["sys.setTitle"] = "设置玩家称号函数",          ["sys.yanshenSpecial"] = "眼神特殊函数",
            ["sys.cdMs"] = "毫秒级cd记录",                ["sys.loopTimer"] = "全局循环函数",
            ["sys.hideGoldLog"] = "屏蔽元宝数据库日志",    ["sys.hideGoldMsg"] = "屏蔽元宝增减信息",
            ["sys.hideAttrUp"] = "屏蔽属性提升提示",       ["sys.hideRank"] = "屏蔽排行榜",
            ["sys.blockSpam"] = "屏蔽发言频繁禁言功能",    ["sys.delSkillSilent"] = "删除技能不提示",
            ["sys.delHeroSkill"] = "删除英雄技能",         ["sys.upSkillSilent"] = "升级技能不提示",
            ["sys.banChatSilent"] = "禁止发言不提示",      ["sys.nameColor"] = "名字变色",
            ["sys.levelMute"] = "等级禁言",               ["sys.mailAntiSpam"] = "邮件防刷",
            ["sys.playerDropRate"] = "人物爆率调整",       ["sys.scriptDropRate"] = "脚本控制人物爆率",
            ["sys.scriptHair"] = "脚本控制头发外显",       ["sys.newMonsterDrop"] = "新怪物爆率",
            ["sys.getCastle"] = "获取沙城归属",            ["sys.guildShow"] = "行会显示",
            ["sys.multiFaction"] = "角色多阵营",           ["sys.siegeScript"] = "攻沙脚本控制",
            ["sys.siegeModify"] = "攻城修改",              ["sys.siegeDuration"] = "攻城时长_分钟",
            ["sys.siegeModMinute"] = "攻城修改_分钟",       ["sys.siegeModDay"] = "攻城修改_天数",
            ["sys.siegeModHour"] = "攻城修改_小时",        ["sys.killNotice"] = "全服击杀提示",
            ["sys.vacuum"] = "全屏吸怪",                  ["sys.kickPlayer"] = "踢玩家下线",
            ["sys.rename"] = "名字变色",                   ["sys.safeNoDrop"] = "安全区禁止丢物",
            ["sys.floorItemTimeout"] = "地面物品消失时间",  ["sys.brightSuit"] = "skills.halfMoonPoison",
            // ── 技能突破/等级 toggles ──
            ["skills.levelBreak"] = "技能等级突破",         ["skills.levelBreakMax"] = "技能等级突破_最大值",
            // ── 合击 toggles ──
            ["combo.warrior"] = "战士合击",               ["combo.wizTao"] = "法道合击",
            ["combo.taoFactor"] = "道士合击系数",
            // ── 摆摊/交易 toggles ──
            ["trade.stallPass"] = "摆摊穿人",             ["trade.closeStall"] = "关闭摆摊",
            ["trade.tuChengStall"] = "土城摆摊",          ["trade.limitStall"] = "限制摆摊",
            ["trade.mapStall"] = "指定地图编号摆摊",       ["trade.banTradeMap"] = "禁止交易地图",
            // ── 服务功能 toggles ──
            ["func.changeJob"] = "专职变性",               ["func.gamePartnerLimit"] = "战队职业限制",
            // ── 复活戒指 toggles ──
            ["ring.reviveCD"] = "复活戒指改cd",           ["ring.reviveChance"] = "复活戒指概率",
            ["ring.reviveReset"] = "复活戒指重设",         ["ring.reviveImmune"] = "复活戒指重设_无敌时间",
            ["ring.reviveResetTime"] = "复活戒指重设_重设时间",
            // Explicit Chinese key pass-through (used directly in config form)
            ["半月弯刀"] = "半月弯刀",     ["刺杀剑术"] = "刺杀剑术",
            ["烈火剑法"] = "烈火剑法",     ["攻杀剑术"] = "攻杀剑术",
            ["逐日剑法"] = "逐日剑法",     ["基本剑术"] = "基本剑术",
            ["无极真气"] = "无极真气",     ["野蛮麻痹"] = "野蛮麻痹",
            ["野蛮等级"] = "野蛮等级",     ["施毒术"] = "施毒术",
            ["刀刀切割"] = "刀刀切割",     ["攻击反伤"] = "攻击反伤",
            ["攻击吸血"] = "攻击吸血",     ["麻痹概率"] = "麻痹概率",
            ["全屏吸怪"] = "全屏吸怪",     ["全屏拾取"] = "全屏拾取",
            ["高级回收"] = "高级回收",     ["自定义元素"] = "自定义元素",
            ["特殊宝宝"] = "特殊宝宝",     ["特殊属性"] = "特殊属性",
            ["英雄自动开盾"] = "英雄自动开盾", ["行会显示"] = "行会显示",
            ["全局循环函数"] = "全局循环函数", ["毫秒级cd记录"] = "毫秒级cd记录",
            ["踢玩家下线"] = "踢玩家下线", ["名字变色"] = "名字变色",
            ["装备来源"] = "装备来源",     ["火墙_时间"] = "火墙_时间",
            ["自定义伤害"] = "自定义伤害", ["自定义伤害_plus"] = "自定义伤害_plus",
            ["怪物数量1_值"] = "怪物数量1_值", ["召唤骷髅"] = "召唤骷髅",
            ["召唤神兽"] = "召唤神兽",     ["大背包"] = "大背包",
            ["禁止装备自动绑定"] = "禁止装备自动绑定", ["屏蔽元宝数据库日志"] = "屏蔽元宝数据库日志",
            ["装备投保"] = "装备投保",     ["装备吸血"] = "装备吸血",
            ["人物爆率调整"] = "人物爆率调整", ["BB杀怪触发"] = "BB杀怪触发",
            ["在线改名"] = "名字变色",     ["英雄倍攻和暴击"] = "英雄倍攻和暴击",
            ["禁止宝宝休息"] = "禁止宝宝休息", ["全服击杀提示"] = "全服击杀提示",
            // Additional pass-through entries for newly-wired toggles
            ["英雄施法速度"] = "英雄施法速度", ["指定英雄放技能"] = "指定英雄放技能",
            ["眼神特殊函数"] = "眼神特殊函数",
            ["火墙设置时间上限"] = "火墙设置时间上限", ["屏蔽自动绑定"] = "屏蔽自动绑定",
            ["装备多职业"] = "装备多职业",     ["装备转生穿戴判定a"] = "装备转生穿戴判定a",
            ["装备提升人物爆率"] = "装备提升人物爆率", ["随机极品"] = "随机极品",
            ["随身仓库"] = "随身仓库",         ["临时大背包"] = "临时大背包",
            ["技能等级突破"] = "技能等级突破", ["技能等级突破_最大值"] = "技能等级突破_最大值",
            ["主号全局法速"] = "主号全局法速", ["主号施法速度"] = "主号施法速度",
            ["主号高级暴击"] = "主号高级暴击", ["主号分身术a"] = "主号分身术a",
            ["复活戒指改cd"] = "复活戒指改cd", ["复活戒指概率"] = "复活戒指概率",
            ["复活戒指重设"] = "复活戒指重设", ["复活戒指重设_无敌时间"] = "复活戒指重设_无敌时间",
            ["复活戒指重设_重设时间"] = "复活戒指重设_重设时间",
            ["安全区禁止丢物"] = "安全区禁止丢物", ["地面物品消失时间"] = "地面物品消失时间",
            ["限制摆摊"] = "限制摆摊",         ["摆摊穿人"] = "摆摊穿人",
            ["关闭摆摊"] = "关闭摆摊",         ["土城摆摊"] = "土城摆摊",
            ["指定地图编号摆摊"] = "指定地图编号摆摊", ["禁止交易地图"] = "禁止交易地图",
            ["专职变性"] = "专职变性",         ["战队职业限制"] = "战队职业限制",
            ["邮件防刷"] = "邮件防刷",         ["等级禁言"] = "等级禁言",
            ["删除技能不提示"] = "删除技能不提示", ["升级技能不提示"] = "升级技能不提示",
            ["禁止发言不提示"] = "禁止发言不提示", ["删除英雄技能"] = "删除英雄技能",
            ["投保报文"] = "投保报文",         ["give极品"] = "give极品",
        };

        // ── C-runtime number parsing, for the parameters the plugin feeds
        // straight into a memory patch. yanshen2.0.8 @0x10000000 reads the six
        // warrior-skill A/B strings out of its config struct and converts them
        // with the CRT, not with a locale-aware parser:
        //   0x1022DC49  atoi   (strtol base 10: 0x1022DC61 `push 0xA`)
        //   0x10234345  atof   (strtod: 0x1023434F `call 0x102342E1`)
        // So "3.5" becomes 3 on every A, and "1e3" becomes 1, not 1000.

        /// <summary>C `atoi`: optional sign, decimal digits, stop at the first non-digit.</summary>
        internal static int NativeAtoi(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int i = 0;
            while (i < text.Length && (text[i] == ' ' || (text[i] >= '\t' && text[i] <= '\r'))) i++;
            bool negative = false;
            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
            {
                negative = text[i] == '-';
                i++;
            }
            long value = 0;
            for (; i < text.Length && text[i] >= '0' && text[i] <= '9'; i++)
            {
                value = value * 10 + (text[i] - '0');
                if (value > 0x7FFFFFFFL) { value = negative ? 0x80000000L : 0x7FFFFFFFL; break; }
            }
            return negative ? unchecked((int)-value) : unchecked((int)value);
        }

        string RawParam(string chineseKey)
        {
            if (_pluginManager == null) return null;
            var val = _pluginManager.GetNativeConfigValue(chineseKey);
            if (val == null) return null;
            if (val is string s) return s;
            try
            {
                if (val is System.Text.Json.JsonElement je)
                {
                    return je.ValueKind == System.Text.Json.JsonValueKind.String
                        ? je.GetString()
                        : je.ToString();
                }
            }
            catch { }
            return val.ToString();
        }

        /// <summary>Read a parameter the way the plugin does: CRT atoi over the raw config string.</summary>
        int ParamAtoi(string chineseKey, int defaultValue)
        {
            var raw = RawParam(chineseKey);
            return raw == null ? defaultValue : NativeAtoi(raw);
        }

        /// <summary>
        /// Read a divisor the way the plugin does: CRT atof, then narrowed to
        /// float32 by the `fstp dword` that stages the patch word
        /// (0x100B406C for 刺杀, 0x100B42A3 for 半月).
        /// </summary>
        float ParamAtof32(string chineseKey, float defaultValue)
        {
            var raw = RawParam(chineseKey);
            if (raw == null) return defaultValue;
            return double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? (float)d : 0f;
        }

        /// <summary>Get a native config parameter value as float. Returns defaultValue if not found.</summary>
        double GetParam(string chineseKey, double defaultValue = 0)
        {
            if (_pluginManager == null) return defaultValue;
            var val = _pluginManager.GetNativeConfigValue(chineseKey);
            if (val == null) return defaultValue;
            try
            {
                if (val is int i) return i;
                if (val is long l) return l;
                if (val is double d) return d;
                if (val is string s && double.TryParse(s, out var r)) return r;
                if (val is System.Text.Json.JsonElement je)
                {
                    if (je.ValueKind == System.Text.Json.JsonValueKind.Number) return je.GetDouble();
                    if (je.ValueKind == System.Text.Json.JsonValueKind.String && double.TryParse(je.GetString(), out var r2)) return r2;
                }
            }
            catch { }
            return defaultValue;
        }

        /// <summary>Get a native config value as int. Returns defaultValue if not found.</summary>
        int GetParamInt(string chineseKey, int defaultValue = 0)
        {
            if (_pluginManager == null) return defaultValue;
            var val = _pluginManager.GetNativeConfigValue(chineseKey);
            if (val == null) return defaultValue;
            try
            {
                if (val is int i) return i;
                if (val is long l) return (int)l;
                if (val is double d) return (int)d;
                if (val is string s) return (int)(double.TryParse(s, out var r) ? r : 0);
                if (val is System.Text.Json.JsonElement je)
                {
                    if (je.ValueKind == System.Text.Json.JsonValueKind.Number) return je.GetInt32();
                    if (je.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var s2 = je.GetString(); return string.IsNullOrEmpty(s2) ? 0 : (int)(double.TryParse(s2, out var r2) ? r2 : 0);
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        /// <summary>
        /// Read one of the 96 随机极品 knobs. Every one of them exists only as an
        /// immediate the plugin writes into M2Server's code, so three situations
        /// all mean "leave the host alone": 随机极品 off (0x100BF6E3 `cmp
        /// [esi+0x4F0],0 / je` jumps to the restore arm), the key absent, and the
        /// key non-positive (0x100BFE2D `test eax,eax / jle` skips the store).
        /// The stock immediate is therefore the answer in all three, and lives
        /// here rather than at the call sites.
        /// </summary>
        int ExtremeParamInt(string chineseKey, int stockImmediate)
        {
            if (!IsRandomExtreme()) return stockImmediate;
            var value = GetParamInt(chineseKey, stockImmediate);
            return value > 0 ? value : stockImmediate;
        }

        /// <summary>
        /// Check if a feature is enabled by Chinese config key name (from config.json).
        /// Also supports dotted fallback keys via _keyMap for backward compatibility
        /// with external callers that still pass dotted keys.
        /// </summary>
        bool Enabled(string key)
        {
            var plugin = _pluginManager?.GetPlugin("YanshenCompat");
            if (plugin?.State != PluginState.Running)
                return RejectUnavailable(key, "插件未运行");
            if (!plugin.IsInitialized && !IsInitializing(plugin))
                return RejectUnavailable(key, "未初始化（必须先执行 initys）");

            // Resolve through _keyMap first (handles both dotted->Chinese and Chinese pass-through)
            var lookupKey = _keyMap.TryGetValue(key, out var mapped) ? mapped : key;
            var nativeVal = _pluginManager.GetNativeConfigValue(lookupKey);
            if (nativeVal != null)
            {
                return IsEnabledValue(nativeVal)
                    ? true
                    : RejectUnavailable(lookupKey, $"开关未开启（{lookupKey}）");
            }

            var settings = _pluginManager.GetPluginOwnedConfig("YanshenCompat");
            if (settings.TryGetValue(lookupKey, out var settingValue) ||
                (!lookupKey.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                 settings.TryGetValue(key, out settingValue)))
            {
                return IsEnabledValue(settingValue)
                    ? true
                    : RejectUnavailable(lookupKey, $"开关未开启（{lookupKey}）");
            }

            // 火墙修改 / 指定英雄放技能 / 怪物伤害触发技能特效 are not written to config.json;
            // native keeps them in MyJson/{skills,roles}/config.json as "<key>_是否勾选".
            var subsystemValue = _pluginManager.GetSubsystemToggleValue(lookupKey);
            if (subsystemValue != null)
            {
                return IsEnabledValue(subsystemValue)
                    ? true
                    : RejectUnavailable(lookupKey, $"开关未开启（{lookupKey}）");
            }

            return RejectUnavailable(lookupKey, $"开关键缺失（{lookupKey}）");
        }

        private bool RejectUnavailable(string featureName, string reason)
        {
            if (_strictDirectCallName != null)
                throw new YanshenApiUnavailableException(_strictDirectCallName, featureName, reason);
            return false;
        }

        private static bool IsEnabledValue(object value)
        {
            value = PluginManager.NormalizeConfigValue(value);
            return value switch
            {
                null => false,
                bool boolean => boolean,
                byte number => number != 0,
                sbyte number => number != 0,
                short number => number != 0,
                ushort number => number != 0,
                int number => number != 0,
                uint number => number != 0,
                long number => number != 0,
                ulong number => number != 0,
                float number => number != 0,
                double number => number != 0,
                decimal number => number != 0,
                string text when bool.TryParse(text, out var boolean) => boolean,
                string text when double.TryParse(text, out var number) => number != 0,
                string text => !string.IsNullOrWhiteSpace(text),
                _ => true,
            };
        }

        internal void EnsureFeatureEnabled(string featureName)
        {
            _ = Enabled(featureName);
        }

        private bool EnabledAll(params string[] featureNames)
        {
            foreach (var featureName in featureNames)
            {
                if (!Enabled(featureName)) return false;
            }
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        // 6.1 元素系统 (17元素) — 14 functions
        // ═══════════════════════════════════════════════════════════════

        /// <summary>设置身体部位装备元素值。id:1-5元素类型(6=投保), pis:部位0-15, val:值</summary>
        /// <returns>写入的值；任何一道门没过都返回 0（1005E519 把结果槽初始化成 0）。</returns>
        public int GivePis(int elementType, int bodyPos, int value)
        {
            if (!Enabled("自定义元素")) return 0;
            if (bodyPos < 0 || bodyPos >= _player.m_UseItems.Length) return 0;
            var item = _player.m_UseItems[bodyPos];
            if (item == null) return 0;
            // 负值在写入前就被拒掉，旧值保持不变：1005E8D5 85FF test edi,edi / 0F884601 js。
            if (value < 0) return 0;
            // 类型 <1 走 ys1 的 dword 槽，>17 夹到 17 而不是拒绝：
            // 1005E8DD 83FE01 cmp esi,1 / 0F8C2201 jl；1005E8E6 83FE11 cmp esi,0x11 /
            // 7E07 jle / BE11000000 mov esi,0x11；1005E8F2 83FE01 / 0F840D01 je。
            if (elementType < 1) elementType = 1;
            else if (elementType > 17) elementType = 17;
            // 2..17 是单字节槽，原生 1005E9FA 880C10 mov byte[eax+edx],cl 直接截断低位。
            SetElementValue(item, elementType, elementType == 1 ? value : (byte)value);
            return value;
        }

        /// <summary>获取身体部位装备元素值</summary>
        public int GetPis(int elementType, int bodyPos)
        {
            if (!Enabled("自定义元素")) return 0;
            if (bodyPos < 0 || bodyPos >= _player.m_UseItems.Length) return 0;
            var item = _player.m_UseItems[bodyPos];
            if (item == null) return 0;
            return GetElementValue(item, elementType);
        }

        /// <summary>给17元素物品 (新格式), ys1最大21亿, 其他255</summary>
        public void GiveNewItem(string itemName, int bindFlag, int[] ys)
        {
            if (!Enabled("自定义元素")) return;
            if (_npc == null || ys == null || ys.Length < 1) return;
            // Create item manually (custom element values, not via standard GiveItem)
            if (M2Share.UserEngine.GetStdItemIdx(itemName) <= 0) return;
            if (!_player.IsEnoughBag()) return;
            var userItem = new TUserItem();
            if (!M2Share.UserEngine.CopyToUserItemFromName(itemName, ref userItem))
            {
                userItem = null;
                return;
            }
            for (int i = 0; i < Math.Min(ys.Length, 17); i++)
                SetElementValue(userItem, i + 1, ys[i]);
            userItem.Bind = (byte)Math.Min(255, Math.Max(0, bindFlag));
            _player.m_ItemList.Add(userItem);
            _player.SendAddItem(userItem);
        }

        /// <summary>给17元素+6极品物品</summary>
        public void GiveItemYS_JP(string itemName, int bindFlag, int[] ys, int[] jp)
        {
            if (!Enabled("自定义元素")) return;
            var previousCount = _player.m_ItemList.Count;
            GiveNewItem(itemName, bindFlag, ys);
            if (jp == null || jp.Length < 1) return;
            var item = _player.m_ItemList.Count > previousCount ? _player.m_ItemList[_player.m_ItemList.Count - 1] : null;
            if (item == null) return;
            for (int i = 0; i < Math.Min(jp.Length, 6); i++)
                SetExtremeValue(item, i, jp[i]);
            _player.SendUpdateItem(item);
        }

        /// <summary>给带描述来源的物品 (pname/desc1/desc2 最长8汉字16英文)</summary>
        public void GiveItemWithDesc(string itemName, string pname, string desc1, string desc2, int bindFlag)
        {
            if (!Enabled("自定义元素")) return;
            if (_npc == null) return;
            // Clone item creation to set description metadata
            if (M2Share.UserEngine.GetStdItemIdx(itemName) <= 0) return;
            if (!_player.IsEnoughBag()) return;
            var userItem = new TUserItem();
            if (!M2Share.UserEngine.CopyToUserItemFromName(itemName, ref userItem))
            {
                userItem = null;
                return;
            }
            userItem.pname = pname ?? string.Empty;
            userItem.desc1 = desc1 ?? string.Empty;
            userItem.desc2 = desc2 ?? string.Empty;
            userItem.Bind = (byte)Math.Min(255, Math.Max(0, bindFlag));
            _player.m_ItemList.Add(userItem);
            _player.SendAddItem(userItem);
        }

        /// <summary>通过ys_WupinGetData返回的不透明数据还原物品。</summary>
        public void GiveDataItem(string itemName, string dataString)
        {
            if (!Enabled("自定义元素")) return;
            if (string.IsNullOrEmpty(itemName) || string.IsNullOrEmpty(dataString)) return;
            if (!TryDeserializeItemData(dataString, out var saved)) return;
            if (!_player.IsEnoughBag()) return;

            var created = new TUserItem();
            if (!M2Share.UserEngine.CopyToUserItemFromName(itemName, ref created)) return;

            var restored = new TUserItem(saved)
            {
                MakeIndex = created.MakeIndex,
                wIndex = created.wIndex
            };
            _player.m_ItemList.Add(restored);
            _player.SendAddItem(restored);
        }

        /// <summary>
        /// Ys_NpcGiveItemYs / 数字隧道 24 —— 给 ClientItemID 指定的那件物品写 17 个元素值。
        /// 处理函数 0x10073B40，字段 2 = ClientItemID，字段 3..19 = ys1..ys17。
        ///
        /// 寻址：10073BEA mov eax,[eax+0x508] 只遍历背包 m_ItemList，比 [item+0x18]
        /// （= ClientItemID），取**第一个**命中的；身上装备和英雄容器都不在范围内。
        /// 空背包或找不到走 10073BB5 BEFEFFFFFF mov esi,-2。
        ///
        /// 负值跳过而不是当 0 写：10073C8F 85C0 test eax,eax / 10073C91 0F88E0000000 js
        /// 直接跳到 10073D77 inc edi，本轮什么都不写。官方文档同一句：
        /// 「ys1~ys17：大于等于0表示设置这个元素值，小于0表示这个元素值不修改」。
        ///
        /// ys1 是 dword 且不夹取（10073CA4 89707C mov [eax+0x7C],esi），ys2..ys17 先
        /// 夹到 255（10073CAC 3DFF000000 cmp eax,0xFF / 7E07 jle / C745E4FF000000）
        /// 再按字节写。函数里没有任何发包调用，刷新是脚本自己的活（caret 29）。
        /// </summary>
        public int NpcGiveItemYs(int clientItemId, int[] ys)
        {
            if (!Enabled("自定义元素")) return 0;
            if (ys == null) return -2;
            TUserItem found = null;
            foreach (var item in _player.m_ItemList)
            {
                if (item == null || item.wIndex == 0) continue;
                if (_player.EnsureClientItemId(item) != clientItemId) continue;
                found = item;
                break;
            }
            if (found == null) return -2;
            // 10073C69 83FF12 cmp edi,0x12 / 0F8D0B010000 jge —— 循环 edi = 1..17，
            // 每轮 10073C7B call 0x10018460 取 vector::at(edi+2)。处理器入口只要求
            // 19 段（10073B8C 83F813 cmp eax,0x13），但 edi=17 要读第 20 段，所以
            // 正好 19 段时 at() 抛 out_of_range，被 10073D9A 的 SEH 收成 -3。
            for (var i = 0; i < 17; i++)
            {
                if (i >= ys.Length) return -3;
                var value = ys[i];
                if (value < 0) continue;
                if (i == 0) found.ys1 = value;
                else SetElementValue(found, i + 1, value);
            }
            return 1;                                   // 10073D7D BE01000000
        }

        private TUserItem FindOwnedItemByClientId(int clientItemId, bool allowMakeIndexFallback = true)
        {
            return _player?.FindOwnedItemByClientId(clientItemId, allowMakeIndexFallback);
        }

        private TUserItem FindOwnedItemByItemId(int itemId)
        {
            return FindOwnedItemByItemId(_player, itemId);
        }

        private static TUserItem FindOwnedItemByItemId(TPlayObject owner, int itemId)
        {
            if (owner == null || itemId == 0) return null;

            var item = owner.FindClientItemIn(owner.m_UseItems, itemId, true)
                       ?? owner.FindClientItemIn(owner.m_ItemList, itemId, true);
            if (item == null && owner.m_HeroObject != null)
            {
                item = owner.FindClientItemIn(owner.m_HeroObject.m_UseItems, itemId, true)
                       ?? owner.FindClientItemIn(owner.m_HeroObject.m_ItemList, itemId, true);
            }
            return item;
        }

        private TUserItem FindItemByItemId(int itemId, out TPlayObject owner)
        {
            owner = _player;
            var item = FindOwnedItemByItemId(owner, itemId);
            if (item != null) return item;

            var players = M2Share.UserEngine?.GetPlayerList();
            if (players == null) return null;
            foreach (var player in players)
            {
                if (player == null || ReferenceEquals(player, _player)) continue;
                item = FindOwnedItemByItemId(player, itemId);
                if (item == null) continue;
                owner = player;
                return item;
            }

            owner = null;
            return null;
        }

        private static TBaseObject FindObjectById(int roleId)
        {
            return roleId > 0 ? M2Share.ObjectManager?.Get(roleId) as TBaseObject : null;
        }

        private static void EnsureBindByteSlot(TUserItem item)
        {
            if (item == null) return;
            // 14 是 TUserItem.btValue 的原生宽度；绑定隧道写的是 btValue[10]
            // （item+0x34），所以门槛不能停在 9。
            if (item.btValue != null && item.btValue.Length >= 14) return;

            var replacement = new byte[14];
            if (item.btValue != null)
                Buffer.BlockCopy(item.btValue, 0, replacement, 0, Math.Min(item.btValue.Length, replacement.Length));
            item.btValue = replacement;
        }

        private static bool IsMonsterOrSlave(TBaseObject target)
        {
            return target != null
                   && target.m_btRaceServer >= Grobal2.RC_ANIMAL
                   && target.m_btRaceServer != Grobal2.RC_HEROOBJECT
                   && target is not TPlayObject;
        }

        private static bool KillRoleSilently(TBaseObject target)
        {
            if (!IsMonsterOrSlave(target) || target.m_boDeath)
                return false;

            var master = target.m_Master;
            target.m_WAbil.HP = 0;
            target.m_boDeath = true;
            target.m_dwDeathTick = HUtil32.GetTickCount();
            target.MakeGhost();
            if (master != null)
                master.m_SlaveList.Remove(target);
            target.m_Master = null;
            if (target.m_PEnvir != null && !target.m_boDelFormMaped)
            {
                target.m_PEnvir.DelObjectCount(target);
                target.m_boDelFormMaped = true;
            }
            return true;
        }

        private static string GetSpeakerName(TBaseObject target)
        {
            if (target == null) return string.Empty;
            return target.m_btRaceServer == Grobal2.RC_PLAYOBJECT
                ? target.m_sCharName
                : M2Share.FilterShowName(target.m_sCharName);
        }

        private static bool NameMatches(TBaseObject target, string name)
        {
            if (target == null || string.IsNullOrWhiteSpace(name)) return false;
            return string.Equals(M2Share.FilterShowName(target.m_sCharName), name,
                StringComparison.OrdinalIgnoreCase);
        }

        private static int ChebyshevDistance(int x1, int y1, int x2, int y2)
        {
            return Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
        }

        private void RefreshOwnedItem(TUserItem item)
        {
            RefreshOwnedItem(_player, item);
        }

        private static void RefreshOwnedItem(TPlayObject owner, TUserItem item)
        {
            if (item == null || owner == null)
                return;

            if (ContainsReference(owner.m_UseItems, item) || ContainsReference(owner.m_ItemList, item))
            {
                owner.SendUpdateItem(item);
                return;
            }

            var hero = owner.m_HeroObject;
            if (hero == null)
                return;

            if (ContainsReference(hero.m_UseItems, item))
            {
                hero.SendHeroUseItems();
                return;
            }

            if (ContainsReference(hero.m_ItemList, item))
                hero.SendHeroBagItems();
        }

        private static bool ContainsReference(IEnumerable<TUserItem> items, TUserItem item)
        {
            if (items == null || item == null)
                return false;

            foreach (var current in items)
            {
                if (ReferenceEquals(current, item))
                    return true;
            }
            return false;
        }

        /// <summary>旧版5元素给予</summary>
        public void GiveItem5El(string itemName, int ys1, int ys2, int ys3, int ys4, int ys5) { GiveNewItem(itemName, 0, new[] { ys1, ys2, ys3, ys4, ys5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }); }

        /// <summary>设置装备元素</summary>
        public int SetEquipElement(int bodyPos, int elemId, int value) { return GivePis(elemId, bodyPos, value); }

        /// <summary>获取装备元素</summary>
        public int GetEquipElement(int bodyPos, int elemId, int isHero) { return GetPis(elemId, bodyPos); }

        /// <summary>获取装备极品值: types 0=身体 1=背包 2=itemId</summary>
        public int GetItemExtreme(int types, int id, int jpIdx)
        {
            TUserItem item = null;
            if (types == 0)
            {
                if (id >= 0 && id < _player.m_UseItems.Length) item = _player.m_UseItems[id];
            }
            else if (types == 1)
            {
                if (id >= 0 && id < _player.m_ItemList.Count) item = _player.m_ItemList[id];
            }
            else if (types == 2)
            {
                foreach (var it in _player.m_ItemList)
                    if (it != null && it.MakeIndex == id) { item = it; break; }
            }
            if (item == null) return 0;
            return GetExtremeValue(item, ExtremeIndexFromJid(jpIdx));
        }

        /// <summary>
        /// ^35^/^36^ 的 jid 是 1 基的：1005D363 / 1005D5CA `8D 43 FF lea eax,[ebx-1]`
        /// 后 `83 F8 05 cmp eax,5` / `ja`。越界的 jid 不报错，落到 ja 之前预置的
        /// 偏移 0x2A（1005D35C / 1005D5C3 `C7 45 EC 2A 00 00 00`），也就是 jp2 那一格。
        /// </summary>
        private static int ExtremeIndexFromJid(int jid)
        {
            return jid >= 1 && jid <= 6 ? jid - 1 : 1;
        }

        /// <summary>设置装备极品值</summary>
        public int SetItemExtreme(int types, int id, int jpIdx, int value)
        {
            TUserItem item = null;
            if (types == 0)
            {
                if (id >= 0 && id < _player.m_UseItems.Length) item = _player.m_UseItems[id];
            }
            else if (types == 1)
            {
                if (id >= 0 && id < _player.m_ItemList.Count) item = _player.m_ItemList[id];
            }
            else if (types == 2)
            {
                foreach (var it in _player.m_ItemList)
                    if (it != null && it.MakeIndex == id) { item = it; break; }
            }
            if (item == null) return 0;
            // 1005D6E4 `88 04 1E mov byte [esi+ebx], al` 只写低字节，但
            // 1005D6E7 存回的是完整的 eax，所以返回值是原样的 val。
            SetExtremeValue(item, ExtremeIndexFromJid(jpIdx), (byte)value);
            return value;
        }

        /// <summary>
        /// ys_GiveDuar / 数字隧道 15 —— 装备持久（TUserItem.Dura，原生 item+0x26 的 word）。
        /// 处理函数 0x10072650，字段顺序 (pis, val, types) = fields[2..4]。
        /// types 0=查询 1=增加 2=减少 3=设置。全函数只 call vector::at / stoi / vector 析构，
        /// 没有任何发包或引擎调用，所以四条支路都不刷新客户端。
        /// </summary>
        public int EquipDura(int bodyPos, int value, int opType)
        {
            if (!Enabled("自定义元素")) return 0;
            // 10072722 83FE0F cmp esi,0xF / 10072725 0F8719010000 ja 0x10072844
            //   -> 10072853 B819FCFFFF mov eax,0xFFFFFC19
            if ((uint)bodyPos > 0xF) return -999;
            // 1007272B 85FF test edi,edi / 1007272D 7909 jns -> 1007272F C745E800000000
            // 10072738 81FFFFFF0000 cmp edi,0xFFFF / 1007273E 7E07 jle
            //   -> 10072740 C745E8FFFF0000
            if (value < 0) value = 0;
            else if (value > 0xFFFF) value = 0xFFFF;
            // 10072747 83F803 cmp eax,3 / 1007274A 0F87E8000000 ja 0x10072838
            //   -> 10072838 C745EC19FCFFFF mov [ebp-0x14],0xFFFFFC19
            if ((uint)opType > 3) return -999;
            var item = bodyPos < _player.m_UseItems.Length ? _player.m_UseItems[bodyPos] : null;
            // 100726C5 C745ECFFFFFFFF: 结果预置 -1。四条支路都在 `test eax,eax / je` 之后
            // 才写 [ebp-0x14]，所以空槽位一律返回 -1 且不写任何东西。
            if (item == null) return -1;
            switch (opType)
            {
                case 0:                                     // 10072757 臂
                    return item.Dura;                       // 1007276F 668B5826
                case 1:                                     // 100727A5 臂
                {
                    var sum = item.Dura + value;
                    // 100727C4 81FBFFFF0000 cmp ebx,0xFFFF / 100727CA 7E05 jle
                    if (sum > 0xFFFF) sum = 0xFFFF;
                    item.Dura = (ushort)sum;                // 100727D1 66895826
                    return sum;                             // 100727D5 895DEC
                }
                case 2:                                     // 100727DC 臂
                {
                    var diff = item.Dura - value;
                    // 100727FB 6683FB00 cmp bx,0 / 100727FF 7D02 jge —— 减法是 32 位的，
                    // 但判负只看低 16 位并按有符号解释。照抄，因为它有两个可观测后果：
                    //   Dura 在 32768..65535 之间时，减掉一小点得到的差仍落在 0x8000..0xFFFF，
                    //   被判成负数而清零（持久 >32.767 的装备一减就归零）；
                    //   反过来 val-Dura 超过 32768 时低 16 位又是正数，会被当成有效值写回
                    //   （Dura=0、val=40000 -> 写入 25536，返回 -40000）。
                    if ((short)diff < 0) diff = 0;          // 10072801 33DB xor ebx,ebx
                    item.Dura = (ushort)diff;               // 10072803 66895826 只写低 16 位
                    return diff;                            // 10072807 895DEC 存的是完整 32 位
                }
                default:                                    // 10072811 臂（types == 3）
                    item.Dura = (ushort)value;              // 1007282A 66895826
                    return value;                           // 1007282E 895DEC
            }
        }

        /// <summary>装备投保</summary>
        public int EquipInsurance(int bodyPos, int value) { return SetEquipElement(bodyPos, 6, value); }

        // ═══════════════════════════════════════════════════════════════
        // 6.2 技能伤害系统 — 9 functions
        // ═══════════════════════════════════════════════════════════════

        /// <summary>核心伤害公式: (maxDC - targetMaxAC) + (baseHp * (magicLv+1))/10 + cuttingV</summary>
        int CalcDamage(int magicLv, int baseHp, int cuttingV, TBaseObject target)
        {
            var atk = (int)_player.m_WAbil.DC;
            var def = (int)target.m_WAbil.AC;
            var raw = Math.Max(0, atk - def) + (baseHp * (magicLv + 1)) / 10 + cuttingV;
            return Math.Max(0, raw);
        }

        /// <summary>寻找范围内目标, includePlayers控制是否包含玩家</summary>
        List<TBaseObject> FindTargets(int x, int y, int range, bool players)
        {
            var list = new List<TBaseObject>();
            var envir = _player.m_PEnvir;
            if (envir == null) return list;
            envir.GetRangeBaseObject(x, y, range, players, list);
            return list;
        }

        /// <summary>ys_MyJn_plus — 基础版自定义伤害</summary>
        public int CustomDamage(int magicLv, int baseHp, int range, int tx, int ty, int canl, int types, int cuttingV)
        {
            if (!Enabled("刀刀切割")) return 0;
            return CustomDamageCore(magicLv, baseHp, range, tx, ty, canl, types, cuttingV);
        }

        private int CustomDamageCore(int magicLv, int baseHp, int range, int tx, int ty,
            int canl, int types, int cuttingV)
        {
            int total = 0;
            foreach (var t in FindTargets(tx, ty, range, canl != 0))
            {
                var dmg = CalcDamage(magicLv, baseHp, cuttingV, t);
                if (dmg > 0) { t.m_WAbil.HP -= Math.Min(t.m_WAbil.HP, dmg); total += dmg; }
            }
            return total;
        }

        /// <summary>ys_MyJn_plus2 — 带半径类型(lei:0=圆形 1=直线)</summary>
        public int CustomDamage2(int magicLv, int baseHp, int range, int tx, int ty, int canl, int types, int cuttingV, int lei) { return CustomDamage(magicLv, baseHp, range, tx, ty, canl, types, cuttingV); }

        /// <summary>ys_MyJn_effect — 带特效ID</summary>
        public int CustomDamageEffect(int magicLv, int baseHp, int range, int tx, int ty,
            int canl, int types, int cuttingV, int lei, int effect)
        {
            if (!EnabledAll("眼神特殊函数", "自定义伤害_plus", "super攻击触发")) return 0;
            return CustomDamageCore(magicLv, baseHp, range, tx, ty, canl, types, cuttingV);
        }

        /// <summary>ys_MyJn_undead — 对不死族额外伤害(千分比, 1500=1.5倍)</summary>
        public int CustomDamageUndead(int magicLv, int baseHp, int range, int tx, int ty, int canl, int types, int cuttingV, int lei, int effect, int undead) { int d = CustomDamage(magicLv, baseHp, range, tx, ty, canl, types, cuttingV); return d * undead / 1000; }

        /// <summary>ys_MyJn_super — 完整版(MgId魔法ID, AttactId攻击ID)</summary>
        public int CustomDamageSuper(int magicLv, int baseHp, int range, int tx, int ty, int canl, int types, int cuttingV, int lei, int effect, int undead, int mgId, int attId) { return CustomDamageUndead(magicLv, baseHp, range, tx, ty, canl, types, cuttingV, lei, effect, undead); }

        /// <summary>ys_MyJn_delay — 终极版(含延迟ms, 翻倍千分比)</summary>
        public int CustomDamageDelay(int magicLv, int baseHp, int range, int tx, int ty, int canl, int types, int cuttingV, int lei, int effect, int undead, int mgId, int attId, int double_, int delayMs) { return CustomDamageSuper(magicLv, baseHp, range, tx, ty, canl, types, cuttingV, lei, effect, undead, mgId, attId) * double_ / 1000; }

        /// <summary>ys_Cutting — 神圣伤害(无视防御)</summary>
        public int HolyDamage(int range, int tx, int ty, int canl, int types, int cuttingV, int lei, int effect, int attId, int delayMs)
        {
            if (!Enabled("刀刀切割")) return 0;
            int total = 0;
            foreach (var t in FindTargets(tx, ty, range, canl != 0))
            {
                if (cuttingV > 0) { t.m_WAbil.HP -= Math.Min(t.m_WAbil.HP, cuttingV); total += cuttingV; }
            }
            return total;
        }

        /// <summary>Ys_MyYsJn — 14参数超级伤害(含ys_id元素ID, Doubling翻倍, lei字符串类型)</summary>
        public int SuperDamage14(int magicLv, int baseHp, int range, int tx, int ty, int canl, int types, int cuttingV, int ysId, int v1, int doubling, string lei) { return CustomDamage(magicLv, baseHp, range, tx, ty, canl, types, cuttingV) * doubling / 1000; }

        /// <summary>Ys_Attact — 直接攻击指定RoleId造成hp伤害</summary>
        public void DirectAttack(int roleId, int hp)
        {
            if (!Enabled("刀刀切割")) return;
            var list = new List<TBaseObject>();
            _player.m_PEnvir?.GetRangeBaseObject(_player.m_nCurrX, _player.m_nCurrY, 20, true, list);
            foreach (var t in list)
            {
                if (t.ObjectId == roleId)
                {
                    if (hp > 0)
                        t.m_WAbil.HP = TBaseObject.ClampAbility((long)t.m_WAbil.HP - hp);
                    else
                        t.m_WAbil.HP = (int)Math.Min(t.m_WAbil.MaxHP,
                            (long)t.m_WAbil.HP - hp);
                    t.SendRefMsg(Grobal2.RM_STRUCK, (short)Math.Abs(hp), t.m_WAbil.HP, t.m_WAbil.MaxHP, _player.ObjectId, "");
                    break;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 6.3 控制技能 — 12 functions
        // ═══════════════════════════════════════════════════════════════

        /// <summary>麻痹: timer秒, rand概率(100=100%), round范围</summary>
        public int Paralysis(int timerSec, int probability, int range, int tx, int ty, int canl, bool isAoe)
        {
            if (!Enabled("麻痹概率")) return 0;
            int count = 0;
            foreach (var t in FindTargets(tx, ty, range, canl != 0))
            {
                if (M2Share.RandomNumber.Random(100) < probability)
                {
                    // Use Stone status (POISON_STONE=5) for paralysis effect — duration in seconds
                    t.MakePosion(Grobal2.POISON_STONE, timerSec, 0);
                    count++;
                }
            }
            return count;
        }

        /// <summary>施毒: leix 0=红毒 1=绿毒, hp每跳伤害, gailv概率</summary>
        public int Poison(int duration, int type, int hpPerTick, int probability, int range, int tx, int ty, int canl, int isAoe)
        {
            if (!Enabled("施毒术")) return 0;
            return PoisonCore(duration, type, hpPerTick, probability, range, tx, ty, canl, isAoe);
        }

        private int PoisonCore(int duration, int type, int hpPerTick, int probability,
            int range, int tx, int ty, int canl, int isAoe)
        {
            int count = 0;
            // type: 0=red(POISON_DAMAGEARMOR), 1=green(POISON_DECHEALTH)
            int poisonType = type == 0 ? Grobal2.POISON_DAMAGEARMOR : Grobal2.POISON_DECHEALTH;
            foreach (var t in FindTargets(tx, ty, range, canl != 0))
            {
                if (M2Share.RandomNumber.Random(100) < probability)
                {
                    t.MakePosion(poisonType, duration, hpPerTick);
                    count++;
                }
            }
            return count;
        }
        public int PoisonEffect(int duration, int type, int hpPerTick, int probability,
            int range, int tx, int ty, int canl, int isAoe, int effect)
        {
            if (!EnabledAll("眼神特殊函数", "super攻击触发")) return 0;
            return PoisonCore(duration, type, hpPerTick, probability, range, tx, ty, canl, isAoe);
        }

        /// <summary>命令16：只向调用玩家发送原始客户端消息。</summary>
        public int SendDirectMessage(int recog, int param, int tag, int series, int ident, string body)
        {
            if (!Enabled("眼神特殊函数")) return 0;
            var message = MakeDirectMessage(recog, param, tag, series, ident);
            _player.SendSocket(message, body ?? string.Empty);
            return 0;
        }

        /// <summary>命令22：以解析后的Recog对象坐标为中心向调用者地图内玩家发送消息。</summary>
        public int SendGroundMessage(int recog, int param, int tag, int series, int ident, int range, string body)
        {
            if (!Enabled("眼神特殊函数")) return 0;
            var source = ResolveGroundMessageSource(_player, recog);
            if (source == null) return 0;

            var message = MakeGroundMessage(source, param, tag, series, ident);
            foreach (var recipient in FindGroundMessageRecipients(_player, source, range))
                recipient.SendSocket(message, body ?? string.Empty);
            return 0;
        }

        private static TBaseObject ResolveGroundMessageSource(TPlayObject caller, int recog)
        {
            if (caller == null) return null;
            if (recog == -1) return caller.m_TargetCret ?? caller;
            if (recog == 0) return caller;
            return M2Share.ObjectManager?.Get(recog) ?? caller;
        }

        private static ClientPacket MakeDirectMessage(int recog, int param, int tag,
            int series, int ident)
        {
            return Grobal2.MakeDefaultMsg(ident, recog, param, tag, series);
        }

        private static ClientPacket MakeGroundMessage(TBaseObject source, int param, int tag,
            int series, int ident)
        {
            return Grobal2.MakeDefaultMsg(ident, source.ObjectId, param, tag, series);
        }

        private static List<TPlayObject> FindGroundMessageRecipients(TPlayObject caller,
            TBaseObject source, int range)
        {
            var recipients = new List<TPlayObject>();
            var envir = caller?.m_PEnvir;
            if (envir == null || source == null || range <= 0) return recipients;

            var objects = new List<TBaseObject>();
            envir.GetMapBaseObjects(source.m_nCurrX, source.m_nCurrY, range, objects);
            var minX = (long)source.m_nCurrX - range;
            var maxX = (long)source.m_nCurrX + range;
            var minY = (long)source.m_nCurrY - range;
            var maxY = (long)source.m_nCurrY + range;
            var seen = new HashSet<int>();
            foreach (var actor in objects)
            {
                if (actor?.GetType() != typeof(TPlayObject)) continue;
                if (actor.m_nCurrX < 0 || actor.m_nCurrY < 0
                    || actor.m_nCurrX < minX || actor.m_nCurrX >= maxX
                    || actor.m_nCurrY < minY || actor.m_nCurrY >= maxY)
                    continue;
                var player = (TPlayObject)actor;
                if (seen.Add(player.ObjectId)) recipients.Add(player);
            }
            return recipients;
        }

        /// <summary>推开: juli距离, fangxiang 0=后退 1=拉进</summary>
        public int PushEnemy(int distance, int direction, int probability, int range, int tx, int ty, int canl, int isAoe)
        {
            if (!Enabled("野蛮麻痹")) return 0;
            int count = 0;
            foreach (var t in FindTargets(tx, ty, range, canl != 0))
            {
                if (M2Share.RandomNumber.Random(100) < probability)
                {
                    // direction: 0=push away (back dir), 1=pull towards player
                    var playerBase = _player as TBaseObject;
                    if (playerBase == null) continue;
                    byte pushDir;
                    if (direction == 0)
                    {
                        // Push away from player
                        pushDir = M2Share.GetNextDirection(_player.m_nCurrX, _player.m_nCurrY, t.m_nCurrX, t.m_nCurrY);
                    }
                    else
                    {
                        // Pull towards player (push in reverse direction)
                        pushDir = M2Share.GetNextDirection(t.m_nCurrX, t.m_nCurrY, _player.m_nCurrX, _player.m_nCurrY);
                    }
                    t.CharPushed(pushDir, distance);
                    count++;
                }
            }
            return count;
        }
        public int PushEnemy2(int distance, int direction, int probability, int range, int tx, int ty, int canl, int isAoe, int roleId) { return PushEnemy(distance, direction, probability, range, tx, ty, canl, isAoe); }

        /// <summary>拉人/定身: why 0=拉人 1=拉回</summary>
        public int PullEnemy(int why, int level, int probability, int range, int tx, int ty, int canl, int isAoe)
        {
            if (!EnabledAll("眼神特殊函数", "super攻击触发")) return 0;
            return PullEnemyCore(why, level, probability, range, tx, ty, canl, isAoe);
        }

        private int PullEnemyCore(int why, int level, int probability, int range,
            int tx, int ty, int canl, int isAoe)
        {
            int count = 0;
            foreach (var t in FindTargets(tx, ty, range, canl != 0))
            {
                if (M2Share.RandomNumber.Random(100) < probability)
                {
                    // why: 0=teleport to player, 1=pull towards player (level = distance)
                    if (why == 0)
                    {
                        // Teleport target to a nearby position
                        short newX = _player.m_nCurrX;
                        short newY = _player.m_nCurrY;
                        if (t.m_PEnvir != null)
                        {
                            t.m_PEnvir.DeleteFromMap(t.m_nCurrX, t.m_nCurrY,
                                CellType.OS_MOVINGOBJECT, t, false);
                            t.m_nCurrX = newX;
                            t.m_nCurrY = newY;
                            t.m_PEnvir.AddToMap(t.m_nCurrX, t.m_nCurrY, CellType.OS_MOVINGOBJECT, t);
                            t.SendRefMsg(Grobal2.RM_SPACEMOVE_SHOW, t.m_btDirection, t.m_nCurrX, t.m_nCurrY, 0, "");
                        }
                    }
                    else
                    {
                        byte pullDir = M2Share.GetNextDirection(t.m_nCurrX, t.m_nCurrY, _player.m_nCurrX, _player.m_nCurrY);
                        t.CharPushed(pullDir, level);
                    }
                    count++;
                }
            }
            return count;
        }
        public int PullEnemy2(int why, int level, int probability, int range, int tx,
            int ty, int canl, int isAoe, int roleId)
        {
            if (!Enabled("野蛮麻痹")) return 0;
            return PullEnemyCore(why, level, probability, range, tx, ty, canl, isAoe);
        }
        /// <summary>定身: duration秒, 使用LockRun状态冻结</summary>
        public int RootTarget(int duration)
        {
            if (!Enabled("野蛮麻痹")) return 0;
            // Freeze position by locking movement
            _player.m_wStatusTimeArr[Grobal2.STATE_LOCKRUN] = (ushort)duration;
            _player.m_nCharStatus = _player.GetCharStatus();
            _player.StatusChanged();
            return duration;
        }

        /// <summary>吸血: fixedHp固定, percentHp千分比</summary>
        public int LifeSteal(int fixedHp, int percentHp)
        {
            if (!Enabled("攻击吸血")) return 0;
            var steal = TBaseObject.ClampAbility((long)fixedHp
                + (long)_player.m_WAbil.MaxHP * percentHp / 1000);
            _player.m_WAbil.HP = (int)Math.Min(_player.m_WAbil.MaxHP,
                (long)_player.m_WAbil.HP + steal);
            return steal;
        }

        public int VacuumMonstersEx(int range, int levelLimit, int maxCount)
        {
            if (!Enabled("全屏吸怪")) return 0;
            var envir = _player.m_PEnvir; if (envir == null) return 0;
            var list = new List<TBaseObject>();
            M2Share.UserEngine.GetMapMonster(envir, list);
            int pulled = 0;
            foreach (var m in list)
            {
                if (m == null || m.m_boDeath || m.m_btRaceServer == Grobal2.RC_PLAYOBJECT) continue;
                if (levelLimit > 0 && m.m_Abil.Level > levelLimit) continue;
                int dx = Math.Abs(m.m_nCurrX - _player.m_nCurrX);
                int dy = Math.Abs(m.m_nCurrY - _player.m_nCurrY);
                if (dx <= range && dy <= range)
                {
                    m.m_nCurrX = _player.m_nCurrX;
                    m.m_nCurrY = _player.m_nCurrY;
                    pulled++;
                }
                if (maxCount > 0 && pulled >= maxCount) break;
            }
            return pulled;
        }

        // ═══════════════════════════════════════════════════════════════
        // 6.4 增益/减益/治疗 — 14 functions
        // ═══════════════════════════════════════════════════════════════

        /// <summary>自愈术: oneHp单人治疗, allHp全体治疗, isStack叠加</summary>
        public int Healing(int range, int tx, int ty, int oneHp, int allHp, int isStack, int roleId, int effect)
        {
            if (!Enabled("刀刀切割")) return 0;
            int totalHeal = 0;
            if (range > 0)
            {
                // AoE heal on targets in range
                foreach (var t in FindTargets(tx, ty, range, true))
                {
                    t.IncHealthSpell(allHp, 0);
                    totalHeal += allHp;
                }
            }
            // Self/roleId heal
            if (roleId == 0 || roleId == _player.ObjectId)
            {
                _player.IncHealthSpell(oneHp, 0);
                totalHeal += oneHp;
            }
            else
            {
                // Find creature by roleId and heal
                var list = new List<TBaseObject>();
                _player.m_PEnvir?.GetRangeBaseObject(_player.m_nCurrX, _player.m_nCurrY, 20, true, list);
                foreach (var t in list)
                {
                    if (t.ObjectId == roleId) { t.IncHealthSpell(oneHp, 0); totalHeal += oneHp; break; }
                }
            }
            return totalHeal;
        }

        /// <summary>减少目标临时属性: attrId 0=DC 1=MC 2=SC 3=AC 4=MAC 5=HP 6=MP</summary>
        public int SubTempAttr(int range, int tx, int ty, int value, int duration, int attrId, int roleId, int effect)
        {
            if (!Enabled("刀刀切割")) return 0;
            foreach (var t in FindTargets(tx, ty, range, true))
                ModifyStat(t, attrId, -value, duration);
            return value;
        }

        /// <summary>增加临时属性: isOther 0=敌人 1=队友 2=自己</summary>
        public int AddTempAttr(int range, int tx, int ty, int value, int duration, int attrId, int roleId, int effect, int isOther)
        {
            if (!Enabled("刀刀切割")) return 0;
            foreach (var t in FindTargets(tx, ty, range, true))
                ModifyStat(t, attrId, value, duration);
            return value;
        }

        /// <summary>增加临时属性Pro: types 0=不限 1=只怪物 2=只人物</summary>
        public int AddTempAttrPro(int range, int tx, int ty, int value, int duration, int attrId, int roleId, int effect, int isOther, int types)
        {
            if (!Enabled("刀刀切割")) return 0;
            foreach (var t in FindTargets(tx, ty, range, true))
            {
                // types filter: 0=all, 1=monster only, 2=player only
                if (types == 1 && t.m_btRaceServer == Grobal2.RC_PLAYOBJECT) continue;
                if (types == 2 && t.m_btRaceServer != Grobal2.RC_PLAYOBJECT) continue;
                ModifyStat(t, attrId, value, duration);
            }
            return value;
        }

        /// <summary>修改目标属性值: attrId 0=DC 1=MC 2=SC 3=AC 4=MAC 5=HP 6=MP</summary>
        void ModifyStat(TBaseObject t, int attrId, int delta, int durationSec)
        {
            switch (attrId)
            {
                case 0: t.m_WAbil.DC = HUtil32.MakeLong(HUtil32.LoWord(t.m_WAbil.DC) + delta, HUtil32.HiWord(t.m_WAbil.DC) + delta); break;
                case 1: t.m_WAbil.MC = HUtil32.MakeLong(HUtil32.LoWord(t.m_WAbil.MC) + delta, HUtil32.HiWord(t.m_WAbil.MC) + delta); break;
                case 2: t.m_WAbil.SC = HUtil32.MakeLong(HUtil32.LoWord(t.m_WAbil.SC) + delta, HUtil32.HiWord(t.m_WAbil.SC) + delta); break;
                case 3: t.m_WAbil.AC = HUtil32.MakeLong(HUtil32.LoWord(t.m_WAbil.AC) + delta, HUtil32.HiWord(t.m_WAbil.AC) + delta); break;
                case 4: t.m_WAbil.MAC = HUtil32.MakeLong(HUtil32.LoWord(t.m_WAbil.MAC) + delta, HUtil32.HiWord(t.m_WAbil.MAC) + delta); break;
                case 5:
                    t.m_WAbil.MaxHP = TBaseObject.ClampAbility((long)t.m_WAbil.MaxHP + delta);
                    t.m_WAbil.HP = (int)Math.Clamp((long)t.m_WAbil.HP + delta,
                        0, t.m_WAbil.MaxHP);
                    break;
                case 6:
                    t.m_WAbil.MaxMP = TBaseObject.ClampAbility((long)t.m_WAbil.MaxMP + delta);
                    t.m_WAbil.MP = (int)Math.Clamp((long)t.m_WAbil.MP + delta,
                        0, t.m_WAbil.MaxMP);
                    break;
            }
            t.RecalcAbilitys();
            t.SendMsg(t, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
        }

        /// <summary>增加HP/MP上限</summary>
        public int AddMaxHp(int amount) { _player.m_WAbil.MaxHP = TBaseObject.ClampAbility((long)_player.m_WAbil.MaxHP + amount); _player.m_WAbil.HP = TBaseObject.ClampAbility((long)_player.m_WAbil.HP + amount); _player.RecalcAbilitys(); return amount; }
        public int AddMaxMp(int amount) { _player.m_WAbil.MaxMP = TBaseObject.ClampAbility((long)_player.m_WAbil.MaxMP + amount); _player.m_WAbil.MP = TBaseObject.ClampAbility((long)_player.m_WAbil.MP + amount); _player.RecalcAbilitys(); return amount; }

        /// <summary>给予经验</summary>
        public int GiveExp(int amount) { _player.m_Abil.Exp += amount; return amount; }

        /// <summary>减少经验: downLevel是否可降级, tips是否提示</summary>
        public int DecExp(int amount, int downLevel, int tips)
        {
            if (!Enabled("眼神特殊函数")) return 0;
            _player.m_Abil.Exp = Math.Max(0, _player.m_Abil.Exp - amount);
            return amount;
        }

        /// <summary>设置技能免伤倍数: key标识, id技能ID, value倍数</summary>
        private static readonly ConcurrentDictionary<(string key, int id), int> _skillDmgReduction = new();
        public int SetSkillDmgReduction(string key, int id, int value)
        {
            if (!EnabledAll("眼神特殊函数", "指定技能id免伤")) return 0;
            _skillDmgReduction[(key, id)] = value;
            return value;
        }

        public int GetSkillDmgReduction(string key, int id)
        {
            if (!EnabledAll("眼神特殊函数", "指定技能id免伤")) return 0;
            return _skillDmgReduction.TryGetValue((key, id), out var v) ? v : 0;
        }

        // CD time storage per-slot
        private static readonly ConcurrentDictionary<int, int> _cdSlots = new ConcurrentDictionary<int, int>();
        /// <summary>CD时间系统 — 获取slot上次记录时间(秒)</summary>
        public int CDGetTimes(int slot) { return _cdSlots.TryGetValue(slot, out var v) ? v : 0; }
        public int CDGetTimesMs() { return Environment.TickCount; }
        public bool CDCmpTime(int vx, int vy, int diff) { return Math.Abs(vx - vy) >= diff; }
        public bool CDCmpTimeMs(int vx, int vy, int diff) { return Math.Abs(vx - vy) >= diff; }
        public int CDGetRemaining(int vx, int vy, int diff) { return Math.Max(0, diff - Math.Abs(vx - vy)); }
        public int CDGetDiff(int vx, int vy) { return Math.Abs(vx - vy); }
        public void CDSet(int slot, int durationMs)
        {
            if (!Enabled("毫秒级cd记录")) { _cdSlots[slot] = Environment.TickCount + durationMs; return; }
            _cdSlots[slot] = Environment.TickCount + durationMs;
        }

        // ═══════════════════════════════════════════════════════════════
        // 6.5 宝宝/宠物系统 — 10 functions
        // ═══════════════════════════════════════════════════════════════

        /// <summary>召唤带属性的宝宝 — 生成怪物并设置指定属性</summary>
        public int SummonPet(string monName, int count, int level, int ac, int dc, int dcMax, int mac, int mc, int sc, int gs, int ys, int hp, int maxHp)
        {
            if (!Enabled("眼神特殊函数")) return 0;
            return SummonPetCore(monName, count, level, ac, dc, dcMax, mac, mc, sc,
                gs, ys, hp, maxHp);
        }

        private int SummonPetCore(string monName, int count, int level, int ac, int dc,
            int dcMax, int mac, int mc, int sc, int gs, int ys, int hp, int maxHp)
        {
            int summoned = 0;
            for (int i = 0; i < count; i++)
            {
                var mon = M2Share.UserEngine.RegenMonsterByName(_player.m_PEnvir, _player.m_nCurrX, _player.m_nCurrY, monName);
                if (mon != null)
                {
                    // Set custom attributes on the newly-summoned monster
                    mon.m_WAbil.AC = HUtil32.MakeLong(ac, ac);
                    mon.m_WAbil.DC = HUtil32.MakeLong(dc, dcMax);
                    mon.m_WAbil.MAC = HUtil32.MakeLong(mac, mac);
                    mon.m_WAbil.MC = HUtil32.MakeLong(mc, mc);
                    mon.m_WAbil.SC = HUtil32.MakeLong(sc, sc);
                    if (hp > 0) mon.m_WAbil.HP = hp;
                    if (maxHp > 0) mon.m_WAbil.MaxHP = maxHp;
                    if (mon.m_WAbil.HP > mon.m_WAbil.MaxHP) mon.m_WAbil.HP = mon.m_WAbil.MaxHP;
                    if (level > 0) mon.m_Abil.Level = (ushort)level;
                    mon.RecalcAbilitys();
                    // Add to player's slave list so it follows/obeys
                    _player.m_SlaveList.Add(mon);
                    mon.m_Master = _player;
                    summoned++;
                }
            }
            return summoned;
        }

        public int SummonPetRoyalty(string monName, int count, int level, int ac, int dc,
            int dcMax, int mac, int mc, int sc, int gs, int ys, int hp, int maxHp,
            int royaltySec)
        {
            if (!Enabled("特殊宝宝")) return 0;
            return SummonPetCore(monName, count, level, ac, dc, dcMax, mac, mc, sc,
                gs, ys, hp, maxHp);
        }

        /// <summary>
        /// 数字隧道 23 / <c>ys_SetPetV</c>。处理函数 <c>0x100735B0</c>（唯一调用
        /// <c>0x10077289</c>）。向量长度 <c>0x100735FF 83 F8 0E cmp eax,0xE / jb</c>
        /// 要求 ≥14 个 <c>std::string</c>（含 <c>!!!!集成函数</c> 与命令号）。
        /// 字段 <c>at(2)=id … at(0xC)=Maxhp at(0xD)=MonName</c>。
        /// 从宠表是宿主 <c>[player+0x4FC]</c>（<c>0x10073760 8B 83 FC 04 00 00</c>）。
        /// 名字长度在 <c>[ebp-0x1C]</c>（MSVC string.size，<c>0x10073778 8B 55 E4</c>）：
        /// 空名走 1-based 槽（<c>0x10073799 85 F6 / jle</c>、<c>0x100737A1 3B F0 / jg</c>、
        /// <c>0x100737AC 8D 04 B5 FC FF FF FF lea eax,[esi*4-4]</c>）；
        /// 非空名按名扫描全表（<c>0x10073970 3B F0 / 0x10073B15 46 inc esi</c>）。
        /// 每项 <c>test/cmp ; jle</c> 仅 <c>&gt;0</c> 才写，偏移与 JSON 应用点相同。
        /// 成功失败都 <c>0x10073936 83 C8 FF or eax,0xffffffff</c> 返回 -1。
        /// gs/ys 的「怪物伤害触发技能特效」约束的是消费端，不挡写入。
        /// </summary>
        public int SetPetAttr(string monName, int id, int ac, int dc, int dcMax, int mac, int mc, int sc, int gs, int ys, int hp, int maxHp)
        {
            if (!Enabled("眼神特殊函数")) return 0;
            return ApplySetPetV(_player?.m_SlaveList, monName, id, ac, dc, dcMax,
                mac, mc, sc, gs, ys, hp, maxHp);
        }

        internal static int ApplySetPetV(IList<TBaseObject> slaves, string monName,
            int id, int ac, int dc, int dcMax, int mac, int mc, int sc,
            int gs, int ys, int hp, int maxHp)
        {
            if (slaves == null || slaves.Count <= 0)
                return -1;

            if (string.IsNullOrEmpty(monName))
            {
                if (id <= 0 || id > slaves.Count)
                    return -1;
                var slave = slaves[id - 1];
                if (slave == null)
                    return -1;
                ApplyYanshenMonsterAttrs(slave, ac, mac, dc, dcMax, mc, sc,
                    speed: 0, hit: 0, hp, maxHp, gs, ys);
                return -1;
            }

            foreach (var slave in slaves)
            {
                if (slave == null) continue;
                if (slave.m_sCharName == monName)
                    ApplyYanshenMonsterAttrs(slave, ac, mac, dc, dcMax, mc, sc,
                        speed: 0, hit: 0, hp, maxHp, gs, ys);
            }
            return -1;
        }

        /// <summary>给宝宝技能: magicId技能, gailv概率, shanghai伤害, del删除(1=删除)</summary>
        public int GivePetSkill(int magicId, int probability, int damage, int del, string petName)
        {
            if (!Enabled("怪物伤害触发技能特效")) return 0;
            foreach (var slave in _player.m_SlaveList)
            {
                if (slave == null) continue;
                if (string.IsNullOrEmpty(petName) || slave.m_sCharName == petName)
                {
                    if (del == 1)
                    {
                        // Remove magic
                        for (int i = slave.m_MagicList.Count - 1; i >= 0; i--)
                            if (slave.m_MagicList[i].wMagIdx == magicId)
                                slave.m_MagicList.RemoveAt(i);
                    }
                    else
                    {
                        var magic = M2Share.UserEngine.FindMagic(magicId);
                        if (magic != null && !slave.IsTrainingSkill(magicId))
                        {
                            slave.m_MagicList.Add(new TUserMagic { MagicInfo = magic, wMagIdx = magic.wMagicID, btLevel = 1, btKey = 0 });
                        }
                    }
                }
            }
            return magicId;
        }

        /// <summary>给宝宝特殊属性: '倍功'/'切割'/'暴击'/'连击'/'连击削弱'</summary>
        public int GivePetSpecialAttr(int key1, int key2, string attrType, string petName)
        {
            if (!Enabled("怪物伤害触发技能特效")) return 0;
            // Store special attributes on matching pet/slave objects
            foreach (var slave in _player.m_SlaveList)
            {
                if (slave == null) continue;
                if (string.IsNullOrEmpty(petName) || slave.m_sCharName == petName)
                {
                    switch (attrType)
                    {
                        case "倍功": slave.m_nHitDouble = (ushort)key1; break;
                        case "暴击": slave.m_btHitPoint = (byte)key1; break;
                        case "切割": slave.m_nHitPlus = (ushort)key1; break;
                    }
                }
            }
            return key1;
        }

        /// <summary>宝宝跟随攻击指定目标</summary>
        public int PetFollowAttack(int roleId)
        {
            if (!Enabled("眼神特殊函数")) return 0;
            foreach (var slave in _player.m_SlaveList)
            {
                if (slave == null) continue;
                // Set target for slave to follow and attack
                if (roleId == 0) { slave.m_TargetCret = null; continue; }
                var list = new List<TBaseObject>();
                _player.m_PEnvir?.GetRangeBaseObject(_player.m_nCurrX, _player.m_nCurrY, 30, true, list);
                foreach (var t in list)
                    if (t.ObjectId == roleId) { slave.m_TargetCret = t; break; }
            }
            return roleId;
        }

        /// <summary>指定英雄释放技能</summary>
        public int HeroCastSkill(int magicId, int isRun)
        {
            if (!Enabled("指定英雄放技能")) return 0;
            return YanshenHeroCastState.Set(_player?.m_sCharName, magicId,
                isRun);
        }

        /// <summary>按名字杀死宝宝</summary>
        public int KillPetByName(string name)
        {
            if (!Enabled("特殊宝宝")) return 0;
            int count = 0;
            for (int i = _player.m_SlaveList.Count - 1; i >= 0; i--)
            {
                var slave = _player.m_SlaveList[i];
                if (slave == null || slave.m_boDeath) continue;
                if (string.IsNullOrEmpty(name) || slave.m_sCharName == name)
                {
                    slave.m_WAbil.HP = 0;
                    slave.m_boDeath = true;
                    slave.m_dwDeathTick = HUtil32.GetTickCount();
                    count++;
                }
            }
            return count;
        }

        /// <summary>按名字获取宝宝属性: types 0=X 1=Y 2=HP 3=MaxHP 4=DC 5=MC ...
        public int GetPetAttrByName(string name, int types)
        {
            if (!Enabled("特殊宝宝")) return 0;
            foreach (var slave in _player.m_SlaveList)
            {
                if (slave == null || slave.m_boDeath) continue;
                if (string.IsNullOrEmpty(name) || slave.m_sCharName == name)
                {
                    return types switch
                    {
                        0 => slave.m_nCurrX,
                        1 => slave.m_nCurrY,
                        2 => slave.m_WAbil.HP,
                        3 => slave.m_WAbil.MaxHP,
                        4 => HUtil32.LoWord(slave.m_WAbil.DC),
                        5 => HUtil32.HiWord(slave.m_WAbil.DC),
                        6 => HUtil32.LoWord(slave.m_WAbil.MC),
                        7 => HUtil32.HiWord(slave.m_WAbil.MC),
                        8 => HUtil32.LoWord(slave.m_WAbil.SC),
                        9 => HUtil32.HiWord(slave.m_WAbil.SC),
                        10 => HUtil32.LoWord(slave.m_WAbil.AC),
                        11 => HUtil32.HiWord(slave.m_WAbil.AC),
                        12 => HUtil32.LoWord(slave.m_WAbil.MAC),
                        13 => HUtil32.HiWord(slave.m_WAbil.MAC),
                        _ => 0
                    };
                }
            }
            return 0;
        }

        // ═══════════════════════════════════════════════════════════════
        // 6.6 物品/背包 — 13 functions
        // ═══════════════════════════════════════════════════════════════

        /// <summary>GetV/SetV 拒绝参数时的返回值，与 PasApiBridge 同一常量（0x6DF1F1 预置）。</summary>
        private const int NativeScriptVarMiss = -1;

        /// <summary>
        /// 极品位 1-6。作者原文只给了 This_player.GetV(N,1~6) 一句，现由插件字节定案：
        /// 脱壳转储（基址 0x10000000）里 <c>极品开关</c>（VA 0x102BF804）取到变量组号后，
        /// <c>0x1006C222</c> 起是恰好六次 <c>GetV(组号, N)</c>，N 由
        /// <c>0x1006C232 B9 01 00 00 00</c> 到 <c>0x1006C2D2 B9 06 00 00 00</c> 连续取 1..6，
        /// 第七次不存在（<c>0x1006C2E3 popal</c> 收尾）。物品侧对应
        /// <c>0x1006C10A..0x1006C137</c> 读 <c>item+0x2A..0x2F</c> 六个字节。
        /// </summary>
        private const int RecycleExtremeSlots = 6;

        /// <summary>
        /// 元素位 1-17。原先按 Ys_GetOther(types=1) 的公布范围推断，现由插件字节确证：
        /// 脱壳转储 <c>staging/yanshen208_strparam_runtime_dump_20260719/</c>
        /// <c>yanshen2_0_8_dll.memory.bin</c>（基址 0x10000000）里，读到配置键
        /// <c>元素开关</c>（VA 0x102BF810）并通过 <c>0x1006C532 cmp byte [eax+8],0</c>
        /// 判开关之后，是一段完全展开的取值序列：<c>0x1006C579</c> 起连续 17 次
        /// <c>push N / call 0x10065F00</c>，N 恰为 1..17 连续无缺口，末次在 <c>0x1006C68F</c>。
        /// </summary>
        private const int RecycleElementSlots = 17;

        /// <summary>
        /// 回收的灵符 reason 是 0，不是 NPC 给予那套 23001。插件在 0x1006CE65 直接调
        /// M2Server 的加灵符 0x6DE7BC，并在 0x1006CE68 用 <c>33 D2 xor edx,edx</c>
        /// 把 reason 参数清零。23001 出自另一条路 —— NPC Give 派发器
        /// <c>0x6C88FB BA D9 59 00 00 mov edx,0x59D9</c>。
        /// </summary>
        private const int RecycleLingFuReason = 0;

        /// <summary>回收功能不可用（配置缺根键、解析失败或结算中途抛异常）。</summary>
        private const int RecycleUnusable = -999;

        /// <summary>回收跑完，与件数无关。</summary>
        private const int RecycleDone = 1;

        /// <summary>
        /// 自动回收 — 按 JSON 配置回收背包物品。
        ///
        /// 返回值只有两种，原生不返回件数：入口 0x1006CF10 在有效位
        /// <c>0x1031B8C5</c> 为 0 时 <c>0x1006CF20 B8 19 FC FF FF</c> 返回 -999；
        /// 正常出口 <c>0x1006CECC B8 01 00 00 00</c> 恒返回 1
        /// （前一条 <c>0x1006CEC6 mov eax,0x3E7</c> 是作者留下的死代码，被这条盖掉）；
        /// 异常臂 <c>0x1006CEEA B8 19 FC FF FF</c> 同样是 -999。
        ///
        /// 【已删除的 INVENTED 门，勿重新加回】曾有一道 <c>RecycleBagModelResolved()</c>：
        /// 「无限背包 勾选了但不是 固定格子 就整体拒绝回收」。原生没有这道门。
        /// 入口 sub_1006CF10 全长 0x66 字节，只有一个 <c>call</c>（0x1006CF64 → 0x1006B020）
        /// 和一个门（0x1006CF16 <c>80 3D C5 B8 31 10 00 cmp byte [0x1031B8C5],0</c>）；
        /// 无限背包_是否勾选(0x102C2C7C) / 无限背包_是否固定(0x102BFAF0) / 固定格子(0x102BFB04) /
        /// V变量控制格子(0x102C44AC) / 无限背包_额外格子(0x102BFB10) / 无限背包_变量v1(0x102BFB24) /
        /// 无限背包_变量v2(0x102BFB34) 七个键的 VA 在 0x1006B020..0x1006CF80 内引用数 = 0
        /// （对照组：背包容量 sub_1007E370 引用得到，扫描不是瞎的）。
        /// 详见 tools/ys_recycle_re/v9_invented_scan.py 与 docs/ys_recycle_native_defects_20260813.md。
        /// </summary>
        public int AutoRecycle()
        {
            // 原生入口不查这个键，只查配置有效位；保留它是因为生产 config.json 里
            // 高级回收 = 1，该门在目标部署上零差异，而拆掉它等于对所有部署同时打开删除闸门。
            if (!Enabled("高级回收")) return RecycleUnusable;
            try
            {
                var recycleConfig = _pluginManager?.GetRecycleConfigSnapshot();
                if (recycleConfig == null) return RecycleUnusable;

                var totals = new RecycleRunTotals();
                for (int i = _player.m_ItemList.Count - 1; i >= 0; i--)
                {
                    var item = _player.m_ItemList[i];
                    if (item == null) continue;
                    var itemName = M2Share.UserEngine.GetStdItemName(item.wIndex);
                    if (!recycleConfig.TryGetItemRule(itemName, out var rule, out var stackable))
                        continue;
                    if (!RecycleTypeOpen(rule, stackable)) continue;
                    if (!stackable && !RecycleQualityAllowed(item, rule)) continue;
                    RecycleOne(item, itemName, rule, stackable, ref totals);
                }

                // 空包也要走这一趟：0x1006B28E 0F 88 AE 1B 00 00 js 0x1006CE42 直接跳到
                // 结算段，四路各自的 jle 会把它们全部跳过。
                SettleRecycleTotals(in totals);
                return RecycleDone;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("[异常] AutoRecycle " + ex.Message);
                return RecycleUnusable;
            }
        }

        /// <summary>
        /// 总开关：GetV(v1,v2)==关闭值 时该类型停止回收；省略则失去开关效果。
        ///
        /// 生效还有两个原生前置条件，两条分支不一样。可叠材料在 0x1006B783：
        /// <code>
        /// 1006B783  83 7D 84 FF  cmp dword [ebp-0x7C], -1   ; 关闭值，缺省 -2
        /// 1006B787  7C 23        jl  0x1006B7AC             ; &lt; -1 ⇒ 整道门失效
        /// </code>
        /// 缺省值 -2 由每件重置 0x1006B2F4 <c>C7 45 84 FE FF FF FF</c> 写入，
        /// 所以配置里把 关闭值 写成 -2 或更小等同于关掉这道门。
        /// 物品种类在 0x1006C0BE 多两条：0x1006C0CB / 0x1006C0D4 两个 jle 要求
        /// v1 和 v2 都为正，否则同样失效。
        /// </summary>
        private bool RecycleTypeOpen(RecycleRule rule, bool stackable)
        {
            if (!rule.HasMasterSwitch) return true;
            if (rule.MasterSwitchClosedValue < -1) return true;
            if (!stackable &&
                (rule.MasterSwitchGroup <= 0 || rule.MasterSwitchIndex <= 0)) return true;
            return ReadPlayerV(rule.MasterSwitchGroup, rule.MasterSwitchIndex)
                   != rule.MasterSwitchClosedValue;
        }

        /// <summary>
        /// 极品开关 / 元素开关。两道门的判定在插件里逐字节同构，首段分别是 0x1006C2E4
        /// 与 0x1006C699：
        /// <code>
        /// 1006C2EA  85 C0              test eax, eax        ; eax = GetV(组号, 槽)
        /// 1006C2EC  7E 14              jle  放行            ; 阈值 &lt;= 0 ⇒ 该槽不过滤
        /// 1006C2EE  3B 85 50 FF FF FF  cmp  eax, 物品值
        /// 1006C2F4  7F 0C              jg   放行            ; 阈值 &gt; 物品值 ⇒ 放行
        /// 1006C2F6  C7 45 9C 64 …      mov  拦下标志, 0x64
        /// </code>
        /// 即拦下的充要条件是 <c>0 &lt; 阈值 ≤ 物品值</c>。GetV 未命中返回的 -1 落在
        /// "阈值 ≤ 0" 这一侧，所以是不过滤，不是全挡 —— 生产 V(126,*) 从没写过，
        /// 按全挡实现会让回收整体静默失效。
        /// </summary>
        private bool RecycleQualityAllowed(TUserItem item, RecycleRule rule)
        {
            if (rule.ExtremeGroup > 0)
                for (var slot = 1; slot <= RecycleExtremeSlots; slot++)
                {
                    var threshold = ReadPlayerV(rule.ExtremeGroup, slot);
                    if (threshold > 0 && threshold <= GetExtremeValue(item, slot - 1))
                        return false;
                }

            if (rule.ElementGroup > 0)
                for (var slot = 1; slot <= RecycleElementSlots; slot++)
                {
                    var threshold = ReadPlayerV(rule.ElementGroup, slot);
                    if (threshold > 0 && threshold <= GetElementValue(item, slot))
                        return false;
                }

            return true;
        }

        /// <summary>
        /// 一次 AutoRecycle 调用内跨物品存活的四个货币累加器。
        ///
        /// 原生是 sub_1006B020 的四个栈槽，**只在进入循环之前清零一次**
        /// （0x1006B24D 起 <c>xor eax,eax</c> 之后连续 <c>mov …,eax</c>：
        /// 0x1006B258 <c>[ebp-0xA0]</c> 元宝、0x1006B25E <c>[ebp-0xA4]</c> 灵符、
        /// 0x1006B264 <c>[ebp-0xA8]</c> 金币、0x1006B26A <c>[ebp-0xAC]</c> 经验），
        /// 循环头在 0x1006B285 <c>dec edx</c>、回边在 0x1006CE3D <c>jmp 0x1006B285</c>，
        /// 每件的重置块 0x1006B294..0x1006B30D 里没有这四个槽。
        /// </summary>
        private struct RecycleRunTotals
        {
            public int Yuanbao;
            public int LingFu;
            public int Gold;
            public int Exp;
        }

        /// <summary>
        /// 循环结束后的一次性落账，0x1006CE42..0x1006CEBD。
        ///
        /// 【原生缺陷，照抄；勿"修"】REPLICATION_RULES §3.1。四路各自 <c>&gt; 0</c> 才调、
        /// 顺序固定 元宝 → 灵符 → 金币 → 经验、全部**不看返回值**。
        /// 于是金币累计顶到 <c>m_nGoldMax</c> 时 <c>IncGold</c> 会**整笔拒绝**，
        /// 而这一轮匹配到的物品早在循环里就删光了 ⇒ **整批只删不给**。
        /// 这正是要复刻的行为，不要加上限预检、不要分批付。
        ///
        /// 槽 → 引擎函数的映射由插件自己的运行期解析块钉死（<c>add edx,imm32</c> 后
        /// 紧跟 <c>mov [槽],edx</c>，每个槽全转储各只有一个写入点）：
        /// 0x1006B09A→<c>[0x1031BC64]=0x6F8730</c>（元宝）、
        /// 0x1006B0B8→<c>[0x1031BC60]=0x6DE7BC</c>（灵符）、
        /// 0x1006B0DC→<c>[0x1031BC5C]=0x6D791C</c>（IncGold）、
        /// 0x1006B136→<c>[0x1031BC50]=0x6C87B4</c>（经验）。
        /// </summary>
        private void SettleRecycleTotals(in RecycleRunTotals totals)
        {
            // 1006CE42  83 BD 60 FF FF FF 00  cmp  [ebp-0xA0],0
            // 1006CE49  7E 11                 jle  0x1006CE5C
            // 1006CE4B  8B 45 A0 / 33 C9      mov  eax,self / xor ecx,ecx
            // 1006CE50  8B 95 60 FF FF FF     mov  edx,[ebp-0xA0]
            // 1006CE56  FF 15 64 BC 31 10     call [0x1031BC64] = 0x6F8730  ; 元宝
            if (totals.Yuanbao > 0) CreditRecycleYuanbao(totals.Yuanbao);

            // 1006CE65  8B 45 A0              mov  eax,self
            // 1006CE68  33 D2                 xor  edx,edx               ; reason = 0
            // 1006CE6A  8B 8D 5C FF FF FF     mov  ecx,[ebp-0xA4]
            // 1006CE70  FF 15 60 BC 31 10     call [0x1031BC60]          ; 灵符
            if (totals.LingFu > 0)
                _player.AddNativeLingFu(RecycleLingFuReason, totals.LingFu);

            // 1006CE7F  8B 45 A0 / 33 C9      mov  eax,self / xor ecx,ecx
            // 1006CE84  8B 95 58 FF FF FF     mov  edx,[ebp-0xA8]
            // 1006CE8A  FF 15 5C BC 31 10     call [0x1031BC5C]          ; IncGold，返回值丢弃
            if (totals.Gold > 0) _player.IncGold(totals.Gold);

            // 1006CE99  8B 35 FC 0C 31 10     mov  esi,[0x10310CFC]      ; → "经验"
            // 1006CE9F..1006CEAE            push 1 / 总额 / 0 / 1 / 0 / 0
            // 1006CEB0  B1 01                 mov  cl,1
            // 1006CEB7  FF 15 50 BC 31 10     call [0x1031BC50]          ; 经验
            if (totals.Exp > 0) _player.GainExp(totals.Exp);
        }

        /// <summary>
        /// 元宝落账。原生 <c>0x6F8730</c> 不是就地改字段，而是拼一条请求丢进异步链：
        /// <code>
        /// 006F8749  8B F2                 mov  esi,edx               ; 金额
        /// 006F874B  8B D8                 mov  ebx,eax               ; Self
        /// 006F8777  8D 93 06 01 00 00     lea  edx,[ebx+0x106]       ; 角色名
        /// 006F87D7  A1 B0 68 7D 00        mov  eax,[0x7D68B0]        ; 全局服务单例
        /// 006F87E0  E8 C3 95 01 00        call 0x711DA8
        /// 006F881B  8B CE                 mov  ecx,esi
        /// 006F881D  E8 5A 8F 01 00        call 0x71177C
        /// </code>
        /// <c>sub_711DA8</c> 在本仓已定性为「外部/异步的元宝通道，不是进程内改值」
        /// （见 NativeStallBuyExecutor.cs 的同址判例），所以 C# 侧的等价物就是
        /// <c>NativeYuanbaoManager</c> 的入队。
        ///
        /// 关键在于**不要等它的结果**：0x1006CE56 之后没有 test/cmp，插件把返回值丢掉，
        /// 后面照样接着结算灵符/金币/经验。此前 C# 因为「结算成败要等回调」而把
        /// 会产元宝的物品整件拒收（D4），那是 C# 自己加的门，原生没有。
        ///
        /// 仍未解开的部分（不影响本条）：0x6F8730 拼的那两条 GBK 字面量
        /// （0x6F8854 / 0x6F885C）与它写进日志的确切文案。它在 M2Server 里 rel32 调用者
        /// 为 0，只有插件硬编码调用它。
        /// </summary>
        private void CreditRecycleYuanbao(int amount)
        {
            _player.ScriptRequestNativeYuanbao(amount,
                GameSvr.Services.NativeYuanbaoManager.AddOperation);
        }

        /// <summary>
        /// 结算一件物品。
        ///
        /// 【原生缺陷，照抄；勿"修"】REPLICATION_RULES §3.1。
        /// 顺序是**先删后结算，且没有任何回滚**。删除段 0x1006BB5D..0x1006BBD2 整段跑完，
        /// 0x1006BBD8 起才是四路累加，0x1006BCB7 起才是 其他 的 SetV：
        /// <code>
        /// 1006BB68  8B 75 8C              mov  esi,[ebp-0x74]        ; item
        /// 1006BB6B  8B 45 80 / 8B 40 04   mov  eax,[TList] / [eax+4] ; FList
        /// 1006BB71  03 85 D8 FE FF FF     add  eax, idx*4
        /// 1006BB77  8B 00                 mov  eax,[eax]
        /// 1006BB7F  3B C6                 cmp  eax,esi
        /// 1006BB81  75 45                 jne  0x1006BBC8            ; 指针变了 ⇒ 不删
        /// 1006BB89  83 F8 01 / 7F 0F      cmp  FCount,1 / jg
        /// 1006BB95  FF 15 0C 0D 31 10     call [0x10310D0C]=0x6C1ED8 ; FCount<=1 清空整包
        /// 1006BBA6  FF 15 68 BC 31 10     call [0x1031BC68]          ; TList.Delete(idx)
        /// 1006BBB9  3E FF 97 68 02 00 00  call [player VMT+0x268]    ; 下发删除
        /// 1006BBC2  FF 15 4C BC 31 10     call [0x1031BC4C]          ; Dispose(item)
        /// 1006BBCF  3B 45 8C / 0F 85 …    cmp / jne 0x1006BD2D       ; 没删成 ⇒ 也不结算
        /// 1006BBD8  …                                               ; 四路累加从这里开始
        /// </code>
        /// 唯一能让本件不结算的条件是「删除没发生」；反过来「结算失败」**不会**让物品回来。
        /// 而且这里连「落账」都还没发生：四路货币只进累加器，真正付钱在整个循环跑完之后
        /// 的 <see cref="SettleRecycleTotals"/>（0x1006CE42..0x1006CEBD），全都不看返回值。
        /// IncGold 顶到 <c>[eax+0x68C]</c> 时 <c>0x6D7934 7F 0D jg</c> 整笔返回 FALSE，
        /// 而这一轮的物品早已删光 —— 这就是「整批只删不给」。
        ///
        /// 所以删除之后不允许再有任何 return：算术必须像原生一样静默截断，不能中途放弃。
        /// </summary>
        private void RecycleOne(TUserItem item, string itemName, RecycleRule rule,
            bool stackable, ref RecycleRunTotals totals)
        {
            // 倍率：GetV=200 表示 2 倍 ⇒ 单价*GetV/100，先乘后除；小于等于 0 表示无效，按 1 倍。
            var rate = rule.HasRate ? ReadPlayerV(rule.RateGroup, rule.RateIndex) : 0;

            // 可叠材料整堆结算，件数取 word[item+0x26]（= Dura，本仓另有 0x63F454 商人基础价
            // 与 0x740914 背包计数两处同址判例）。0x1006BB2F 66 8B 58 26 读它，
            // 0x1006BC07 / 0x1006BC44 / 0x1006BC76 / 0x1006BCAE / 0x1006BCD6 五路各乘一次。
            // 物品种类分支从 0x1006CD03 起整段没有这个乘法，件数恒为 1。
            // 原生这里是整件不做类型判断的：谁被写进 可叠材料，就按它的 Dura 乘。
            var count = stackable ? (int)item.Dura : 1;
            var otherUnit = rule.HasOther ? rule.OtherValue : 0;

            // 至少一路产出为正才允许删除。
            //
            // 【原生缺陷，照抄；勿"修"】判的是**缩放前、未乘件数**的五个单价，不是实付金额。
            // 可叠材料分支 0x1006BB3B..0x1006BB57：
            //   1006BB3B  85 FF                 test edi,edi              ; 元宝单价 [ebp-0x70]
            //   1006BB3D  7F 1E                 jg   0x1006BB5D
            //   1006BB3F  83 BD 70 FF FF FF 00  cmp  [ebp-0x90],0         ; 灵符单价
            //   1006BB46  7F 15                 jg   0x1006BB5D
            //   1006BB48  83 BD 6C FF FF FF 00  cmp  [ebp-0x94],0         ; 金币单价
            //   1006BB4F  7F 0C                 jg   0x1006BB5D
            //   1006BB51  85 C9                 test ecx,ecx              ; 其他值 [ebp-0x68]
            //   1006BB53  7F 08                 jg   0x1006BB5D
            //   1006BB55  85 F6                 test esi,esi              ; 经验单价 [ebp-0x78]
            //   1006BB57  0F 8E D0 01 00 00     jle  0x1006BD2D           ; 全 <=0 ⇒ 本件结束
            // 物品种类分支 0x1006CC68..0x1006CC82 逐字节同构（jle 0x1006CE1F）。
            // 第一条 imul 倍率在 0x1006BBEE、第一条 imul 件数在 0x1006BC07，
            // 都在删除段 0x1006BB5D 之后 —— 判零时这五个值一次缩放都没做过。
            //
            // 后果：单价=1 且倍率=50 会过门，却只入账 ⌊1*50/100⌋=0 ⇒ 删了不给。
            if (rule.Yuanbao <= 0 && rule.LingFu <= 0 && rule.Gold <= 0 &&
                otherUnit <= 0 && rule.Exp <= 0)
                return;

            // ── 删除段。此行之后不允许再出现任何 return，见方法头。 ──
            if (!_player.DelBagItem(item.MakeIndex, itemName)) return;

            // 四路货币只累加，不在这里落账 —— 落账是循环结束后的一次性动作
            // （SettleRecycleTotals / 0x1006CE42..0x1006CEBD）。
            // 累加次序照 0x1006BBD8（元宝）→ 0x1006BC1B（灵符）→ 0x1006BC4D（金币）
            // → 0x1006BC7F（经验），四条 add 分别是
            // 0x1006BC0A / 0x1006BC47 / 0x1006BC79 / 0x1006BCB1，全是 32 位回绕的 add。
            totals.Yuanbao = unchecked(
                totals.Yuanbao + ScaleRecyclePrice(rule.Yuanbao, rate, count));
            totals.LingFu = unchecked(
                totals.LingFu + ScaleRecyclePrice(rule.LingFu, rate, count));
            totals.Gold = unchecked(
                totals.Gold + ScaleRecyclePrice(rule.Gold, rate, count));
            totals.Exp = unchecked(
                totals.Exp + ScaleRecyclePrice(rule.Exp, rate, count));

            var other = ScaleRecyclePrice(otherUnit, rate, count);

            // 其他 走 0x1006BCB7（可叠材料）/ 0x1006CDB4（物品种类）两段同构代码：
            //   1006BCB7  8B 45 98   mov eax,[ebp-0x68]   ; 其他值，**缩放前**
            //   1006BCBA  85 C0      test eax,eax
            //   1006BCBC  7E 6F      jle 0x1006BD2D       ; 缩放前 <=0 才整段不写 SetV
            //   1006BCBE  85 FF      test edi,edi         ; 倍率，第一条 imul 在 0x1006BCC2
            //   1006BCF4  FF 15 58 BC 31 10  call GetV
            //   1006BCFD  7D 02 / 1006BCFF 33 C0          ; 累加基数的负值钳到 0
            //   1006BD26  FF 15 54 BC 31 10  call SetV    ; 不看返回值
            // 同 D3：闸门读的是缩放前的值。缩放后为 0 时原生照样写一次 SetV，
            // 于是 GetV 原本是 -1 的槽会被钳零后写成 0 —— 这不是空操作，别"优化"掉。
            // （此处原注释写的「缩放后 <= 0」与字节矛盾，已按字节订正。）
            // 组/下标非法时 SetV 自己用 0x6DF2B3 / 0x6DF2B7 两个 jle 静默丢弃，
            // 物品照删 —— 这一路的守卫在 WritePlayerV 里，不在这里，别提前 return。
            if (otherUnit > 0)
            {
                var accumulated = unchecked(Math.Max(0,
                    ReadStoredPlayerV(rule.OtherGroup, rule.OtherIndex)) + other);
                WritePlayerV(rule.OtherGroup, rule.OtherIndex, accumulated);
            }
        }

        /// <summary>
        /// 单价 → 实付。先按倍率缩放再乘件数，与 0x1006BBE9（缩放）后接 0x1006BC07（乘件数）
        /// 的次序一致。
        ///
        /// 【原生缺陷，照抄；勿"修"】两步都是 **32 位** imul，溢出静默截断，不会中止本件：
        /// <c>0x1006BBEB 0F AF CB imul ecx,ebx</c>（倍率×单价）→
        /// <c>0x1006BBEE B8 1F 85 EB 51 / F7 E9 / C1 FA 05 / C1 EB 1F / 03 DA</c>
        /// （0x51EB851F 魔数除 100，向零截断）→ <c>0x1006BC07 0F AF C3 imul eax,ebx</c>
        /// （×件数）→ <c>0x1006BC0A 01 85 60 FF FF FF add [ebp-0xA0],eax</c>（32 位累加）。
        /// 此前 C# 用 64 位算并在越界时整件放弃 —— 那是删除段之后的一条 return，
        /// 与「先删后结算、无回滚」直接冲突，所以必须回到 32 位回绕。
        /// </summary>
        private static int ScaleRecyclePrice(int unitPrice, int rate, int count)
        {
            if (unitPrice <= 0) return 0;
            var scaled = rate > 0 ? unchecked(rate * unitPrice) / 100 : unitPrice;
            return unchecked(scaled * count);
        }

        /// <summary>
        /// GetV 语义，与 PasApiBridge.GetPlayerVar 同源：0x6DF203 test esi,esi 判组号，
        /// 组 0 走 0x6DF20F mov eax,[ebx+eax*4+0x808] 的内联槽（0x6DF20A sub edx,0x64 +
        /// 0x6DF20D jae ⇒ 只收 1..100，没写过的槽读 0），其余走键控字典，
        /// 未命中保留 0x6DF1F1 预置的 -1。
        /// </summary>
        private int ReadPlayerV(int group, int index)
        {
            var player = _player;
            if (player == null) return NativeScriptVarMiss;
            // Flat group*1000+index into m_ScriptVVars silently reads 0 for
            // group 0: those slots live in m_ScriptVGroup0, not the dictionary.
            if (!player.TryGetScriptVar('V', group, index, out var value))
                return NativeScriptVarMiss;
            return value;
        }

        /// <summary>
        /// 其他 是个累加器（生产 NPC 装备回收-3.pas 把 GetV(10,200) 加进 MyShengwan 再清零），
        /// 累加基数要读真实存储：GetV 把"从没写过"和"存的就是 -1"都折叠成 -1，拿 -1 当基数会
        /// 让第一件物品少给一点，甚至把 值=0 的类型写成 -1 让 NPC 反扣一点声望。
        /// </summary>
        private int ReadStoredPlayerV(int group, int index)
        {
            var player = _player;
            if (player == null) return 0;
            return player.TryGetScriptVar('V', group, index, out var value) ? value : 0;
        }

        /// <summary>SetV 的收参门：0x6DF2B3/0x6DF2B7 两个 test 拒掉非正参数，组 0 只收 1..100。</summary>
        private static bool PlayerVarWritable(int group, int index) =>
            group == 0 ? index >= 1 && index <= 100 : group > 0 && index > 0;

        private void WritePlayerV(int group, int index, int value)
        {
            var player = _player;
            if (player == null || !PlayerVarWritable(group, index)) return;
            // SetScriptVar stores 0 as 0 (sub_6E4140 has no zero test).
            player.SetScriptVar('V', group, index, value);
        }

        /// <summary>全屏拾取: round范围, gbv网关绕过值, isMy仅拾取自己的</summary>
        public int AutoPickup(int range, int v1, int gbv, int isMy)
        {
            if (!EnabledAll("眼神特殊函数", "全屏拾取")) return 0;
            var envir = _player.m_PEnvir; if (envir == null) return 0;
            int picked = 0;
            // Scan all map cells within range for items
            for (int dx = -range; dx <= range; dx++)
            {
                for (int dy = -range; dy <= range; dy++)
                {
                    int cx = _player.m_nCurrX + dx;
                    int cy = _player.m_nCurrY + dy;
                    var mapItem = envir.GetItem(cx, cy);
                    if (mapItem == null) continue;
                    // isMy check: skip items that belong to others
                    if (isMy != 0 && mapItem.OfBaseObject != null && mapItem.OfBaseObject != _player) continue;
                    // Pick up gold
                    if (mapItem.Name == Grobal2.sSTRING_GOLDNAME)
                    {
                        if (envir.DeleteFromMap(cx, cy, CellType.OS_ITEMOBJECT, mapItem) == 1)
                        {
                            if (_player.IncGold(mapItem.Count)) { _player.GoldChanged(); picked++; }
                            else envir.AddToMap(cx, cy, CellType.OS_ITEMOBJECT, mapItem);
                        }
                        continue;
                    }
                    // Pick up items
                    if (_player.IsEnoughBag() && mapItem.UserItem != null)
                    {
                        var userItem = mapItem.UserItem;
                        var stdItem = M2Share.UserEngine.GetStdItem(userItem.wIndex);
                        if (stdItem != null && _player.IsAddWeightAvailable(M2Share.UserEngine.GetStdItemWeight(userItem.wIndex)))
                        {
                            if (envir.DeleteFromMap(cx, cy, CellType.OS_ITEMOBJECT, mapItem) == 1)
                            {
                                _player.AddItemToBag(userItem);
                                _player.SendAddItem(userItem);
                                picked++;
                            }
                        }
                    }
                }
            }
            return picked;
        }

        /// <summary>获取背包负重: flag 0=当前 1=最大</summary>
        public int GetBagWeight(int flag)
        {
            if (!Enabled("眼神特殊函数")) return 0;
            return flag == 0 ? _player.m_WAbil.Weight : _player.m_WAbil.MaxWeight;
        }

        /// <summary>
        /// Ys_GiveBind / 数字隧道 33 —— flag==0 绑定，flag!=0 解绑。
        /// 处理函数 0x10076060，字段顺序 (itemid, flag) = fields[2..3]。
        ///
        /// 寻址：0x10076110 call 0x10075AE0，那个查找器只遍历 [player+0x508]（= m_ItemList），
        /// 逐项和入参比较后原样返回命中的对象；身上装备和英雄容器都不在范围内。
        /// 入参在原生是「服务端物品 id」，即 TUserItem 对象指针本身（caret 20
        /// 的 Ys_GetClientItemIDByItemid 在 0x1005AE06 用同样的指针相等判定，
        /// 命中后返回 [item+0x18] = ClientItemID）。C# 没有指针，本工程一贯用
        /// MakeIndex 代表「服务端物品 id」，这里沿用同一约定。
        ///
        /// 绑定位是 item+0x34：10076124 C6403401 / 1007613A C6403400。换算到
        /// TUserItem 就是 btValue[10]（记录偏移 0x14），与本仓库其余地方
        /// （NativeStallItemMove、MailService、TryGetNativePileCompatibility）
        /// 用的 btValue[10..11] 绑定/锁定字是同一个字节。原生只写低字节，
        /// btValue[11] 保持不动。
        ///
        /// 全函数没有任何发包调用，返回值恒为 1（10076129 / 1007613F BE01000000），
        /// 找不到物品也是 1。
        /// </summary>
        public int BindUnbindItem(int itemId, int flag)
        {
            if (!Enabled("屏蔽自动绑定")) return 0;
            TUserItem found = null;
            foreach (var item in _player.m_ItemList)
            {
                if (item != null && item.MakeIndex == itemId) { found = item; break; }
            }
            if (found != null)
            {
                EnsureBindByteSlot(found);
                found.btValue[10] = flag == 0 ? (byte)1 : (byte)0;
            }
            return 1;
        }

        /// <summary>
        /// ys_DropItem / 数字隧道 7 —— 在角色周围地面上**新产生** count 件 itemName。
        /// 官方文档原话：「此函数是在角色周围地面上新产生物品，和角色背包有不有物品
        /// 毫无关系」（AllFuc使用例子.pas:309）。原来的实现方向正好反了：它用
        /// DelBagItem 把玩家背包里的同名物品删掉。
        ///
        /// 处理函数 0x10070D20，字段顺序 (num, range, name) = fields[2..4]；它把
        /// (self, name, range, num) 交给宿主 0x0064E6F4（10070D98 mov [ebp-0x2C],0x64E6F4
        /// / 10070E0A call [ebp-0x2C]），返回值是原样回传的 num（10070DB7 mov [ebp-0x1C],eax，
        /// 之后再没被改过）。
        ///
        /// 宿主 0x0064E6F4 的阶梯：
        ///   0064E71E 83FB01 cmp ebx,1 / 7D05 jge / BB01000000  —— num &lt; 1 提到 1
        ///   0064E72B mov edx,0x64E7E0 / call 0x0040591C / 753D jne —— 名字等于「金币」
        ///       （0x64E7DC 处的长度前缀是 4，正好两个汉字）走金币分支，每次最多 2000
        ///   否则每轮 0064E784 call 0x0074DE54 造一件新物品，造不出来就跳出循环，
        ///       造出来就 0064E79D call 0x007688A0 扔地上，扔失败 0064E7A8 call 0x00404690 释放
        /// </summary>
        public int DropItem(int count, int range, string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return count;
            var n = count < 1 ? 1 : count;

            if (string.Equals(itemName, "金币", StringComparison.Ordinal))
            {
                // 0064E737..0064E770 的三段判定原样转写：先切 2000，再补尾数。
                do
                {
                    if (n > 2000) { _player.YanshenTunnelDropGold(2000); n -= 2000; }
                    if (n <= 2000) _player.YanshenTunnelDropGold(n);
                } while (n > 2000);
                return count;
            }

            for (var i = 0; i < n; i++)
            {
                var userItem = new TUserItem();
                if (!M2Share.UserEngine.CopyToUserItemFromName(itemName, ref userItem)) break;
                // 宿主 0x007688A0 的第 3 个寄存器参就是 range；第 6 个栈参（[ebp+0xC]）
                // 是 self，对应 C# 的 DropCreat。另外两个字节旗标（[ebp+0x14]=1 跳过
                // 0x0078389C 的可丢弃校验、[ebp+0x10]=0）在 C# 的 DropItemDown 里没有
                // 对应形参，本工程的模型只有 (boDieDrop, ItemOfCreat, DropCreat)。
                _player.DropItemDown(userItem, range, false, null, _player);
            }
            return count;
        }

        /// <summary>
        /// Ys_DropItembyId / caret ^33^ —— 把身体部位 pos 上的装备摘下来扔到地上。
        /// 处理函数 0x1005CDD0。原来的实现调 DelBagItem 去背包里找 MakeIndex，而
        /// 已装备的物品根本不在 m_ItemList 里，所以它既没摘装备也没掉东西，却照样返回 1。
        /// </summary>
        public int DropEquipByPos(int pos)
        {
            // 1005CE27 83F80F cmp eax,0xF / 1005CE2A 0F8762010000 ja
            //   -> 1005CF92 分支，1005CFA1 33C0 xor eax,eax：整段不执行，返回 0
            if ((uint)pos > 0xF) return 0;
            // 两条隧道唯一的实质差别在第 6 个栈参：caret 33 推常量 0（1005CED0 6A00），
            // caret 34 推玩家自己（1005D145 FF75B4）。
            DropEquippedSlot(pos, null);
            // 1005CF8D 8B45C8 mov eax,[ebp-0x38] —— 返回的是装备位下标本身，不是 1；
            // 槽位为空也走这条，仍旧返回下标。
            return pos;
        }

        /// <summary>
        /// Ys_DropItembyName / caret ^34^ —— 按装备名字定位身体部位再扔。
        /// 处理函数 0x1005CFF0。
        /// </summary>
        public int DropEquipByName(string name)
        {
            var slot = -1;
            // 1005D04E 83FE10 cmp esi,0x10 / 7D5A jge —— 只扫 0..15 号装备位，取第一个命中
            for (var i = 0; i < 16 && i < _player.m_UseItems.Length; i++)
            {
                var item = _player.m_UseItems[i];
                if (item == null || item.wIndex == 0) continue;
                // 1005D07F call 0x10056970 取 [item+0x1C] 那条 Delphi 串（物品自己的显示名，
                // 对应 C# 的 ItmUnit.GetItemName，而不是 std 名）；1005D08F call 0x10043E20
                // 转 0x10018E20 是逐 dword / 逐字节比较，**区分大小写**。
                if (!string.Equals(ItmUnit.GetItemName(item), name, StringComparison.Ordinal)) continue;
                slot = i;
                break;
            }
            // 1005D0AD 83FB0F cmp ebx,0xF / 0F8773010000 ja -> 1005D244 33C0：没找到返回 0
            if ((uint)slot > 0xF) return 0;
            DropEquippedSlot(slot, _player);
            // 1005D20F 8B45B0 —— 返回装备位下标，所以「扔掉 0 号位」和「没找到」都是 0
            return slot;
        }

        /// <summary>
        /// caret ^33^ / ^34^ 共用的落地序列。两段原生码（1005CEAB..1005CF40 与
        /// 1005D120..1005D1B6）逐条同构，caret 33 里被 Themida 虚拟化的只是三个宿主
        /// 地址的立即数，调用形状本身是明文的：
        ///   ① 宿主 0x0075F3E8(装备容器, 槽号, cl=0)：0075F409 取出物品、0075F40F 把槽
        ///      置 0。第三参传 0，所以宿主自己的 RecalcAbilitys（0x0075EE78）和外观刷新
        ///      都不执行。
        ///   ② 宿主 0x007688A0(self, item, ecx=3, 1, 0, dropper, 来源串)：范围恒为 3；
        ///      第一个栈参 1 让宿主跳过 0x0078389C 的可丢弃校验。
        ///   ③ 宿主 0x00765F6C，cx=0x27A4=RM_SENDDELITEMLIST，包体 4 字节 = [item+0x18]
        ///      = ClientItemID（1005D175 8B4718 / 1005D18C lea eax,[ebp-0x6C]）。
        ///   ④ 只有装备位落在 {0,1,4,13} 时再调玩家虚函数 [+0x1CC]。这个集合来自
        ///      1005CEE3 sub eax,2/jb → sub eax,2/je → sub eax,9/jne，与宿主自己的
        ///      0x0075F1D8 逐字节相同。
        /// 全程不碰 m_ItemList。
        /// </summary>
        private void DropEquippedSlot(int slot, TBaseObject dropper)
        {
            if (slot < 0 || slot >= _player.m_UseItems.Length) return;
            var item = _player.m_UseItems[slot];
            if (item == null || item.wIndex == 0) return;      // 1005CEBE 85FF / 747E je

            var deleted = new List<TDeleteItem>
            {
                new TDeleteItem
                {
                    sItemName = M2Share.UserEngine.GetStdItemName(item.wIndex),
                    MakeIndex = item.MakeIndex,
                    ClientItemID = _player.EnsureClientItemId(item)
                }
            };

            // 原生先清槽再扔（它握的是对象指针）；C# 的 DropItemDown 要靠 wIndex 反查
            // StdItem，所以只能先扔后清槽。清的是槽位引用而不是物品的 wIndex——
            // 物品对象已经挂在地图上了，改它的 wIndex 会把地面物品一并弄坏。
            _player.DropItemDown(item, 3, false, null, dropper);
            _player.m_UseItems[slot] = null;
            _player.SendMsg(_player, Grobal2.RM_SENDDELITEMLIST, 0,
                deleted.Count, 0, 0, "", deleted);
            // [player]+0x1CC 不是 RecalcAbilitys（那是 +0x8C，本路径因为第三参传 0
            // 而根本不走）。C# 侧对应的是 FeatureChanged —— 装备外观广播。
            if (slot is 0 or 1 or 4 or 13) _player.FeatureChanged();
        }

        /// <summary>按stdmode修理背包物品: Dura=DuraMax</summary>
        public int RepairBagByStdMode(int stdMode, int isHero)
        {
            int count = 0;
            foreach (var item in _player.m_ItemList)
            {
                if (item == null) continue;
                var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
                if (stdItem != null && stdItem.StdMode == stdMode)
                {
                    if (item.Dura < item.DuraMax) { item.Dura = item.DuraMax; _player.SendUpdateItem(item); count++; }
                }
            }
            return count;
        }

        /// <summary>物品ID互转: ClientItemID → ItemID</summary>
        public int GetItemIdByClientId(int clientItemId)
        {
            return FindOwnedItemByClientId(clientItemId)?.MakeIndex ?? 0;
        }

        /// <summary>物品ID互转: ItemID → ClientItemID</summary>
        public int GetClientItemIdByItemId(int itemId)
        {
            var item = FindOwnedItemByItemId(itemId);
            return item == null ? 0 : _player.EnsureClientItemId(item);
        }

        /// <summary>修改装备描述/来源</summary>
        public int ModifyItemDesc(int clientItemId, string pname, string desc1, string desc2)
        {
            if (!Enabled("装备来源")) return clientItemId;
            var found = FindOwnedItemByClientId(clientItemId);
            if (found == null) return clientItemId;
            found.pname = pname ?? string.Empty;
            found.desc1 = desc1 ?? string.Empty;
            found.desc2 = desc2 ?? string.Empty;
            RefreshOwnedItem(found);
            return clientItemId;
        }

        /// <summary>更新身体装备数据到客户端 (发送装备刷新包)</summary>
        public int UpdateBodyEquip(int playerId)
        {
            if (!Enabled("自定义元素")) return 0;
            // 遍历所有已装备物品并逐个发送刷新包给客户端
            for (int i = 0; i < _player.m_UseItems.Length; i++)
            {
                var item = _player.m_UseItems[i];
                if (item != null)
                    _player.SendUpdateItem(item);
            }
            return 1;
        }

        /// <summary>更换大背包: name旧背包名, newName新背包名</summary>
        public int ChangeBigBag(string name, string newName)
        {
            // Switch bag item — find old bag, delete it, give new one
            var userItem = _player.CheckItems(name);
            if (userItem != null)
            {
                _player.DelBagItem(userItem.MakeIndex, M2Share.UserEngine.GetStdItemName(userItem.wIndex));
                if (_npc != null) _npc.GotoLable_GiveItem(_player, newName, 1);
                return 1;
            }
            return 0;
        }

        /// <summary>检查物品是否绑定: 参数为MakeIndex的字符串表示</summary>
        public bool CheckItemBind(string makeIndex)
        {
            if (!int.TryParse(makeIndex, out int idx)) return false;
            foreach (var item in _player.m_ItemList)
                if (item != null && item.MakeIndex == idx) return item.Bind != 0;
            return false;
        }

        // ═══════════════════════════════════════════════════════════════
        // 6.7 物品数据操作 (GetSignInActPrizer隧道) — 5 functions
        // ═══════════════════════════════════════════════════════════════

        /// <summary>获取背包所有物品MakeIndex列表: isAll=true含绑定物品</summary>
        public string GetBagMakeIndexList(bool isAll)
        {
            var indices = new System.Text.StringBuilder();
            foreach (var item in _player.m_ItemList)
            {
                if (item != null && (isAll || item.Bind == 0))
                    indices.Append(item.MakeIndex).Append(',');
            }
            return indices.Length == 0 ? "NULL" : indices.ToString().TrimEnd(',');
        }

        /// <summary>通过MakeIndex获取物品完整数据。</summary>
        public string GetItemDataByMakeIndex(int makeIndex)
        {
            foreach (var item in _player.m_ItemList)
                if (item != null && item.MakeIndex == makeIndex)
                    return SerializeItemData(item);
            return "";
        }

        /// <summary>获取物品数据并回收该物品</summary>
        public string GetItemDataAndRecycle(int makeIndex)
        {
            var data = GetItemDataByMakeIndex(makeIndex);
            if (!string.IsNullOrEmpty(data))
                _player.DelBagItem(makeIndex, ""); // delete item by makeIndex
            return data;
        }

        /// <summary>通过ClientItemID获取物品数据</summary>
        public string GetItemDataByClientId(int clientItemId)
        {
            var item = FindOwnedItemByClientId(clientItemId);
            return item == null ? string.Empty : SerializeItemData(item);
        }

        /// <summary>通过服务端物品ID查询物品数据库字段。2.08 文档仅明确了 pid=0..7。</summary>
        public int GetItemDbData(int itemId, int pid)
        {
            return GetItemDbData(_player, itemId, pid);
        }

        public int GetItemDbData(TPlayObject player, int itemId, int pid)
        {
            var item = FindOwnedItemByItemId(player, itemId);
            if (item == null) return -1;

            var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
            if (stdItem == null) return -1;

            return pid switch
            {
                0 => stdItem.StdMode,
                1 => stdItem.Shape,
                2 => stdItem.Source,
                3 => stdItem.Outlook,
                4 => stdItem.Looks,
                5 => stdItem.Weight,
                6 => stdItem.DuraMax,
                7 => stdItem.NeedLevel,
                _ => -1
            };
        }

        /// <summary>2.08 高层对象接口：通过服务端物品ID返回物品对象。</summary>
        public TUserItem GetItemObject(int itemId)
        {
            return FindItemByItemId(itemId, out _);
        }

        /// <summary>2.08 高层对象接口：通过物品对象获取服务端物品ID。</summary>
        public int GetItemId(TUserItem item)
        {
            return item?.MakeIndex ?? 0;
        }

        public bool IsSafeZone(int roleId)
        {
            var target = roleId <= 0 || (_player != null && roleId == _player.ObjectId)
                ? _player
                : FindObjectById(roleId);
            return target != null && target.InSafeZone();
        }

        public int SetItemBindByItemId(int itemId, int bind)
        {
            var item = FindItemByItemId(itemId, out var owner);
            if (item == null) return 0;

            item.Bind = (byte)Math.Clamp(bind, 0, 1);
            EnsureBindByteSlot(item);
            item.btValue[8] = item.Bind == 0 ? (byte)0 : (byte)1;
            RefreshOwnedItem(owner, item);
            return item.Bind;
        }

        /// <summary>Ys_GetOther — 通过服务端物品id获取或设置装备的极品或元素值。
        /// types=0 操作极品(id 1-6)，types=1 操作元素(id 1-17)。
        /// val&gt;=0 表示赋值并返回设置值；val&lt;0 表示读取并返回当前值。
        /// 物品未找到或 id/types 越界返回 -1（元素/极品值恒为非负，不与 -1 冲突）。</summary>
        public int GetOther(int itemId, int id, int val, int types)
        {
            var item = FindItemByItemId(itemId, out var owner);
            if (item == null) return -1;

            if (types == 0)
            {
                var index = id - 1; // 极品位 1-6 → 内部索引 0-5
                if (index < 0 || index > 5) return -1;
                if (val < 0) return GetExtremeValue(item, index);
                if (!SetExtremeValue(item, index, val)) return -1;
                RefreshOwnedItem(owner, item);
                return val;
            }
            if (types == 1)
            {
                if (id < 1 || id > 17) return -1; // 元素位 1-17
                if (val < 0) return GetElementValue(item, id);
                if (!SetElementValue(item, id, val)) return -1;
                RefreshOwnedItem(owner, item);
                return val;
            }
            return -1;
        }

        public int GetOnlinePlayerNum()
        {
            return M2Share.UserEngine?.OnlinePlayObject ?? 0;
        }

        public TUserItem GetBodyItem(TPlayObject player, int pos)
        {
            var owner = player ?? _player;
            if (owner == null || pos < 0 || pos >= owner.m_UseItems.Length) return null;

            var item = owner.m_UseItems[pos];
            return item != null && item.wIndex > 0 ? item : null;
        }

        public bool KillRole(int roleId)
        {
            return KillRoleSilently(FindObjectById(roleId));
        }

        public int ChangeRole(int roleId, int hp, int ac, int mac, int dc, int ys, int gs)
        {
            var target = FindObjectById(roleId);
            if (!IsMonsterOrSlave(target))
                return 0;

            var acValue = HUtil32.MakeLong(Math.Max(0, ac), Math.Max(0, ac));
            var macValue = HUtil32.MakeLong(Math.Max(0, mac), Math.Max(0, mac));
            var dcValue = HUtil32.MakeLong(Math.Max(0, dc), Math.Max(0, dc));
            var maxHp = Math.Max(0, hp);

            if (target.m_Abil != null)
            {
                target.m_Abil.AC = acValue;
                target.m_Abil.MAC = macValue;
                target.m_Abil.DC = dcValue;
                target.m_Abil.MaxHP = maxHp;
            }

            target.m_WAbil.AC = acValue;
            target.m_WAbil.MAC = macValue;
            target.m_WAbil.DC = dcValue;
            target.m_WAbil.MaxHP = maxHp;
            target.m_WAbil.HP = maxHp;
            target.m_nWalkSpeed = Math.Max(0, ys);
            target.m_nNextHitTime = Math.Max(0, gs);
            target.StatusChanged();
            return 1;
        }

        public int KillMon(string mapName, string monName)
        {
            var map = M2Share.MapManager.FindMap(mapName);
            if (map == null) return 0;

            var list = new List<TBaseObject>();
            M2Share.UserEngine.GetMapMonster(map, list);

            int killed = 0;
            foreach (var target in list)
            {
                if (!NameMatches(target, monName)) continue;
                if (KillRoleSilently(target)) killed++;
            }
            return killed;
        }

        public TPlayObject FindPlayerByName(string humanName)
        {
            if (string.IsNullOrWhiteSpace(humanName)) return null;
            return M2Share.UserEngine.GetPlayObjectEx(humanName)
                ?? M2Share.UserEngine.GetPlayObject(humanName);
        }

        public int Say(int roleId, string say)
        {
            var target = roleId <= 0 || (_player != null && roleId == _player.ObjectId)
                ? _player
                : FindObjectById(roleId);
            if (target == null || string.IsNullOrEmpty(say)) return 0;

            target.SendRefMsg(Grobal2.RM_HEAR, 0, M2Share.g_Config.btHearMsgFColor,
                M2Share.g_Config.btHearMsgBColor, 0, GetSpeakerName(target) + ':' + say);
            return 1;
        }

        public int WildCharge(TPlayObject player, int x, int y, int roleId)
        {
            var actor = player ?? _player;
            if (actor == null || actor.m_PEnvir == null) return 0;

            var target = FindObjectById(roleId);
            var mapName = target?.m_sMapName ?? actor.m_sMapName;
            var targetX = target?.m_nCurrX ?? (short)x;
            var targetY = target?.m_nCurrY ?? (short)y;

            actor.SendRefMsg(Grobal2.RM_SPACEMOVE_FIRE, 0, 0, 0, 0, "");
            actor.SpaceMove(mapName, targetX, targetY, 0);
            return 1;
        }

        public int CreateMon(string mapName, int x, int y, int ranger, string monName,
            int num, int hp, int ac, int mac, int dc, int ys, int gs)
        {
            var map = M2Share.MapManager.FindMap(mapName);
            if (map == null || num <= 0) return 0;

            int created = 0;
            for (var i = 0; i < num; i++)
            {
                var spawnX = x;
                var spawnY = y;
                if (ranger > 0)
                {
                    spawnX += M2Share.RandomNumber.Random(ranger * 2 + 1) - ranger;
                    spawnY += M2Share.RandomNumber.Random(ranger * 2 + 1) - ranger;
                }

                var mon = M2Share.UserEngine.RegenMonsterByName(mapName, (short)spawnX, (short)spawnY, monName);
                if (mon == null) continue;

                var acValue = HUtil32.MakeLong(Math.Max(0, ac), Math.Max(0, ac));
                var macValue = HUtil32.MakeLong(Math.Max(0, mac), Math.Max(0, mac));
                var dcValue = HUtil32.MakeLong(Math.Max(0, dc), Math.Max(0, dc));
                var maxHp = Math.Max(0, hp);

                mon.m_Abil.AC = acValue;
                mon.m_Abil.MAC = macValue;
                mon.m_Abil.DC = dcValue;
                mon.m_Abil.MaxHP = maxHp;
                mon.m_WAbil.AC = acValue;
                mon.m_WAbil.MAC = macValue;
                mon.m_WAbil.DC = dcValue;
                mon.m_WAbil.MaxHP = maxHp;
                mon.m_WAbil.HP = maxHp;
                mon.m_nWalkSpeed = Math.Max(0, ys);
                mon.m_nNextHitTime = Math.Max(0, gs);
                mon.StatusChanged();
                created++;
            }
            return created;
        }

        public const string NpcCreatMonsSentinel = "yanshen2.0.7";
        public const int NpcCreatMonsFieldCount = 19;

        /// <summary>
        /// NpcFuc.pas serializes
        /// <c>0^x^y^num^round^Ac^Mac^Dc^DcMax^Mc^Sc^Speed^Hit^hp^Maxhp^AttackSpd^WalkSpd^MonName^Map</c>
        /// and smuggles it through <c>CheckMapMonByName('yanshen2.0.7', res)</c>.
        /// Attribute writes follow plugin JSON apply at <c>0x100884f0</c>
        /// (<c>0x100889D7..0x10088B56</c>): each field is skipped unless &gt; 0
        /// (<c>test/cmp ; jle</c>). The same writer is reused by digital tunnel 23
        /// / <c>ys_SetPetV</c> (<c>0x100735B0</c>) with Speed/Hit left 0.
        /// JSON key <c>Speed</c> @ <c>0x102BA7EC</c> writes word actor+0x1E8/+0x264
        /// (<c>0x10088ABB 66 89 83 e8 01 00 00</c>), the same word native 基本剑术
        /// adds 准确 into (<c>0x76AF99 66 01 83 64 02 00 00</c>). JSON <c>Hit</c>
        /// @ <c>0x102BA7F4</c> writes +0x1EA/+0x266.
        /// Opcode-0 scatter was not found in the dump; round uses the same formula
        /// as <see cref="CreateMon"/>'s ranger.
        /// </summary>
        public int NpcCreateMonsFromPayload(string payload)
        {
            if (!TryParseNpcCreatMonsPayload(payload, out var spec, out var error))
                throw new YanshenApiUnavailableException("NPC_CreatMons", "npc自定义函数", error);

            return NpcCreateMons(spec.X, spec.Y, spec.Num, spec.Round,
                spec.Ac, spec.Mac, spec.Dc, spec.DcMax, spec.Mc, spec.Sc,
                spec.Speed, spec.Hit, spec.Hp, spec.MaxHp, spec.AttackSpd, spec.WalkSpd,
                spec.MonName, spec.Map);
        }

        public int NpcCreateMons(int x, int y, int num, int round,
            int ac, int mac, int dc, int dcMax, int mc, int sc,
            int speed, int hit, int hp, int maxHp, int attackSpd, int walkSpd,
            string monName, string mapName)
        {
            var map = M2Share.MapManager.FindMap(mapName);
            if (map == null || num <= 0) return 0;

            int created = 0;
            for (var i = 0; i < num; i++)
            {
                var spawnX = x;
                var spawnY = y;
                if (round > 0)
                {
                    spawnX += M2Share.RandomNumber.Random(round * 2 + 1) - round;
                    spawnY += M2Share.RandomNumber.Random(round * 2 + 1) - round;
                }

                var mon = M2Share.UserEngine.RegenMonsterByName(mapName, (short)spawnX, (short)spawnY, monName);
                if (mon == null) continue;

                ApplyYanshenMonsterAttrs(mon, ac, mac, dc, dcMax, mc, sc,
                    speed, hit, hp, maxHp, attackSpd, walkSpd);
                mon.StatusChanged();
                created++;
            }
            return created;
        }

        internal static void ApplyYanshenMonsterAttrs(TBaseObject mon,
            int ac, int mac, int dc, int dcMax, int mc, int sc,
            int speed, int hit, int hp, int maxHp, int attackSpd, int walkSpd)
        {
            if (mon == null) return;

            if (ac > 0)
            {
                var packed = HUtil32.MakeLong(ac, ac);
                if (mon.m_Abil != null) mon.m_Abil.AC = packed;
                mon.m_WAbil.AC = packed;
            }

            if (mac > 0)
            {
                var packed = HUtil32.MakeLong(mac, mac);
                if (mon.m_Abil != null) mon.m_Abil.MAC = packed;
                mon.m_WAbil.MAC = packed;
            }

            if (dc > 0)
            {
                if (mon.m_Abil != null)
                    mon.m_Abil.DC = HUtil32.MakeLong(dc, HUtil32.HiWord(mon.m_Abil.DC));
                mon.m_WAbil.DC = HUtil32.MakeLong(dc, HUtil32.HiWord(mon.m_WAbil.DC));
            }

            if (dcMax > 0)
            {
                if (mon.m_Abil != null)
                    mon.m_Abil.DC = HUtil32.MakeLong(HUtil32.LoWord(mon.m_Abil.DC), dcMax);
                mon.m_WAbil.DC = HUtil32.MakeLong(HUtil32.LoWord(mon.m_WAbil.DC), dcMax);
            }

            if (mc > 0)
            {
                var packed = HUtil32.MakeLong(mc, mc);
                if (mon.m_Abil != null) mon.m_Abil.MC = packed;
                mon.m_WAbil.MC = packed;
            }

            if (sc > 0)
            {
                var packed = HUtil32.MakeLong(sc, sc);
                if (mon.m_Abil != null) mon.m_Abil.SC = packed;
                mon.m_WAbil.SC = packed;
            }

            if (speed > 0)
                mon.m_btHitPoint = unchecked((ushort)speed);

            if (hit > 0)
            {
                mon.m_wSpeedPoint = unchecked((ushort)hit);
                mon.m_btSpeedPoint = unchecked((byte)hit);
            }

            if (hp > 0)
            {
                if (mon.m_Abil != null) mon.m_Abil.HP = hp;
                mon.m_WAbil.HP = hp;
            }

            if (maxHp > 0)
            {
                if (mon.m_Abil != null) mon.m_Abil.MaxHP = maxHp;
                mon.m_WAbil.MaxHP = maxHp;
            }

            if (attackSpd > 0)
                mon.m_nNextHitTime = attackSpd;

            if (walkSpd > 0)
                mon.m_nWalkSpeed = walkSpd;
        }

        internal static bool TryParseNpcCreatMonsPayload(string payload,
            out NpcCreatMonsSpec spec, out string error)
        {
            spec = default;
            if (string.IsNullOrEmpty(payload))
            {
                error = "NPC_CreatMons 载荷为空";
                return false;
            }

            var fields = payload.Split('^');
            if (fields.Length != NpcCreatMonsFieldCount)
            {
                error = $"NPC_CreatMons 载荷必须是 {NpcCreatMonsFieldCount} 段（生产 NpcFuc 含 Hit），实际 {fields.Length}";
                return false;
            }

            if (!string.Equals(fields[0], "0", StringComparison.Ordinal))
            {
                error = $"NPC_CreatMons 载荷首段必须是 opcode 0，实际 '{fields[0]}'";
                return false;
            }

            if (!TryParseNpcCreatMonsInt(fields[1], "x", out var x, out error)
                || !TryParseNpcCreatMonsInt(fields[2], "y", out var y, out error)
                || !TryParseNpcCreatMonsInt(fields[3], "num", out var num, out error)
                || !TryParseNpcCreatMonsInt(fields[4], "round", out var round, out error)
                || !TryParseNpcCreatMonsInt(fields[5], "Ac", out var ac, out error)
                || !TryParseNpcCreatMonsInt(fields[6], "Mac", out var mac, out error)
                || !TryParseNpcCreatMonsInt(fields[7], "Dc", out var dc, out error)
                || !TryParseNpcCreatMonsInt(fields[8], "DcMax", out var dcMax, out error)
                || !TryParseNpcCreatMonsInt(fields[9], "Mc", out var mc, out error)
                || !TryParseNpcCreatMonsInt(fields[10], "Sc", out var sc, out error)
                || !TryParseNpcCreatMonsInt(fields[11], "Speed", out var speed, out error)
                || !TryParseNpcCreatMonsInt(fields[12], "Hit", out var hit, out error)
                || !TryParseNpcCreatMonsInt(fields[13], "hp", out var hp, out error)
                || !TryParseNpcCreatMonsInt(fields[14], "Maxhp", out var maxHp, out error)
                || !TryParseNpcCreatMonsInt(fields[15], "AttackSpd", out var attackSpd, out error)
                || !TryParseNpcCreatMonsInt(fields[16], "WalkSpd", out var walkSpd, out error))
                return false;

            spec = new NpcCreatMonsSpec
            {
                X = x,
                Y = y,
                Num = num,
                Round = round,
                Ac = ac,
                Mac = mac,
                Dc = dc,
                DcMax = dcMax,
                Mc = mc,
                Sc = sc,
                Speed = speed,
                Hit = hit,
                Hp = hp,
                MaxHp = maxHp,
                AttackSpd = attackSpd,
                WalkSpd = walkSpd,
                MonName = fields[17],
                Map = fields[18],
            };
            error = null;
            return true;
        }

        private static bool TryParseNpcCreatMonsInt(string text, string name, out int value, out string error)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                error = null;
                return true;
            }

            value = 0;
            error = $"NPC_CreatMons 字段 {name} 不是整数: '{text}'";
            return false;
        }

        internal struct NpcCreatMonsSpec
        {
            public int X, Y, Num, Round, Ac, Mac, Dc, DcMax, Mc, Sc, Speed, Hit, Hp, MaxHp, AttackSpd, WalkSpd;
            public string MonName, Map;
        }

        public int AttackPlayer(TPlayObject player, int hp, int effectId)
        {
            if (player == null || hp <= 0 || player.m_boDeath
                || player.m_boSuperMan || player.m_boSupermanItem) return 0;

            var damage = Math.Min(player.m_WAbil.HP, Math.Max(0, hp));
            if (effectId > 0)
                player.SendRefMsg(Grobal2.RM_MAGICFIRE, 0,
                    HUtil32.MakeWord(0, effectId),
                    HUtil32.MakeLong(player.m_nCurrX, player.m_nCurrY),
                    player.ObjectId, "");

            player.m_WAbil.HP = Math.Max(0, player.m_WAbil.HP - damage);
            player.SendRefMsg(Grobal2.RM_STRUCK, (short)damage, player.m_WAbil.HP,
                player.m_WAbil.MaxHP, _player?.ObjectId ?? 0, "");
            if (player.m_WAbil.HP == 0 && !player.m_boDeath)
                player.Die();
            return damage;
        }

        public int SetGlobalInt(string key, int value)
        {
            if (string.IsNullOrWhiteSpace(key)) return -1;
            if (value == -1)
            {
                _volatileInts.TryRemove(key, out _);
                return -1;
            }

            _volatileInts[key] = value;
            return value;
        }

        public int GetGlobalInt(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && _volatileInts.TryGetValue(key, out var value)
                ? value
                : -1;
        }

        public int SetGlobalString(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) return 0;
            _volatileStrings[key] = value ?? string.Empty;
            return 1;
        }

        public string GetGlobalString(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && _volatileStrings.TryGetValue(key, out var value)
                ? value
                : string.Empty;
        }

        public int GetHeroAttr(TPlayObject player, int type)
        {
            var owner = player ?? _player;
            var hero = owner?.m_HeroObject;
            if (hero == null) return -1;

            return type switch
            {
                0 => hero.m_nCurrX,
                1 => hero.m_nCurrY,
                2 => hero.m_WAbil.HP,
                3 => hero.m_WAbil.MaxHP,
                4 => hero.m_WAbil.MP,
                5 => hero.m_WAbil.MaxMP,
                6 => HUtil32.LoWord(hero.m_WAbil.DC),
                7 => HUtil32.HiWord(hero.m_WAbil.DC),
                8 => HUtil32.LoWord(hero.m_WAbil.MC),
                9 => HUtil32.HiWord(hero.m_WAbil.MC),
                10 => HUtil32.LoWord(hero.m_WAbil.SC),
                11 => HUtil32.HiWord(hero.m_WAbil.SC),
                12 => HUtil32.LoWord(hero.m_WAbil.AC),
                13 => HUtil32.HiWord(hero.m_WAbil.AC),
                14 => HUtil32.LoWord(hero.m_WAbil.MAC),
                15 => HUtil32.HiWord(hero.m_WAbil.MAC),
                16 => hero.m_nAntiMagic,
                17 => hero.m_btAntiPoison,
                18 => hero.m_btAntiPoison, // 2.08 示例文档此处描述与 17 重复，先按原文保留
                19 => hero.m_nPoisonRecover,
                20 => hero.m_nHealthRecover,
                21 => hero.m_nSpellRecover,
                22 => hero.m_WAbil.WearWeight,
                23 => hero.m_WAbil.MaxWearWeight,
                24 => hero.m_WAbil.Weight,
                25 => hero.m_WAbil.MaxWeight,
                26 => hero.m_Abil.Exp,
                27 => hero.m_Abil.MaxExp > 0 ? hero.m_Abil.MaxExp : hero.GetLevelExp(hero.m_Abil.Level),
                28 => hero.m_nLuck,
                29 => hero.m_btSpeedPoint,
                30 => hero.m_btHitPoint,
                31 => hero.m_WAbil.HandWeight,
                32 => hero.m_WAbil.MaxHandWeight,
                _ => -1
            };
        }

        public int NewPushPull(TPlayObject player, int distance, int direction, int probability,
            int range, int targetX, int targetY, int canl, int isQun, int roleId)
        {
            var caster = player ?? _player;
            if (caster == null || caster.m_PEnvir == null || distance <= 0) return 0;

            var targets = new List<TBaseObject>();
            if (roleId > 0)
            {
                var target = FindObjectById(roleId);
                if (target == null || target.m_PEnvir != caster.m_PEnvir
                    || !caster.IsProperTarget(target)) return 0;
                targetX = target.m_nCurrX;
                targetY = target.m_nCurrY;
                targets.Add(target);
            }
            else
            {
                caster.m_PEnvir.GetRangeBaseObject(targetX, targetY, Math.Max(0, range), canl != 0, targets);
            }

            if (canl > 0 && ChebyshevDistance(caster.m_nCurrX, caster.m_nCurrY,
                    targetX, targetY) > canl)
                return 0;

            if (targets.Count == 0) return 0;

            var applyAll = isQun == 0 && targets.Count > 1;
            if (applyAll && M2Share.RandomNumber.Random(100) >= probability)
                return 0;

            int moved = 0;
            foreach (var target in targets)
            {
                if (target == null || target.m_boDeath || ReferenceEquals(target, caster)
                    || target.m_PEnvir != caster.m_PEnvir || !caster.IsProperTarget(target)) continue;
                if (!applyAll && M2Share.RandomNumber.Random(100) >= probability) continue;

                var pushDir = direction == 0
                    ? M2Share.GetNextDirection(caster.m_nCurrX, caster.m_nCurrY, target.m_nCurrX, target.m_nCurrY)
                    : M2Share.GetNextDirection(target.m_nCurrX, target.m_nCurrY, caster.m_nCurrX, caster.m_nCurrY);
                moved += target.CharPushed(pushDir, distance);
            }
            return moved;
        }

        private static string SerializeItemData(TUserItem item)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(0x37325359); // YS27
            var packet = item.GetBuffer();
            writer.Write(packet.Length);
            writer.Write(packet);
            writer.Write(item.UpgradeFlags);
            writer.Write(item.NativeRecord?.Length ?? 0);
            if (item.NativeRecord != null) writer.Write(item.NativeRecord);

            var itemName = M2Share.UserEngine?.GetStdItemName(item.wIndex) ?? string.Empty;
            var opaque = Convert.ToBase64String(stream.ToArray());
            var add = item.btValue ?? Array.Empty<byte>();
            int A(int index) => index < add.Length ? add[index] : 0;
            return $"YS207|1|0|0|{itemName}|{opaque},0,0,0,0,0,0,0,0,0,{A(0)},{A(1)},{A(2)},{A(3)},{A(4)}";
        }

        private static bool TryDeserializeItemData(string data, out TUserItem item)
        {
            item = null;
            var firstField = data.Split(',', 2)[0];
            var parts = firstField.Split('|');
            if (parts.Length < 6 || parts[0] != "YS207") return false;
            try
            {
                var raw = Convert.FromBase64String(parts[5]);
                using var stream = new MemoryStream(raw, writable: false);
                using var reader = new BinaryReader(stream);
                if (reader.ReadInt32() != 0x37325359) return false;
                var packetLength = reader.ReadInt32();
                if (packetLength <= 0 || packetLength > stream.Length - stream.Position) return false;
                item = Packets.ToPacket<TUserItem>(reader.ReadBytes(packetLength));
                if (item == null) return false;
                if (stream.Position < stream.Length) item.UpgradeFlags = reader.ReadByte();
                if (stream.Position + sizeof(int) <= stream.Length)
                {
                    var nativeLength = reader.ReadInt32();
                    if (nativeLength < 0 || nativeLength > stream.Length - stream.Position) return false;
                    item.NativeRecord = nativeLength == 0 ? null : reader.ReadBytes(nativeLength);
                }
                return true;
            }
            catch (FormatException) { return false; }
            catch (EndOfStreamException) { return false; }
        }

        // ═══════════════════════════════════════════════════════════════
        // 6.8 角色属性/组队 — 8 functions
        // ═══════════════════════════════════════════════════════════════

        /// <summary>获取角色/怪物属性: types 0=X 1=Y 2=curHP 3=maxHP 4=curMP 5=maxMP 6=minDC 7=maxDC 8=minMC 9=maxMC 10=minSC 11=maxSC 12=minAC 13=maxAC 14=minMAC 15=maxMAC</summary>
        public int GetCreatureAttr(int roleId, int type)
        {
            if (!Enabled("行会显示")) return 0;
            // Find creature by roleId
            TBaseObject target = null;
            if (roleId == 0 || roleId == _player.ObjectId) target = _player;
            else
            {
                var list = new List<TBaseObject>();
                _player.m_PEnvir?.GetRangeBaseObject(_player.m_nCurrX, _player.m_nCurrY, 20, true, list);
                foreach (var t in list)
                    if (t.ObjectId == roleId) { target = t; break; }
            }
            if (target == null) return 0;
            return type switch
            {
                0 => target.m_nCurrX,  1 => target.m_nCurrY,
                2 => target.m_WAbil.HP,  3 => target.m_WAbil.MaxHP,
                4 => target.m_WAbil.MP,  5 => target.m_WAbil.MaxMP,
                6 => HUtil32.LoWord(target.m_WAbil.DC),  7 => HUtil32.HiWord(target.m_WAbil.DC),
                8 => HUtil32.LoWord(target.m_WAbil.MC),  9 => HUtil32.HiWord(target.m_WAbil.MC),
                10 => HUtil32.LoWord(target.m_WAbil.SC), 11 => HUtil32.HiWord(target.m_WAbil.SC),
                12 => HUtil32.LoWord(target.m_WAbil.AC), 13 => HUtil32.HiWord(target.m_WAbil.AC),
                14 => HUtil32.LoWord(target.m_WAbil.MAC),15 => HUtil32.HiWord(target.m_WAbil.MAC),
                _ => 0
            };
        }

        /// <summary>组队成员数量</summary>
        public int GetGroupMemberCount()
        {
            if (!Enabled("眼神特殊函数")) return 0;
            return _player.m_GroupOwner?.m_GroupMembers?.Count ?? 0;
        }

        /// <summary>组队成员roleId(按索引)</summary>
        public int GetGroupMemberRoleId(int index)
        {
            if (!Enabled("眼神特殊函数")) return 0;
            var members = _player.m_GroupOwner?.m_GroupMembers;
            return members != null && index < members.Count && members[index] != null
                ? members[index].ObjectId
                : 0;
        }

        /// <summary>组队成员角色名(按索引)</summary>
        public string GetGroupMemberName(int index)
        {
            if (!Enabled("眼神特殊函数")) return string.Empty;
            var members = _player.m_GroupOwner?.m_GroupMembers;
            return members != null && index < members.Count && members[index] != null
                ? members[index].m_sCharName
                : string.Empty;
        }

        /// <summary>获取英雄极品值</summary>
        public int GetHeroExtreme(int pos, int id)
        {
            // Hero not available on every server — self only
            return GetItemExtreme(0, pos, id);
        }

        /// <summary>获取/设置技能经验: isMax=1满级, isHero=1英雄</summary>
        public int SetSkillExp(string skillName, int isMax, int isHero)
        {
            foreach (var magic in _player.m_MagicList)
            {
                if (magic.MagicInfo != null && string.Equals(magic.MagicInfo.sMagicName, skillName, StringComparison.OrdinalIgnoreCase))
                {
                    if (isMax == 1)
                    {
                        magic.btLevel = 3;
                        magic.nTranPoint = 100000;
                    }
                    else
                    {
                        magic.btLevel = Math.Min((byte)3, (byte)(magic.btLevel + 1));
                    }
                    return 1;
                }
            }
            return 0;
        }

        // ═══════════════════════════════════════════════════════════════
        // 6.9 数据库操作 — 3 functions
        // ═══════════════════════════════════════════════════════════════

        /// <summary>执行SQL(INSERT/UPDATE/DELETE): fg=true返回查询的ID值</summary>
        public int SqlDbInsert(string sql, bool returnId)
        {
            if (!Enabled("眼神特殊函数")) return 0;
            try
            {
                string connStr = _pluginManager?.GetPluginSetting<string>("YanshenCompat", "dbConnection", null);
                if (string.IsNullOrEmpty(connStr))
                    connStr = M2Share.g_Config?.sConnctionString;
                if (string.IsNullOrEmpty(connStr)) return 0;
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(connStr);
                conn.Open();
                using var cmd = new MySql.Data.MySqlClient.MySqlCommand(sql, conn);
                int rows = cmd.ExecuteNonQuery();
                if (returnId) return (int)cmd.LastInsertedId;
                return rows;
            }
            catch (Exception ex) { M2Share.MainOutMessage("[异常] SqlDbInsert " + ex.Message); return 0; }
        }

        /// <summary>SQL查询返回字符串 (通过GetSignInActPrizer实现)</summary>
        public string SqlDbSelect(string sql)
        {
            if (!Enabled("眼神特殊函数")) return "";
            try
            {
                string connStr = _pluginManager?.GetPluginSetting<string>("YanshenCompat", "dbConnection", null);
                if (string.IsNullOrEmpty(connStr))
                    connStr = M2Share.g_Config?.sConnctionString;
                if (string.IsNullOrEmpty(connStr)) return "";
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(connStr);
                conn.Open();
                using var cmd = new MySql.Data.MySqlClient.MySqlCommand(sql, conn);
                using var reader = cmd.ExecuteReader();
                var sb = new System.Text.StringBuilder();
                while (reader.Read())
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(reader[i]?.ToString() ?? "");
                    }
                    sb.Append(';');
                }
                return sb.ToString().TrimEnd(';');
            }
            catch (Exception ex) { M2Share.MainOutMessage("[异常] SqlDbSelect " + ex.Message); return ""; }
        }

        /// <summary>向DBServer发送消息 — 通过插件系统发送DB操作消息</summary>
        public int SendDbMsg(int id, string sql)
        {
            if (!Enabled("眼神特殊函数")) return 0;
            // Send DB message via plugin manager or direct connection
            try { SqlDbInsert(sql, false); }
            catch (YanshenApiUnavailableException) { throw; }
            catch (Exception ex) { M2Share.MainOutMessage("[异常] SendDbMsg " + ex.Message); }
            return id;
        }

        // ═══════════════════════════════════════════════════════════════
        // 6.10 其他 — 20 functions
        // ═══════════════════════════════════════════════════════════════

        /// <summary>播放特效 — 在指定位置播放WIL特效(SendRefMsg)</summary>
        public int PlayEffect(int range, int tx, int ty, int all, int effectId)
        {
            if (!Enabled("自定义伤害")) return 0;
            if (all == 1)
            {
                // Broadcast to all visible
                _player.SendRefMsg(Grobal2.RM_MAGICFIRE, (short)effectId, (short)tx, (short)ty, 0, "");
            }
            else
            {
                // Send to a single target or self
                foreach (var t in FindTargets(tx, ty, Math.Max(range, 1), true))
                    t.SendRefMsg(Grobal2.RM_MAGICFIRE, (short)effectId, (short)tx, (short)ty, 0, "");
            }
            return effectId;
        }

        /// <summary>弹射/溅射技能: js 0=弹射 1=溅射</summary>
        public int BounceSkill(int magicId, int x, int y, int roleId, int times, int range, int double_, int cutting, int effectId, int js)
        {
            if (!Enabled("自定义伤害")) return 0;
            // Build list of targets and cascade damage
            var targets = FindTargets(x, y, range, true);
            int dmg = cutting;
            int count = 0;
            foreach (var t in targets)
            {
                if (t == null || t.m_boDeath) continue;
                int appliedDmg = dmg;
                if (double_ > 0) appliedDmg = appliedDmg * double_ / 1000;
                if (appliedDmg > 0)
                {
                    t.m_WAbil.HP = TBaseObject.ClampAbility((long)t.m_WAbil.HP - appliedDmg);
                    t.SendRefMsg(Grobal2.RM_STRUCK, (short)appliedDmg, t.m_WAbil.HP, t.m_WAbil.MaxHP, _player.ObjectId, "");
                    count++;
                }
                if (count >= times) break;
                // For splash (js=1), damage decreases with each bounce
                if (js == 1) dmg = dmg * 70 / 100;
            }
            return count;
        }

        /// <summary>自定义火墙: 在指定位置创建火焰事件</summary>
        public int CustomFireWall(int magicId, int damage, int duration, int range, int x, int y, int flag)
        {
            if (!Enabled("火墙修改")) return 0;
            var envir = _player.m_PEnvir; if (envir == null) return 0;
            // Create fire event at specified positions within range
            for (int i = 0; i < range; i++)
            {
                for (int j = 0; j < range; j++)
                {
                    int fx = x + i;
                    int fy = y + j;
                    if (fx < 0 || fy < 0 || fx >= envir.wWidth || fy >= envir.wHeight) continue;
                    if (!envir.CanWalk(fx, fy, false)) continue;
                    // Create fire event
                    var fireEvent = (FireBurnEvent)envir.GetEvent(fx, fy);
                    if (fireEvent == null)
                    {
                        fireEvent = new FireBurnEvent(_player, fx, fy, Grobal2.ET_FIRE, duration * 1000, damage);
                        M2Share.EventManager.AddEvent(fireEvent);
                    }
                }
            }
            return magicId;
        }

        /// <summary>发送特效通信给客户端(需客户端补丁)</summary>
        public int SendClientEffect(int roleId, int id, int hp, int sx, int sy, int tx, int ty, string rs, string img1, string img2)
        {
            // Send to self (the current player context) as the engine does not expose a public roleId→player lookup
            _player.SendRefMsg(Grobal2.RM_MAGICFIRE, (short)id, (short)tx, (short)ty, 0, rs);
            return roleId;
        }

        /// <summary>在线改名: 1=成功 2=超长 3=含@ 4=含! 5=含空格 6=重名 7=英雄重名</summary>
        public int Rename(string newName)
        {
            if (!Enabled("眼神特殊函数")) return 0;
            if (string.IsNullOrEmpty(newName) || newName.Length > 14) return 2;
            if (newName.Contains('@')) return 3;
            if (newName.Contains('!')) return 4;
            if (newName.Contains(' ')) return 5;
            // Check duplicate
            var exist = M2Share.UserEngine.GetPlayObject(newName);
            if (exist != null) return 6;
            // Perform rename via engine
            _player.m_sCharName = newName;
            _player.SendRefMsg(Grobal2.RM_MYSTATUS, 0, 0, 0, 0, "");
            return 1;
        }

        /// <summary>强制玩家下线</summary>
        public int KickPlayer() { if (!Enabled("踢玩家下线")) return 0; _player.m_boEmergencyClose = true; return 1; }

        /// <summary>
        /// 数字隧道 20 / <c>ys_CheckMapMonByName</c>。处理函数 <c>0x10073210</c>：
        /// 向量 <c>0x1007325F 83 F8 04 cmp eax,4 / jae</c>，不足返回 -1
        /// （<c>0x10073273 83 C8 FF</c>）。<c>at(2)=MapName at(3)=MonName</c>。
        /// 空地图名与 <c>0x102B2918</c> 空串比较后 <c>edx=0</c>
        /// （<c>0x100733AA BA 00000000</c>），转调宿主 CheckMapMonByName，
        /// 宿主 <c>0x646B79 85 F6 / je</c> 时读 <c>[this+0x128]</c> 当前图。
        /// </summary>
        public int CheckMapMonByName(string mapName, string monName)
        {
            Envirnoment map;
            if (string.IsNullOrEmpty(mapName))
                map = _player?.m_PEnvir ?? _npc?.m_PEnvir;
            else
                map = M2Share.MapManager?.FindMap(mapName);
            if (map == null) return 0;
            int count = 0;
            var list = new List<TBaseObject>();
            M2Share.UserEngine.GetMapMonster(map, list);
            foreach (var m in list)
            {
                if (m?.m_sCharName == null) continue;
                if (string.IsNullOrEmpty(monName)
                    || m.m_sCharName.IndexOf(monName, StringComparison.OrdinalIgnoreCase) >= 0)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 无限定时器。插件面板说明：timer&gt;0 按毫秒间隔反复调用 RunQuest.pas 里
        /// 的同名无参过程，timer=0 清理该名字的定时器，名字给 'ClearAll' 清理全部。
        ///
        /// 注册未实现：本工程没有回调派发层，注册成功也永远不会回调 funcName。
        /// 旧实现直接 return interval，脚本拿到非零值会当成注册成功。
        /// 清理路径本就是空操作，如实返回 0；注册路径必须报错而不是假装成功。
        /// </summary>
        public int SetLoopTimer(int interval, string funcName)
        {
            if (!Enabled("全局循环函数")) return 0;
            if (interval <= 0) return 0;
            throw new YanshenApiUnavailableException("Ys_SetTimerByName", "全局循环函数",
                $"定时器注册未实现，'{funcName}' 不会被回调");
        }

        /// <summary>沙巴克城主行会名</summary>
        public string GetCastleGuildName()
        {
            var castle = M2Share.CastleManager.GetCastle(0);
            return castle?.m_sOwnGuild ?? "";
        }

        /// <summary>
        /// 沙巴克城主角色名。AllFuc.pas 的拼写是 Ys_GetCastleLoadName（Load 不是
        /// Lord），与原生 M2 脚本函数 GetCastleLoadName 同名同义。
        /// </summary>
        public string GetCastleLoadName()
        {
            var castle = M2Share.CastleManager.GetCastle(0);
            return castle?.m_MasterGuild?.GetChiefName() ?? "";
        }

        private static bool SetElementValue(TUserItem item, int elementType, int value)
        {
            if (item == null || elementType < 1 || elementType > 17) return false;
            if (elementType == 1)
            {
                item.ys1 = Math.Max(0, value);
                return true;
            }
            var clamped = (byte)Math.Min(255, Math.Max(0, value));
            switch (elementType)
            {
                case 2: item.ys2 = clamped; break;
                case 3: item.ys3 = clamped; break;
                case 4: item.ys4 = clamped; break;
                case 5: item.ys5 = clamped; break;
                case 6: item.ys6 = clamped; break;
                case 7: item.ys7 = clamped; break;
                case 8: item.ys8 = clamped; break;
                case 9: item.ys9 = clamped; break;
                case 10: item.ys10 = clamped; break;
                case 11: item.ys11 = clamped; break;
                case 12: item.ys12 = clamped; break;
                case 13: item.ys13 = clamped; break;
                case 14: item.ys14 = clamped; break;
                case 15: item.ys15 = clamped; break;
                case 16: item.ys16 = clamped; break;
                case 17: item.ys17 = clamped; break;
            }
            return true;
        }

        private static int GetElementValue(TUserItem item, int elementType)
        {
            if (item == null) return 0;
            return elementType switch
            {
                1 => item.ys1,
                2 => item.ys2,
                3 => item.ys3,
                4 => item.ys4,
                5 => item.ys5,
                6 => item.ys6,
                7 => item.ys7,
                8 => item.ys8,
                9 => item.ys9,
                10 => item.ys10,
                11 => item.ys11,
                12 => item.ys12,
                13 => item.ys13,
                14 => item.ys14,
                15 => item.ys15,
                16 => item.ys16,
                17 => item.ys17,
                _ => 0
            };
        }

        private static bool SetExtremeValue(TUserItem item, int index, int value)
        {
            if (item == null || index < 0 || index > 5) return false;
            var clamped = (byte)Math.Min(255, Math.Max(0, value));
            switch (index)
            {
                case 0: item.jp1 = clamped; break;
                case 1: item.jp2 = clamped; break;
                case 2: item.jp3 = clamped; break;
                case 3: item.jp4 = clamped; break;
                case 4: item.jp5 = clamped; break;
                case 5: item.jp6 = clamped; break;
            }
            return true;
        }

        private static int GetExtremeValue(TUserItem item, int index)
        {
            if (item == null) return 0;
            return index switch
            {
                0 => item.jp1,
                1 => item.jp2,
                2 => item.jp3,
                3 => item.jp4,
                4 => item.jp5,
                5 => item.jp6,
                _ => 0
            };
        }

        // ═══════════════════════════════════════════════════════════════
        // 7.0 Engine Hook — complete 379-key toggle + param bridge
        // Usage: engine code calls api.IsOn("keyName") before executing logic
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Public helper: check if a Chinese-config-key feature is enabled (0=falsy, non-zero=truthy)</summary>
        public bool IsOn(string chineseKey) => Enabled(chineseKey);
        /// <summary>Get float param value by Chinese config key</summary>
        public double ParamF(string chineseKey, double def = 0) => GetParam(chineseKey, def);
        /// <summary>Get int param value by Chinese config key</summary>
        public int Param(string chineseKey, int def = 0) => GetParamInt(chineseKey, def);
        /// <summary>Get string param value by Chinese config key</summary>
        public string ParamS(string chineseKey, string def = "") { if (_pluginManager == null) return def; var v = _pluginManager.GetNativeConfigValue(chineseKey); if (v is string s && !string.IsNullOrEmpty(s)) return s; try { if (v is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.String) return je.GetString() ?? def; } catch { } return v?.ToString() ?? def; }

        // ── Warrior skill overrides ──
        // These six are not interpreted by the plugin at attack time: the config
        // dialog rewrites M2Server code bytes once and the native formula then
        // runs unchanged. Every clamp below is the clamp the plugin applies
        // before the write, and every factor table is the set of values the
        // rewritten instruction can actually encode.

        public bool IsStabSword() => Enabled("刺杀剑术");
        // 0x100B40BB `A2 50 1C 77 00` stores A over the imm8 of host
        // 0x00771C4E `83 C0 02 add eax,2`. That `add` is 32-bit with a
        // sign-extended imm8, hence 0x100B404E `cmp eax,0x7F` + 0x100B4069
        // `cmovg eax,ecx`, and hence the signed reading of the stored byte.
        public int StabSwordA() =>
            unchecked((sbyte)Math.Min(ParamAtoi("刺杀剑术_A值", 2), 127));
        // 0x100B4129 `A3 24 1D 77 00` overwrites [0x00771D24], the float32 the
        // host divides by at 0x00771C5A `D8 35 24 1D 77 00`. Stock value there
        // is `00 00 A0 40` = 5.0f.
        public float StabSwordB() => ParamAtof32("刺杀剑术_B值", 5f);

        public bool IsHalfMoon() => Enabled("半月弯刀");
        // 0x100B42F2 `A2 46 20 77 00` -> imm8 of 0x00772044 `83 C0 02`, same
        // 32-bit `add` with a sign-extended imm8, same cap at 0x100B428E.
        public int HalfMoonA() =>
            unchecked((sbyte)Math.Min(ParamAtoi("半月弯刀_A值", 2), 127));
        // 0x100B4360 `A3 48 21 77 00` -> [0x00772148], read by 0x00772050
        // `D8 35 48 21 77 00`. Stock value `00 00 70 41` = 15.0f.
        public float HalfMoonB() => ParamAtof32("半月弯刀_B值", 15f);

        public bool IsFireSword() => Enabled("烈火剑法");
        public int FireSwordA() => ParamAtoi("烈火剑法_A值", 4);
        // 0x100B4550 `A2 F0 B0 76 00` -> imm8 of 0x0076B0EF `04 04 add al,4`,
        // capped to 255 at 0x100B4491.
        public int FireSwordB() => Math.Min(ParamAtoi("烈火剑法_B值", 4), 255);

        public bool IsThrusting() => Enabled("攻杀剑术");
        // 0x100B3F5A `A2 2D B0 76 00` -> imm8 of 0x0076B02C `04 05 add al,5`,
        // capped to 255 at 0x100B3F04.
        public int ThrustingA() => Math.Min(ParamAtoi("攻杀剑术_A值", 5), 255);

        public bool IsSunSword() => Enabled("逐日剑法");
        // 0x100B47D9 `A2 4D B1 76 00` -> imm8 of the `04 07 add al,7` that
        // 0x100B4750 splices over host 0x0076B14C, capped to 255 at 0x100B46F6.
        public int SunSwordA() => Math.Min(ParamAtoi("逐日剑法_A值", 7), 255);
        // 0x100B4847 `A3 A4 1D 77 00` -> imm32 of 0x00771DA3 `B9 0A 00 00 00
        // mov ecx,0xA`, the divisor of the `idiv ecx` at 0x00771DA9. Integer,
        // not float: this one goes through atoi, not atof.
        public int SunSwordB() => ParamAtoi("逐日剑法_B值", 6);

        public bool IsOneSword() => PatchToggleOn("基本剑术");
        public int OneSwordN() => ParamAtoi("基本剑术_n值", 3);

        // 复活戒指重设 is a host-code patch, same primitive as the six warrior
        // skills. Plugin 0x100B3472 `83 BF B8 05 00 00 00 cmp [edi+0x5B8],0`
        // (loader lag puts 复活戒指重设 at +0x5B8) / je 0x100B36D2 skips the
        // writes when the toggle is off. On: atoi(重设时间) then
        // `69 C0 E8 03 00 00 imul 0x3E8` is stored over the two `0xEA60`
        // immediates, and atoi(无敌时间) `0F B7 C0 movzx eax,ax` is stored
        // over `66 B9 02 00` at 0x743911.
        //   0x100B3501 A3 FA C4 73 00 -> host 0x73C4FA of `81 FE 60 EA 00 00`
        //   0x100B357B A3 58 37 74 00 -> host 0x743758 of `81 FA 60 EA 00 00`
        //   0x100B35E9 A3 80 C4 73 00 -> host 0x73C480 of `B8 3C 00 00 00`
        //   0x100B3657 66 A3 13 39 74 00 -> host 0x743913 of `mov cx,2`
        // 0x7CC on the cfg struct is the already-applied latch (written 0x64
        // after the patch, 0 when the toggle is off) — C# rereads the config
        // each revive, which is the player-visible result of uncheck/recheck.
        public bool IsReviveResetPatchOn() => PatchToggleOn("复活戒指重设");

        /// <summary>
        /// Milliseconds written over the two <c>cmp …,0xEA60</c> sites.
        /// Native default 60000; plugin <c>imul eax,0x3E8</c> is 32-bit wrap.
        /// </summary>
        public int ReviveResetCooldownMs() =>
            unchecked(ParamAtoi("复活戒指重设_重设时间", 60) * 1000);

        /// <summary>
        /// Seconds written over <c>mov cx,2</c> @0x743911. Plugin
        /// <c>0x100B34B7 0F B7 C0 movzx eax,ax</c> keeps the low 16 bits of
        /// atoi, including the zero-extended negative case (atoi -1 → 65535).
        /// </summary>
        public int ReviveResetImmuneSeconds() =>
            ParamAtoi("复活戒指重设_无敌时间", 2) & 0xFFFF;

        /// <summary>
        /// Toggle read for a code-patch override. Unlike a script API, "off"
        /// here is not a failure: it is simply the unpatched native
        /// instruction. Reading it must therefore never raise, because the
        /// recalc path that consults it can run inside a strict direct-call
        /// scope opened by some unrelated yanshen script function.
        /// </summary>
        public bool PatchToggleOn(string chineseKey)
        {
            if (_pluginManager == null) return false;
            var plugin = _pluginManager.GetPlugin("YanshenCompat");
            if (plugin?.State != PluginState.Running) return false;
            if (!plugin.IsInitialized && !IsInitializing(plugin)) return false;
            var lookupKey = _keyMap.TryGetValue(chineseKey, out var mapped) ? mapped : chineseKey;
            var nativeVal = _pluginManager.GetNativeConfigValue(lookupKey);
            return nativeVal != null && IsEnabledValue(nativeVal);
        }

        /// <summary>
        /// 烈火 multiplies the level with `shl eax,k`, so A survives only as a
        /// power of two. 0x100B44BB-0x100B44FD picks k for 0/2/4/8/16; the
        /// staged default at 0x100B4488/0x100B44B3 is `C1 E0 02` = shl eax,2,
        /// which every other A silently falls back to.
        /// </summary>
        public static int FireSwordLevelFactor(int a) => a switch
        {
            0 => 1,
            2 => 2,
            4 => 4,
            8 => 8,
            16 => 16,
            _ => 4,
        };

        /// <summary>
        /// 基本剑术 multiplies with `lea eax,[eax+eax*s]`, so n survives only as
        /// an encodable LEA scale. 0x100B4967-0x100B49A1 picks the SIB byte for
        /// 1/2/5/9; the staged default at 0x100B4946 is `8D 04 40` = x3.
        /// </summary>
        public static int OneSwordLevelFactor(int n) => n switch
        {
            1 => 1,
            2 => 2,
            5 => 5,
            9 => 9,
            _ => 3,
        };
        public bool IsZhenQi() => Enabled("无极真气");
        public double ZhenQiA() => GetParam("无极真气_A值", 10);
        public int ZhenQiTime() => GetParamInt("无极真气_时间", 6);
        public bool IsBloodSuck() => Enabled("嗜血术倍数");
        public bool IsBloodRange() => Enabled("嗜血术范围");
        public bool IsSummonShenShou() => Enabled("召唤神兽");
        public bool IsSummonKuLou() => Enabled("召唤骷髅");
        public bool IsModifyShenShou() => Enabled("修改召唤神兽");
        public int ShenShouIdx() => GetParamInt("神兽_序号", 0);

        /// <summary>
        /// 眼神写进宿主 imm8 的从宠数量。取值域由「被改写的那条指令能编码什么」决定：
        /// 插件只做上钳 127（<c>0x100A9DE9 83 F8 7F cmp eax,0x7F</c> /
        /// <c>0x100A9DEC 7E 07 jle</c> / <c>0x100A9DEE B8 7F000000 mov eax,0x7F</c>），
        /// 没有下钳，随后写入的是 <c>al</c>（<c>0x100A9E0F 88 85 2A EF FF FF</c>）。
        ///
        /// 被改写的那条是 <c>6A xx</c>（<c>push imm8</c>），x86 定义它**符号扩展**成
        /// dword，而 callee <c>sub_6CB070</c> 是按 dword 读这个槽的
        /// （<c>0x006CB1F0 FF 45 14 inc dword [ebp+0x14]</c> /
        /// <c>0x006CB297 3B 45 14 cmp eax,dword [ebp+0x14]</c>，`ret 0x10` 共 4 个栈参）。
        /// 所以负配置值是 <c>(sbyte)</c> 截断后**保持负数**，不是回绕成 0..255：
        /// <c>神兽_数量 = -1</c> → atoi 得 -1 → 不大于 0x7F → al = 0xFF →
        /// <c>push 0xFF</c> → <c>[ebp+0x14] = -1</c>（一只都不造），
        /// 用 <c>(byte)</c> 会算成 255（造 255 只）。
        /// </summary>
        internal static int NativeSlaveCountImm8(int v) => (sbyte)(v > 0x7F ? 0x7F : v);

        /// <summary>
        /// 召唤神兽的从宠数量。眼神把 <c>神兽_数量</c> 经 atoi(<c>0x1022DC49</c>) 后
        /// 改写宿主 <c>0x0076EE98 6A 01</c> 的 imm8（目标 <c>0x0076EE99</c>，原字节 <c>01</c>），
        /// 即 <c>0x0076EEB6 call [esi+0xEC]</c> 造宠调用的第一个栈参。
        /// 补丁点 <c>0x100A9E9B call 0x10033340(src, 1, 0x0076EE99, 0x0076EE99)</c>；
        /// 还原支 <c>0x100A9F33 C6 85 2B EF FF FF 01</c> 写回 <c>01</c>，即关闭态宿主就是 1。
        /// </summary>
        public int ShenShouSlaveCount() => IsSummonShenShou()
            ? NativeSlaveCountImm8(GetParamInt("神兽_数量", 1))
            : 1;

        /// <summary>
        /// 召唤骷髅的从宠数量。同构：宿主 <c>0x0076EE1E 6A 01</c> 的 imm8
        /// （目标 <c>0x0076EE1F</c>，原字节 <c>01</c>），补丁点
        /// <c>0x100AA04B call 0x10033340(src, 1, 0x0076EE1F, 0x0076EE1F)</c>，
        /// 上钳同样在 <c>0x100A9FED cmp eax,0x7F</c>；还原支 <c>0x100AA0BC</c> 写回 <c>01</c>。
        /// 注意名字常量 <c>0x0076EE70</c>「变异骷髅」全镜像没有任何补丁指向它，
        /// 所以骷髅只能改数量、不能改名字。
        /// </summary>
        public int KuLouSlaveCount() => IsSummonKuLou()
            ? NativeSlaveCountImm8(GetParamInt("召唤骷髅_数量", 1))
            : 1;

        /// <summary>
        /// 召唤神兽的怪物名。眼神按 <c>神兽_序号</c> 覆盖宿主 <c>0x0076EEEC</c> 处的
        /// 4 字节 GBK 串（原字节 <c>C9 F1 CA DE</c> =「神兽」）。Delphi 长度前缀
        /// <c>[0x0076EEE8] = 4</c> 不在补丁范围内，所以候选名恒为两个汉字。
        /// 选择链 <c>0x100A9E3E..0x100A9E5F</c>（sub/je 逐级比较）：
        /// 0 →「神兽」（<c>0x100A9DD7</c> 预置 <c>C9F1CADE</c>）、
        /// 1 →「月灵」（<c>0x100A9E59</c> 写 <c>D4C2C1E9</c>）、
        /// 2 →「白虎」（<c>0x100A9E4D</c> 写 <c>A2BBD7B0</c> → 内存序 <c>B0D7BBA2</c>）、
        /// 其余落回预置值。
        /// </summary>
        public string ShenShouName()
        {
            if (!IsSummonShenShou()) return "神兽";
            return ShenShouIdx() switch
            {
                1 => "月灵",
                2 => "白虎",
                _ => "神兽",
            };
        }

        // ── Mage skill toggle checks ──
        public bool IsFireBallSwitch() => Enabled("火球主属性切换");
        public bool IsFireBallRange() => Enabled("火球自定义范围");
        public bool IsFireRainSwitch() => Enabled("火雨主属切换");
        public int FireWallTime() => GetParamInt("火墙_时间", 120);
        public bool IsFireWallNoVamp() => Enabled("火墙不吸血");
        public bool IsFireWallCutting() => Enabled("火墙切割");
        public bool IsFireWallFixDmg() => Enabled("火墙固定增伤");
        public bool IsFireWallTimeLimit() => Enabled("火墙设置时间上限");
        public bool IsHellLightSwitch() => Enabled("地狱雷光可换主属性");
        public bool IsHellLightFactor() => Enabled("地狱雷光系数");
        public bool IsHellLightRange() => Enabled("地狱雷光范围");
        public bool IsBlastFlameSwitch() => Enabled("爆裂火焰可换主属性");
        public bool IsBlastFlameRange() => Enabled("爆裂火焰范围及系数");
        public bool IsIceStormSwitch() => Enabled("冰咆哮主属性切换");
        public bool IsIceStormCutting() => Enabled("冰咆哮切割");
        public bool IsIceStormFixDmg() => Enabled("冰咆哮固定增伤");
        public bool IsIceStormRange() => Enabled("冰咆哮范围");
        public bool IsLaserSwitch() => Enabled("激光电影可换主属性");
        public bool IsLaserHitRate() => Enabled("激光命中概率");
        public bool IsLaserRange() => Enabled("激光范围及系数");
        public bool IsLightningSwitch() => Enabled("雷电主属性切换");
        public bool IsLightningPoison() => Enabled("雷电带毒");
        public bool IsLightningCutting() => Enabled("雷电术切割");
        public bool IsLightningCustom() => Enabled("雷电术自定义伤害");
        public bool IsLightningCustomA() => Enabled("雷电术自定义伤害_系数A");
        public bool IsLightningCustomB() => Enabled("雷电术自定义伤害_系数B");
        public bool IsLightningRange() => Enabled("雷电自定义范围");
        public bool IsFireCutting() => Enabled("烈火切割");
        public bool IsFireFixDmg() => Enabled("烈火固定增伤");
        public bool IsAmuletCutting() => Enabled("火符切割");
        public bool IsAmuletFixDmg() => Enabled("火符固定增伤");
        public bool IsHalfMoonPoison() => Enabled("半月带毒");
        public bool IsWeaponGreenPoison() => Enabled("武器绿毒");
        public bool IsPhysicalPoison() => Enabled("物功带毒");
        public bool IsMageGroupPoison() => Enabled("法师群毒");
        public bool IsGroupPoison() => Enabled("群毒");
        public bool IsGroupPoisonVal() => Enabled("群毒值");
        public bool IsZhaoZeFix() => Enabled("噬魂沼泽绿毒修复");

        // ── Damage/Combat toggle checks ──
        public bool IsCuttingEnabled() => Enabled("刀刀切割");
        public bool IsReflectEnabled() => Enabled("攻击反伤");
        public bool IsLifeSteal() => Enabled("攻击吸血");
        public bool IsEquipSteal() => Enabled("装备吸血");
        public bool IsPoisonEnabled() => Enabled("施毒术");
        public int PoisonFormulaVal() => GetParamInt("施毒术_公式值", 10);
        public bool IsParalysisEnabled() => Enabled("麻痹概率");
        public bool IsParaImmune() => Enabled("麻痹中不被麻痹a");
        public bool IsBreakRevival() => Enabled("破复活");
        public bool IsAntiPoison() => Enabled("免毒符");
        public bool IsMultiDmg() => Enabled("多元伤害");
        public bool IsDmgReduction() => Enabled("千分比免伤");
        public bool IsExpMultiplier() => Enabled("千分比经验倍数");
        public bool IsLuckBlock() => Enabled("格位刺杀免伤a");
        public bool IsProbBlock() => Enabled("概率格挡a");
        public bool IsFixStabParalysis() => Enabled("修复刺杀位麻痹");
        // Code patch at host 0x00767910 (jle → jmp), not a script API.
        // Off means the unpatched luck-max armour roll, so this must not
        // raise inside a strict yanshen call the way Enabled() does.
        public bool IsFixDefense() => PatchToggleOn("修复卡防御");
        /// <summary>
        /// 防0拆分. A detour, not a script API: 0x100AA6DA gates on the switch,
        /// 0x100AA765 calls the trampoline builder 0x10032FD0 with begin/end
        /// 0x6E0FF3/0x6E0FF9 over ClientSplitItem's prologue. Off is simply the
        /// unpatched instruction, so reading it must not raise.
        /// </summary>
        public bool IsZeroDefSplit() => PatchToggleOn("防0拆分");
        public bool IsMagicShieldFix() => Enabled("魔法盾修正");
        public bool IsHolyShieldMsg() => Enabled("护身触发报文a");
        public bool IsHolyShieldChance() => Enabled("护身触发概率a");
        public bool IsPoisonTimeLimit() => Enabled("中毒时间上限");
        public int PoisonTimeLimitSec() => GetParamInt("中毒时间上限_秒", 60);
        public bool IsPoisonBleed() => Enabled("中毒飘血");
        public int DualPoisonMin() => GetParamInt("双毒时间_最低", 5);
        public bool IsRedPoisonA() => Enabled("红毒_A");
        public bool IsRedPoisonB() => Enabled("红毒_B");
        public bool IsGreenPoisonA() => Enabled("绿毒_A");
        public double GreenPoisonB() => GetParam("绿毒_B", 10);
        public int GreenPoisonMin() => GetParamInt("绿毒_最低", 5);

        // ── Power/Crit toggle checks ──
        public bool IsNewMultCrit() => Enabled("新倍攻和暴击");
        public bool IsPermAttr() => Enabled("永久属性");
        public bool IsPermSpeed() => Enabled("永久攻速");
        public bool IsMoveSpeed() => Enabled("移动速度");
        public bool IsPassThrough() => Enabled("穿人穿怪");
        public bool IsSwitchCritMsg() => Enabled("切换暴击报文");

        // ── Xingyao toggle checks ──
        public bool IsXyCutting() => Enabled("星耀专属切割a");
        public bool IsXyPowerCrit() => Enabled("星耀倍功与暴击a");
        public bool IsXyReflect() => Enabled("星耀攻击反伤a");

        // ── Panggu toggle checks ──
        public bool IsPgIceStormRange() => Enabled("盘古冰咆哮的范围");
        public int PgIceStormRangeVal() => GetParamInt("盘古冰咆哮的范围_范围值", 2);
        public bool IsPgHellLightRange() => Enabled("盘古地狱雷光范围");
        public int PgHellLightRangeVal() => GetParamInt("盘古地狱雷光范围_范围值", 2);
        public bool IsPgFireRainRange() => Enabled("盘古流星火雨范围");
        public int PgFireRainRangeVal() => GetParamInt("盘古流星火雨范围_范围值", 2);
        public bool IsPgBlastFlameRange() => Enabled("盘古爆裂火焰范围");
        public int PgBlastFlameRangeVal() => GetParamInt("盘古爆裂火焰范围_范围值", 1);
        public bool IsPgKillTrigger() => Enabled("盘古击杀触发");
        public bool IsPgPhysTrigger() => Enabled("盘古物理攻击触发");
        public bool IsPgMagicTrigger() => Enabled("盘古魔法攻击触发");
        public bool IsPgWearTrigger() => Enabled("盘古穿戴触发");
        public bool IsPgKillPet() => Enabled("盘古杀死宝宝");
        public bool IsPgGiveTitle() => Enabled("盘古给与封号");
        public bool IsPgAdvancedAttr() => Enabled("盘古高级属性");

        // ── Summon toggle checks ──
        public bool IsNewCallPet() => Enabled("新呼唤宝宝");
        public bool IsCustomCall() => Enabled("自定义召唤怪物a");
        public bool IsPetEnabled() => Enabled("特殊宝宝");
        public bool IsPetSpecial() => Enabled("特殊属性");
        public bool IsPetVampire() => Enabled("宠物吸血a");
        public bool IsPetNoRest() => Enabled("禁止宝宝休息");
        public bool IsPetRebelAttr() => Enabled("宝宝叛变属性a");
        public bool IsPetAutoRebel() => Enabled("宝宝自动叛变");
        public bool IsPetDieOffline() => Enabled("下线宝宝死亡");

        // ── Hero toggle checks ──
        public bool IsHeroAutoShield() => Enabled("英雄自动开盾");
        public bool IsHeroPowerCrit() => Enabled("英雄倍攻和暴击");
        public bool IsHeroAdvancedPowerCrit() => Enabled("高级英雄倍功暴击");
        public bool IsHeroBarbarian() => Enabled("英雄野蛮");
        public bool IsHeroSpeed() => Enabled("英雄攻速移速");
        public bool IsHeroCastSpeed() => Enabled("英雄施法速度");
        /// <summary>
        /// 英雄千分比免伤：<b>减的是守方的伤害</b>，读的是守方英雄自己的
        /// <c>S(1,58)</c>。方向此前只有推断，现在有字节证据。
        ///
        /// 消费点在插件的伤害管线 <c>0x100795C0</c>（MSVC，栈参
        /// <c>[ebp+8]</c>=对象A、<c>[ebp+0xC]</c>=对象B、<c>[ebp+0x10]</c>=伤害(in/out)、
        /// <c>[ebp+0x14]</c>=A 的类、<c>[ebp+0x18]</c>=B 的类）。
        /// 收尾 <c>0x1007BFDC 8B 45 10 mov eax,[ebp+0x10]</c> ⇒ 返回值就是伤害。
        ///
        /// 定方向的锚点是同一函数开头那段**英雄格挡**：
        /// <code>
        ///   100798CD  cmp [ebp+0x14], 0x685CA0 / 0x685968 / 0x685FD8
        ///   100798EC  cmp [ebp+8], 0 / je
        ///   100798FE  8B 45 08              mov eax,[ebp+8]
        ///   10079901  8B 80 8C 06 00 00     mov eax,[eax+0x68C]    ; -> 英雄
        ///   1007992C  cmp [ebp-0xAC], 0x006AC8C8                   ; 英雄类
        ///   1007993C  push 0xE4 ; edx=1 ; call 0x10056040          ; S(英雄,1,228)
        ///   1007996A  eax=0x3E8 ; call [0x1031BCC4]                ; Random(1000)
        ///   10079983  cmp S, rand / jle skip
        ///   1007999F  68 B4 F9 2B 10        push "@HeroBlocking"
        ///   100799C5  B8 01 00 00 00        mov eax,1
        ///   100799CB  E9 0F 26 00 00        jmp 0x1007BFDF          ; 绕过取伤害，直接返 1
        /// </code>
        /// 格挡只能是守方能力，而它取的英雄来自 <c>[ebp+8]</c>，
        /// 所以 <c>[ebp+8]</c> = 守方、<c>[ebp+0xC]</c> = 攻方，
        /// <c>[ebp+0x14]</c> = 守方的类。反向佐证：<c>高级英雄倍功暴击</c>
        /// （加伤）在 <c>0x10079FF8</c> 取的是 <c>[ebp+0xC]</c> 的英雄。
        ///
        /// 公式（<c>0x1007A8A1..0x1007A95E</c>，与本键的门控
        /// <c>cmp [单例+0x108],0x1F4</c> 同一分支）：
        /// <code>
        ///   守方类 ∈ {0x685CA0, 0x685968, 0x685FD8}
        ///   hero = [守方+0x68C] ; 无英雄则跳过
        ///   v = S(hero, 1, 58) ; v &lt;= 0 跳过 ; v &gt; 1000 整个丢弃(不钳位)
        ///   damage -= (int)(damage * v / 1000.0)     ; cvttsd2si = 截断
        /// </code>
        ///
        /// <b>未落地的原因已经换了一个</b>：证据齐了，缺的是 C# 侧的载体——
        /// 脚本变量库只挂在 <c>TPlayObject</c>（<c>m_ScriptSVars</c> /
        /// <c>TryGetScriptVar</c>）上，而 <c>HeroObject : AnimalObject</c> 没有。
        /// 原生读的是英雄对象自己的 S 槽（英雄类 <c>0x006AC8C8</c> 的父链落在
        /// <c>0x0073BBE8 -&gt; 0x0073B8DC</c> 这一族，和 <c>THumanKind</c>
        /// <c>0x0073BC34</c> 同簇，所以英雄天然有 S 槽）。
        /// 退而求其次去读主号的 <c>S(1,58)</c> 会读错对象，故维持 fail-closed。
        /// </summary>
        public bool IsHeroDmgReduction() => Enabled("英雄千分比免伤");
        public bool IsHeroReadExtreme() => Enabled("英雄读取极品");
        public bool IsHeroRepairEquip() => Enabled("英雄修装备a");
        public bool IsHeroReadEquip() => Enabled("读取英雄装备");
        public bool IsHeroPhysTrigger() => Enabled("英雄物理攻击触发");
        public bool IsHeroMagicTrigger() => Enabled("英雄魔法攻击触发");
        public bool IsHeroWearTrigger() => Enabled("英雄穿戴触发");

        // ── Trigger toggle checks ──
        public bool IsAttackTrigger() => Enabled("攻击触发");
        public bool IsMagicAttackTrigger() => Enabled("魔法攻击触发");
        public bool IsSuperAttackTrigger() => Enabled("super攻击触发");
        public bool IsAdvancedPhysTrigger() => Enabled("高级物理攻击触发");
        public bool IsAdvancedMagicTrigger() => Enabled("高级魔法攻击触发");
        public bool IsBeKilledTrigger() => Enabled("被击杀触发");
        public bool IsDeathTrigger() => Enabled("死亡触发");
        public bool IsReviveScript() => Enabled("复活触发脚本");
        public bool IsPetKillTrigger() => Enabled("BB杀怪触发");
        public bool IsPetDeathTrigger() => Enabled("BB死亡触发");
        public bool IsSkillTrigger() => Enabled("技能触发脚本");
        public bool IsDmgScriptPlus() => Enabled("伤害触发脚本_plus");
        public bool IsPickupTrigger() => Enabled("捡物触发");
        public bool IsMineTrigger() => Enabled("挖矿触发");
        public bool IsLoginTrigger() => Enabled("上线触发");
        public bool IsWearTrigger() => Enabled("新穿戴触发");
        public bool IsWearPlusTrigger() => Enabled("穿戴触发_plus");
        public bool IsMindRevealTrigger() => Enabled("心灵启示触发");
        public bool IsReturnBtnTrigger() => Enabled("回城按钮触发");
        public bool IsLureTrigger() => Enabled("诱惑之光触发脚本a");
        /// <summary>
        /// 召唤神兽触发 / 召唤骷髅触发。两者是**替换**，不是前置：开关一开，
        /// 宿主的造宠函数就完全不再被调用，宝宝只能由脚本自己造出来。
        ///
        /// 安装器 <c>0x10032FD0</c> 的第 5/6 个参数是一张 dword 令牌表和令牌数
        /// （每个 dword 的低字节是一个码字节；只有 <c>0xE8</c>/<c>0xE9</c> 且不是
        /// 最后一个令牌时才吃掉下一个令牌当 rel32 重定位）。它先 VirtualAlloc
        /// 0x400 字节，把令牌铺成桩体，末尾接一条 <c>E9 &lt;rel32&gt;</c> 指向续跑点
        /// （<c>0x100331AF add ecx,[ebp+0x14]</c>），再在宿主起点写
        /// <c>E9 rel32</c> 并用 <c>0x90</c> 补到 end（end 不含，
        /// <c>0x10033267 cmp eax,[ebp+0x14] / jge</c>）。
        ///
        /// 骷髅：<c>0x100AE275 push 0x23</c>(=35 个令牌) /
        /// <c>0x100AE285 push &amp;[ebp-0x6E4]</c> / <c>0x100AE29A push 0x6EDB49</c> /
        /// <c>0x100AE2AD push 0x6EDB44</c> / <c>0x100AE2C0 push 0x6EDB44</c> /
        /// <c>0x100AE2F6 call 0x10032FD0</c>。宿主 <c>0x006EDB44 E8 B3 12 08 00
        /// call 0x76EDFC</c> 整条 5 字节被换掉，续跑 <c>0x006EDB49</c>。
        /// 令牌由 8 张 16 字节 .rdata 模板 + 3 个立即数拼出，35/35 全部有出处：
        /// <code>
        ///   60 9C 8B D3 A1 20 5D 7D 00 8B 00 8B F0 8B 7E 08
        ///   68 00 01 5D 01 6A 00 33 C9 8B C7 8B 18 FF 53 44 9D 61 E9
        /// ⇒ pushal / pushfd
        ///   mov edx,ebx                  ; ebx = 施法者(0x006EDB42 mov eax,ebx 现场)
        ///   mov eax,[0x007D5D20] / mov eax,[eax] / mov esi,eax
        ///   mov edi,[esi+8]
        ///   push 0x015D0100              ; 神兽侧是 0x03170100
        ///   push 0 / xor ecx,ecx
        ///   mov eax,edi / mov ebx,[eax] / call [ebx+0x44]   ; 宿主脚本派发槽
        ///   popfd / popal
        ///   jmp 0x006EDB49               ; 安装器补的 E9 rel32
        /// </code>
        /// 桩体里**没有任何 E8 令牌**，重定位路径一次都没触发，也就没有对
        /// <c>sub_76EDFC</c> 的调用——这就是「替换而非前置」的字节证据。
        ///
        /// 神兽：<c>0x100AE51F call 0x10032FD0</c>，宿主
        /// <c>0x006EDC5E E8 19 12 08 00 call 0x76EE7C</c>，续跑 <c>0x006EDC63</c>，
        /// 令牌表在 <c>[ebp-0x770]</c>，35 个字节与骷髅逐字节相同，只有那个
        /// <c>push imm32</c> 从 <c>0x015D0100</c> 换成 <c>0x03170100</c>。
        ///
        /// 脚本名按 SSO std::string 拼在栈上后经 <c>call 0x10033450</c> 注册：
        /// 骷髅 <c>[ebp-0x9C]=0xC</c> + <c>'@Sum','monS','kele'</c> ⇒ <c>@SummonSkele</c>；
        /// 神兽 <c>[ebp-0xF8]=0xD</c> + <c>'@Sum','monS','hins'</c> + <c>'u'</c>
        /// ⇒ <c>@SummonShinsu</c>。
        ///
        /// 仍未落地：<c>[ebx+0x44]</c> 这个宿主派发槽和那个 imm32 参数没解出来
        /// （<c>[0x007D5D20]</c> 在运行时转储里为空），C# 也没有对应的脚本入口。
        /// 生产两项都是 0，当前无可观测影响。
        /// </summary>
        public bool IsShenShouTrigger() => Enabled("召唤神兽触发");
        public bool IsKuLouTrigger() => Enabled("召唤骷髅触发");

        // ── Item/Equip toggle checks ──
        public bool IsAutoPickup() => Enabled("全屏拾取");
        public bool IsAutoRecycle() => Enabled("高级回收");
        public bool IsBindDisabled() => Enabled("禁止装备自动绑定");
        public bool IsAutoBindOff() => Enabled("屏蔽自动绑定");
        public bool IsElements() => Enabled("自定义元素");
        public bool IsItemSource() => Enabled("装备来源");
        public bool IsEquipInsurance() => Enabled("装备投保");
        public bool IsInsuranceMsg() => Enabled("投保报文");
        public bool IsMultiJob() => Enabled("装备多职业");
        public bool IsRebirthWear() => Enabled("装备转生穿戴判定a");
        public bool IsBoostDropRate() => Enabled("装备提升人物爆率");
        public double BoostDropRateA() => GetParam("装备提升人物爆率_A值", 10);
        public double BoostDropRateB() => GetParam("装备提升人物爆率_B值", 10);
        public bool IsBigBag() => Enabled("大背包");
        public bool IsTempBag() => Enabled("临时大背包");
        public bool IsPortableStorage() => Enabled("随身仓库");
        // 0x100BF6E3 `cmp [esi+0x4F0],0 / je 0x100C3EE3` gates the whole 96-slot
        // block: off restores the stock immediates rather than failing a call, so
        // this reads as a patch toggle.
        public bool IsRandomExtreme() => PatchToggleOn("随机极品");
        public bool IsGiveExtreme() => Enabled("give极品");
        // 0x100B9D3A `A2 6C FF 73 00` overwrites the imm8 of host
        // 0x0073FF69 `83 7D F4 02 cmp dword [ebp-0xc],2`. Plugin does
        // `atoi(最大装备数量); dec eax` then writes al, so the compare
        // immediate is the low 8 bits of (N-1), sign-extended by `cmp`.
        public int MaxEquipCount() => ParamAtoi("最大装备数量", 3);

        // ── Equipment stat param readers (武器/衣服/头盔/项链/手镯/戒指) ──
        // Defaults are the stock M2Server immediates the plugin overwrites,
        // recovered in docs/ys_gui_extreme_20260813.md.  A missing key and a
        // non-positive one both have to fall back to them: the apply arm is
        // `test eax,eax / jle` (0x100BFE2D), so it leaves the host untouched.
        // 武器
        public int WeaponAttrChance_Acc() => ExtremeParamInt("武器属性几率_准确_值", 24);
        public int WeaponAttrChance_Atk() => ExtremeParamInt("武器属性几率_攻击_值", 30);
        public int WeaponAttrChance_Spd() => ExtremeParamInt("武器属性几率_攻速_值", 20);
        public int WeaponAttrChance_Tao() => ExtremeParamInt("武器属性几率_道术_值", 30);
        public int WeaponAttrChance_Mgc() => ExtremeParamInt("武器属性几率_魔法_值", 30);
        public int WeaponRandExtreme() => ExtremeParamInt("武器最随机性_极品_值", 10);
        public int WeaponMaxPts_Acc() => ExtremeParamInt("武器最高点数_准确_值", 12);
        public int WeaponMaxPts_Atk() => ExtremeParamInt("武器最高点数_攻击_值", 6);
        public int WeaponMaxPts_Spd() => ExtremeParamInt("武器最高点数_攻速_值", 12);
        public int WeaponMaxPts_Tao() => ExtremeParamInt("武器最高点数_道术_值", 12);
        public int WeaponMaxPts_Mgc() => ExtremeParamInt("武器最高点数_魔法_值", 12);
        public int WeaponPtsChance_Acc() => ExtremeParamInt("武器点数几率_准确_值", 15);
        public int WeaponPtsChance_Atk() => ExtremeParamInt("武器点数几率_攻击_值", 20);
        public int WeaponPtsChance_Spd() => ExtremeParamInt("武器点数几率_攻速_值", 15);
        public int WeaponPtsChance_Tao() => ExtremeParamInt("武器点数几率_道术_值", 15);
        public int WeaponPtsChance_Mgc() => ExtremeParamInt("武器点数几率_魔法_值", 15);
        // 衣服
        public int ArmorAttrChance_Acc() => ExtremeParamInt("衣服属性几率_准确_值", 30);
        public int ArmorAttrChance_Atk() => ExtremeParamInt("衣服属性几率_攻击_值", 20);
        public int ArmorAttrChance_Spd() => ExtremeParamInt("衣服属性几率_攻速_值", 30);
        public int ArmorAttrChance_Tao() => ExtremeParamInt("衣服属性几率_道术_值", 30);
        public int ArmorAttrChance_Mgc() => ExtremeParamInt("衣服属性几率_魔法_值", 20);
        public int ArmorRandExtreme() => ExtremeParamInt("衣服最随机性_极品_值", 10);
        public int ArmorMaxPts_Acc() => ExtremeParamInt("衣服最高点数_准确_值", 6);
        public int ArmorMaxPts_Atk() => ExtremeParamInt("衣服最高点数_攻击_值", 6);
        public int ArmorMaxPts_Spd() => ExtremeParamInt("衣服最高点数_攻速_值", 6);
        public int ArmorMaxPts_Tao() => ExtremeParamInt("衣服最高点数_道术_值", 6);
        public int ArmorMaxPts_Mgc() => ExtremeParamInt("衣服最高点数_魔法_值", 6);
        public int ArmorPtsChance_Acc() => ExtremeParamInt("衣服点数几率_准确_值", 20);
        public int ArmorPtsChance_Atk() => ExtremeParamInt("衣服点数几率_攻击_值", 20);
        public int ArmorPtsChance_Spd() => ExtremeParamInt("衣服点数几率_攻速_值", 20);
        public int ArmorPtsChance_Tao() => ExtremeParamInt("衣服点数几率_道术_值", 20);
        public int ArmorPtsChance_Mgc() => ExtremeParamInt("衣服点数几率_魔法_值", 20);
        // 头盔
        public int HelmetAttrChance_Acc() => ExtremeParamInt("头盔属性几率_准确_值", 30);
        public int HelmetAttrChance_Atk() => ExtremeParamInt("头盔属性几率_攻击_值", 20);
        public int HelmetAttrChance_Spd() => ExtremeParamInt("头盔属性几率_攻速_值", 30);
        public int HelmetAttrChance_Tao() => ExtremeParamInt("头盔属性几率_道术_值", 30);
        public int HelmetAttrChance_Mgc() => ExtremeParamInt("头盔属性几率_魔法_值", 20);
        public int HelmetRandExtreme() => ExtremeParamInt("头盔最随机性_极品_值", 10);
        public int HelmetMaxPts_Acc() => ExtremeParamInt("头盔最高点数_准确_值", 6);
        public int HelmetMaxPts_Atk() => ExtremeParamInt("头盔最高点数_攻击_值", 6);
        public int HelmetMaxPts_Spd() => ExtremeParamInt("头盔最高点数_攻速_值", 6);
        public int HelmetMaxPts_Tao() => ExtremeParamInt("头盔最高点数_道术_值", 6);
        public int HelmetMaxPts_Mgc() => ExtremeParamInt("头盔最高点数_魔法_值", 6);
        public int HelmetPtsChance_Acc() => ExtremeParamInt("头盔点数几率_准确_值", 20);
        public int HelmetPtsChance_Atk() => ExtremeParamInt("头盔点数几率_攻击_值", 20);
        public int HelmetPtsChance_Spd() => ExtremeParamInt("头盔点数几率_攻速_值", 20);
        public int HelmetPtsChance_Tao() => ExtremeParamInt("头盔点数几率_道术_值", 20);
        public int HelmetPtsChance_Mgc() => ExtremeParamInt("头盔点数几率_魔法_值", 20);
        // 项链
        public int NecklaceAttrChance_Acc() => ExtremeParamInt("项链属性几率_准确_值", 30);
        public int NecklaceAttrChance_Atk() => ExtremeParamInt("项链属性几率_攻击_值", 40);
        public int NecklaceAttrChance_Spd() => ExtremeParamInt("项链属性几率_攻速_值", 30);
        public int NecklaceAttrChance_Tao() => ExtremeParamInt("项链属性几率_道术_值", 30);
        public int NecklaceAttrChance_Mgc() => ExtremeParamInt("项链属性几率_魔法_值", 40);
        public int NecklaceRandExtreme() => ExtremeParamInt("项链最随机性_极品_值", 10);
        public int NecklaceMaxPts_Acc() => ExtremeParamInt("项链最高点数_准确_值", 6);
        public int NecklaceMaxPts_Atk() => ExtremeParamInt("项链最高点数_攻击_值", 6);
        public int NecklaceMaxPts_Spd() => ExtremeParamInt("项链最高点数_攻速_值", 6);
        public int NecklaceMaxPts_Tao() => ExtremeParamInt("项链最高点数_道术_值", 6);
        public int NecklaceMaxPts_Mgc() => ExtremeParamInt("项链最高点数_魔法_值", 6);
        public int NecklacePtsChance_Acc() => ExtremeParamInt("项链点数几率_准确_值", 20);
        public int NecklacePtsChance_Atk() => ExtremeParamInt("项链点数几率_攻击_值", 20);
        public int NecklacePtsChance_Spd() => ExtremeParamInt("项链点数几率_攻速_值", 20);
        public int NecklacePtsChance_Tao() => ExtremeParamInt("项链点数几率_道术_值", 20);
        public int NecklacePtsChance_Mgc() => ExtremeParamInt("项链点数几率_魔法_值", 20);
        // 手镯
        public int BraceletAttrChance_Acc() => ExtremeParamInt("手镯属性几率_准确_值", 30);
        public int BraceletAttrChance_Atk() => ExtremeParamInt("手镯属性几率_攻击_值", 20);
        public int BraceletAttrChance_Spd() => ExtremeParamInt("手镯属性几率_攻速_值", 30);
        public int BraceletAttrChance_Tao() => ExtremeParamInt("手镯属性几率_道术_值", 30);
        public int BraceletAttrChance_Mgc() => ExtremeParamInt("手镯属性几率_魔法_值", 20);
        public int BraceletRandExtreme() => ExtremeParamInt("手镯最随机性_极品_值", 10);
        public int BraceletMaxPts_Acc() => ExtremeParamInt("手镯最高点数_准确_值", 6);
        public int BraceletMaxPts_Atk() => ExtremeParamInt("手镯最高点数_攻击_值", 6);
        public int BraceletMaxPts_Spd() => ExtremeParamInt("手镯最高点数_攻速_值", 6);
        public int BraceletMaxPts_Tao() => ExtremeParamInt("手镯最高点数_道术_值", 6);
        public int BraceletMaxPts_Mgc() => ExtremeParamInt("手镯最高点数_魔法_值", 6);
        public int BraceletPtsChance_Acc() => ExtremeParamInt("手镯点数几率_准确_值", 20);
        public int BraceletPtsChance_Atk() => ExtremeParamInt("手镯点数几率_攻击_值", 20);
        public int BraceletPtsChance_Spd() => ExtremeParamInt("手镯点数几率_攻速_值", 20);
        public int BraceletPtsChance_Tao() => ExtremeParamInt("手镯点数几率_道术_值", 20);
        public int BraceletPtsChance_Mgc() => ExtremeParamInt("手镯点数几率_魔法_值", 20);
        // 戒指
        public int RingAttrChance_Acc() => ExtremeParamInt("戒指属性几率_准确_值", 30);
        public int RingAttrChance_Atk() => ExtremeParamInt("戒指属性几率_攻击_值", 20);
        public int RingAttrChance_Spd() => ExtremeParamInt("戒指属性几率_攻速_值", 30);
        public int RingAttrChance_Tao() => ExtremeParamInt("戒指属性几率_道术_值", 30);
        public int RingAttrChance_Mgc() => ExtremeParamInt("戒指属性几率_魔法_值", 20);
        public int RingRandExtreme() => ExtremeParamInt("戒指最随机性_极品_值", 9);
        public int RingMaxPts_Acc() => ExtremeParamInt("戒指最高点数_准确_值", 6);
        public int RingMaxPts_Atk() => ExtremeParamInt("戒指最高点数_攻击_值", 6);
        public int RingMaxPts_Spd() => ExtremeParamInt("戒指最高点数_攻速_值", 6);
        public int RingMaxPts_Tao() => ExtremeParamInt("戒指最高点数_道术_值", 6);
        public int RingMaxPts_Mgc() => ExtremeParamInt("戒指最高点数_魔法_值", 6);
        public int RingPtsChance_Acc() => ExtremeParamInt("戒指点数几率_准确_值", 20);
        public int RingPtsChance_Atk() => ExtremeParamInt("戒指点数几率_攻击_值", 20);
        public int RingPtsChance_Spd() => ExtremeParamInt("戒指点数几率_攻速_值", 20);
        public int RingPtsChance_Tao() => ExtremeParamInt("戒指点数几率_道术_值", 20);
        public int RingPtsChance_Mgc() => ExtremeParamInt("戒指点数几率_魔法_值", 20);

        // ── System toggle checks ──
        public bool IsAddLimLF() => Enabled("AddLimLF函数修改");
        public bool IsIncActivePoint() => Enabled("IncActivePoint函数修改");
        public bool IsServerSay() => Enabled("ServerSay函数");
        public bool IsNoKillMapLv() => Enabled("SetNoKillMapLv脚本触发");
        public bool IsSetTitle() => Enabled("设置玩家称号函数");
        public bool IsYanshenSpecial() => Enabled("眼神特殊函数");
        public bool IsCdMs() => Enabled("毫秒级cd记录");
        public bool IsLoopTimer() => Enabled("全局循环函数");
        public bool IsHideGoldLog() => Enabled("屏蔽元宝数据库日志");
        public bool IsHideGoldMsg() => Enabled("屏蔽元宝增减信息");
        public bool IsHideAttrUp() => Enabled("屏蔽属性提升提示");
        public bool IsHideRank() => Enabled("屏蔽排行榜");
        // 屏蔽发言频繁禁言功能 is a host-code patch, same primitive as 复活戒指重设.
        // Plugin 0x100AC678 / 0x100AC6B2 memcpy 6×90 over the two flood-counter
        // increments in ProcessSayMsg (sub_6BB2F8):
        //   0x6BB56A  FF 83 74 0A 00 00  inc dword [ebx+0xA74]  → m_nSayMsgCount++
        //   0x6BB579  FE 83 82 06 00 00  inc byte  [ebx+0x682]  → m_btSayRapidCount++
        // Restore arm 0x100AC75D / 0x100AC797 writes the incs back. Thresholds
        // (>=2 / >=5), the 60 s mute, and the decay decs are not patched.
        public bool IsBlockSpamPatchOn() => PatchToggleOn("屏蔽发言频繁禁言功能");
        public bool IsBlockSpam() => IsBlockSpamPatchOn();
        public bool IsDelSkillSilent() => Enabled("删除技能不提示");
        public bool IsDelHeroSkill() => Enabled("删除英雄技能");
        // 升级技能不提示 is a host-code patch: plugin 0x100DB61C memcpy EB 3A 90 90
        // over 0x73F5EE in sub_73F500 (ChgSelfSkillLv / UpUserSkill worker), jumping
        // from the LStrCatN of "{name} 技能等级变更为：{level}" + SysMsg 0xFFDB
        // straight to RecalcAbilitys at 0x73F62A. Restore arm writes 57 68 7C F6 73 00.
        public bool IsUpSkillSilentPatchOn() => PatchToggleOn("升级技能不提示");
        public bool IsUpSkillSilent() => IsUpSkillSilentPatchOn();
        public bool IsBanChatSilent() => Enabled("禁止发言不提示");
        public bool IsNameColor() => Enabled("名字变色");
        public bool IsLevelMute() => Enabled("等级禁言");
        public bool IsMailAntiSpam() => Enabled("邮件防刷");
        public bool IsPlayerDropRate() => PatchToggleOn("人物爆率调整");
        // 人物等级1..3_值 挨着 人物爆率调整 只是行文顺序，它们**不是**爆率的参数：
        // 加载器把三个键写进页面对象 0x66C/0x670/0x674，而这三格连同名字/数量六格
        // 一起在「修改召唤神兽」的 ON 分支里被搬进单例（0x100BA3F7/0x100BA40E/
        // 0x100BA425 -> [单例+0x86C/0x870/0x874]），GUI 对话框里也在同一个
        // 「修改召唤神兽」框内（0x00030BEA/BEC/BEE 三个「人物等级:」标签）。
        // 默认值来自构造函数 0x100B74AD "42" / 0x100B74F5 "45" / 0x100B753D "48"；
        // 原来的 35/40 没有出处。生产 config.json 是 40/45/48。
        public int PlayerLv1() => GetParamInt("人物等级1_值", 42);
        public int PlayerLv2() => GetParamInt("人物等级2_值", 45);
        public int PlayerLv3() => GetParamInt("人物等级3_值", 48);
        public bool IsScriptDropRate() => Enabled("脚本控制人物爆率");
        public bool IsScriptHair() => Enabled("脚本控制头发外显");
        public bool IsNewMonsterDrop() => Enabled("新怪物爆率");
        public bool IsGetCastle() => Enabled("获取沙城归属");
        // 行会显示 is a host-code patch: plugin 0x100AACD8 / 0x100AAD29 memcpy
        // 90 90 over both skip-jumps in GetShowName's non-castle guild branch
        //   0x6C5BCB  74 49  je 0x6C5C16   (after cmp g_Config.boShowGuildName)
        //   0x6C5BF7  74 1D  je 0x6C5C16   (after castle-war-area test)
        // Restore arm 0x100AADC0 / 0x100AADFA writes 74 49 / 74 1D back.
        // Both je gone ⇒ every path reaches 0x6C5BF9 and emits %guildname/%rankname.
        public bool IsGuildShow() => PatchToggleOn("行会显示");
        public bool IsMultiFaction() => Enabled("角色多阵营");
        public bool IsSiegeScript() => Enabled("攻沙脚本控制");
        public bool IsSiegeModify() => Enabled("攻城修改");
        public bool IsSiegeDuration() => Enabled("攻城时长_分钟");
        public bool IsSiegeModMinute() => Enabled("攻城修改_分钟");
        public bool IsSiegeModDay() => Enabled("攻城修改_天数");
        public bool IsSiegeModHour() => Enabled("攻城修改_小时");
        public bool IsKillNotice() => Enabled("全服击杀提示");
        public bool IsVacuum() => Enabled("全屏吸怪");
        public bool IsKickPlayer() => Enabled("踢玩家下线");
        public bool IsSafeNoDrop() => Enabled("安全区禁止丢物");
        public bool IsFloorItemTimeout() => Enabled("地面物品消失时间");
        public int FloorItemTimeoutSec() => GetParamInt("地面物品消失时间_时间", 300);

        // ── Trade/Stall toggle checks ──
        public bool IsStallPass() => Enabled("摆摊穿人");
        // 关闭摆摊 is a host-code patch: plugin 0x100AD12A memcpy C3 over the
        // first byte of CM_START_STALL (4424) at 0x6E7C38 (native 55 = push ebp).
        // Restore arm 0x100AD1AE writes 55 back. SetTimeLevel (4419) is a
        // different function and is not patched.
        public bool IsCloseStall() => PatchToggleOn("关闭摆摊");
        public bool IsTuChengStall() => Enabled("土城摆摊");
        public bool IsLimitStall() => Enabled("限制摆摊");
        public int LimitStall_LeftX() => GetParamInt("限制摆摊_左x", 280);
        public int LimitStall_LeftY() => GetParamInt("限制摆摊_左y", 328);
        public int LimitStall_RightX() => GetParamInt("限制摆摊_右x", 340);
        public int LimitStall_RightY() => GetParamInt("限制摆摊_右y", 340);
        public int LimitStall_Level() => GetParamInt("限制摆摊_等级", 50);
        public bool IsMapStall() => Enabled("指定地图编号摆摊");
        public int MapStallMap() => GetParamInt("摆摊地图", 3);
        public bool IsBanTradeMap() => Enabled("禁止交易地图");

        // ── Ring toggle checks ──
        public bool IsReviveCD() => Enabled("复活戒指改cd");
        public bool IsReviveChance() => Enabled("复活戒指概率");
        public bool IsReviveReset() => Enabled("复活戒指重设");
        public bool IsReviveImmune() => Enabled("复活戒指重设_无敌时间");
        public bool IsReviveResetTime() => Enabled("复活戒指重设_重设时间");

        // ── Combo toggle checks ──
        public bool IsWarriorCombo() => Enabled("战士合击");
        public double WarriorComboV1() => GetParam("战士合击_数值1");
        public double WarriorComboV2() => GetParam("战士合击_数值2");
        public double WarriorComboV3() => GetParam("战士合击_数值3");
        public double WarriorComboV4() => GetParam("战士合击_数值4");
        public double WarriorComboV5() => GetParam("战士合击_数值5");
        public bool IsWizTaoCombo() => Enabled("法道合击");
        public double WizTaoComboV1() => GetParam("法道合击_数值1");
        public double WizTaoComboV2() => GetParam("法道合击_数值2");
        public double WizTaoComboV3() => GetParam("法道合击_数值3");
        public double WizTaoComboV4() => GetParam("法道合击_数值4");
        public double WizTaoComboV5() => GetParam("法道合击_数值5");
        public bool IsTaoComboFactor() => Enabled("道士合击系数");
        public double TaoComboV1() => GetParam("道士合击系数_数值1");
        public double TaoComboV2() => GetParam("道士合击系数_数值2");
        public double TaoComboV3() => GetParam("道士合击系数_数值3");
        public double TaoComboV4() => GetParam("道士合击系数_数值4");
        public double TaoComboV5() => GetParam("道士合击系数_数值5");

        // ── Misc toggle checks ──
        public bool IsLevelBreak() => Enabled("技能等级突破");
        public bool IsLevelBreakMax() => Enabled("技能等级突破_最大值");
        public bool IsMainGlobalSpeed() => Enabled("主号全局法速");
        public bool IsMainCastSpeed() => Enabled("主号施法速度");
        public bool IsMainAdvCrit() => Enabled("主号高级暴击");
        public bool IsMainClone() => Enabled("主号分身术a");
        public bool IsChangeJob() => Enabled("专职变性");
        public bool IsGamePartnerLimit() => Enabled("战队职业限制");
        public bool IsSelfDmg() => Enabled("自定义伤害");
        public bool IsSelfDmgPlus() => Enabled("自定义伤害_plus");
        public bool IsCustomDmg() => Enabled("自定义伤害");
        public bool IsCustomDmgPlus() => Enabled("自定义伤害_plus");

        // ── Monster toggle checks ──
        //
        // 怪物名字1..3_值 / 怪物数量1..3_值 与 人物等级1..3_值 合起来是
        // 「修改召唤神兽」的九个参数，三档各一行 (人物等级, 怪物名字, 怪物数量)。
        // 九个键在同一个配置加载器里连续读出，名字走 asString、等级/数量走
        // asInt（0x100DFE40），落在页面对象的连续偏移上：
        //   0x100BB7D3 push "人物等级1_值" -> 0x100BB84F lea ecx,[esi+0x66C]
        //   0x100BB86E push "人物等级2_值" -> 0x100BB8E7 lea ecx,[esi+0x670]
        //   0x100BB906 push "人物等级3_值" -> 0x100BB97F lea ecx,[esi+0x674]
        //   0x100BB99E push "怪物名字1_值" -> 0x100BBA62 lea ecx,[esi+0x678]
        //   0x100BBA72 push "怪物名字2_值" -> 0x100BBB36 lea ecx,[esi+0x67C]
        //   0x100BBB46 push "怪物名字3_值" -> 0x100BBC0F lea ecx,[esi+0x680]
        //   0x100BBC1F push "怪物数量1_值" -> 0x100BBC9F lea ecx,[esi+0x684]
        //   0x100BBCBE push "怪物数量2_值" -> 0x100BBD33 lea ecx,[esi+0x688]
        //   0x100BBD52 push "怪物数量3_值" -> 0x100BBDC7 lea ecx,[esi+0x68C]
        // 打补丁时再经 atoi(0x1022DC49) / std::string::assign(0x10018750) 搬进单例：
        //   0x100BA3F7/0x100BA40E/0x100BA425 -> [单例+0x86C/0x870/0x874]  等级
        //   0x100BA445/0x100BA46A/0x100BA49B -> [单例+0x878/0x890/0x8A8]  名字
        //   0x100BA3B2/0x100BA3C9/0x100BA3E0 -> [单例+0x8C0/0x8C4/0x8C8]  数量
        // 上一轮把 0x8C0/0x8C4/0x8C8 记成「页面对象偏移」是把两个对象混了
        // （REPLICATION_RULES §4.22 的撞车坑），这里按实际基址寄存器订正。
        //
        // 默认值取自页面对象构造函数 0x100B7400..0x100B7710，每个字段的
        // `lea esi,[edi+off]` 之后紧跟一次 `push <默认串> / call 0x100107D0`
        // （空则 `call 0x1000BD60` 赋默认）：
        //   0x66C "42"  0x670 "45"  0x674 "48"
        //   0x678 "神兽" 0x67C "白虎" 0x680 "月灵"
        //   0x684 "2"   0x688 "2"   0x68C "2"
        // 原来这里填的是生产 config.json 的实测值（强化神兽/强化神兽/白虎、1/2/2），
        // 那是「这台服务器现在填了什么」，不是键缺失时原生会取什么。
        public string MonsterName1() => ParamS("怪物名字1_值", "神兽");
        public string MonsterName2() => ParamS("怪物名字2_值", "白虎");
        public string MonsterName3() => ParamS("怪物名字3_值", "月灵");

        /// <summary>
        /// 「怪物数量1_值」是数量，不是开关。原先的 <c>Enabled("怪物数量1_值")</c>
        /// 方向就是错的：数量 0 会被读成「关」，任何非 0 数量都读成「开」，
        /// 而它的兄弟键 <c>怪物数量2_值</c>/<c>怪物数量3_值</c> 在同一段加载器里
        /// 走的是同一个 <c>asInt</c>（<c>0x100DFE40</c>），C# 侧已经按 int 建模。
        /// 生产 <c>config.json</c> 三档实测是 1 / 2 / 1。
        /// 与 YS-SW-C1 修掉的 <c>IsShenShouCount()</c>/<c>IsKuLouCount()</c> 同一类缺陷。
        /// </summary>
        public int MonsterCount1() => GetParamInt("怪物数量1_值", 2);
        public int MonsterCount2() => GetParamInt("怪物数量2_值", 2);
        public int MonsterCount3() => GetParamInt("怪物数量3_值", 2);

        /// <summary>
        /// 「修改召唤神兽」：按人物等级挑一档，覆盖神兽的名字与数量。
        /// 命中返回 true 并写出 <paramref name="name"/> / <paramref name="count"/>；
        /// 一档都不满足则返回 false，调用方保持主干算出来的值不变。
        ///
        /// 原生不是改常量，是在神兽生成器 <c>sub_76EE7C</c> 上装两段 detour
        /// （安装器 <c>0x10032B10</c>，<c>0x10032C00</c> 的 Themida 虚拟化孪生体，
        /// 同为 <c>ret 0x10</c> 的 4 参 stdcall；写 <c>E9 rel32</c> 并用 <c>0x90</c>
        /// 补到 end，end 不含）：
        /// <code>
        ///   0x100BA4CC push 0x100B7DE0 / push 0x76EE9F / push 0x76EE98 / push 0x76EE98
        ///   0x100BA4E0 call 0x10032B10
        ///     -> 改写 0x0076EE98 起 7 字节 `6A 01 68 00 2F 0D 00`
        ///        = `push 1`(数量) + `push 0xD2F00`(叛变秒数)，续跑 0x0076EE9F
        ///   0x100BA4E5 push 0x100B7EA0 / push 0x76EEB4 / push 0x76EEAF / push 0x76EEAF
        ///   0x100BA4F9 call 0x10032B10
        ///     -> 改写 0x0076EEAF 起 5 字节 `BA EC EE 76 00`
        ///        = `mov edx,0x76EEEC`(名字指针)，续跑 0x0076EEB4
        /// </code>
        /// 门控 <c>0x100BA0C3 cmp [edi+0x710],0</c>，日志落点
        /// <c>0x100BA504 push "修改召唤神兽(已启动)"</c> /
        /// <c>0x100BA511 push "修改召唤神兽(未启动)"</c>。
        ///
        /// 两条关键的取值域差异，与 <c>召唤神兽</c>(<c>神兽_数量</c>/<c>神兽_序号</c>)**相反**：
        /// 那一套是定长 blob 覆盖，所以数量受 imm8 上钳 127、名字恒两个汉字；
        /// 这一套整条 <c>push</c>/<c>mov edx</c> 都被换掉了，数量是完整 dword、
        /// 名字是指针，长度不受限——生产实测就填了四个汉字的「强化神兽」。
        /// 所以这里既不钳 127 也不截两字。
        ///
        /// 两段补丁区间与 <c>神兽_数量</c> 的 <c>0x0076EE99</c>、
        /// <c>神兽_序号</c> 的 <c>0x0076EEEC</c> 是重叠/失效关系
        /// （<c>0x0076EE99</c> 就落在 <c>E9 rel32</c> 里面），所以两套开关同开时
        /// 只有本键生效。生产 <c>召唤神兽=0</c> / <c>修改召唤神兽=1</c>，不冲突。
        ///
        /// 全镜像扫描 <c>0x0076EE7C..0x0076EF00</c> 的每个 imm32 引用：
        /// <c>0x76EE98/0x76EE9F/0x76EEAF/0x76EEB4</c> 只被上面两处安装调用引用，
        /// **没有任何还原支**——这是一次性安装，关掉开关不会写回宿主原字节。
        /// </summary>
        public bool TryGetModifyShenShou(int humanLevel, out string name, out int count)
        {
            name = null;
            count = 0;
            if (!IsModifyShenShou()) return false;
            // 三档阈值升序（构造函数 42/45/48，生产 40/45/48），逐档比较、后命中的
            // 覆盖先命中的，即「取满足条件的最高一档」——与主干 DragonArray 的
            // 遍历形状一致。一档都不满足时不改动主干的取值。
            var hit = false;
            if (humanLevel >= PlayerLv1()) { name = MonsterName1(); count = MonsterCount1(); hit = true; }
            if (humanLevel >= PlayerLv2()) { name = MonsterName2(); count = MonsterCount2(); hit = true; }
            if (humanLevel >= PlayerLv3()) { name = MonsterName3(); count = MonsterCount3(); hit = true; }
            if (!hit || string.IsNullOrEmpty(name))
            {
                name = null;
                count = 0;
                return false;
            }
            return true;
        }
        public bool IsMonsterDropA() => Enabled("怪物爆率A_值");
        public bool IsMonsterDropB() => Enabled("怪物爆率B_值");
        public bool IsMonsterDropK() => Enabled("怪物爆率K_值");

        // ── Red/Green name K值 params (patch immediates, not locale parse) ──
        // 0x100B9CCC `A3 BB FC 73 00` -> imm32 of 0x0073FCB8
        // `C7 45 F8 15 00 00 00 mov dword [ebp-8],0x15`. Full dword, no wrap.
        public int RedNameK() => ParamAtoi("红名K值", 21);
        // 0x100B9C5E `A2 C9 FC 73 00` -> imm8 of 0x0073FCC7
        // `83 C0 5A add eax,0x5A`. `add eax,imm8` sign-extends, so A
        // survives only as a signed byte. No `cmp 0x7F` clamp before the write.
        public int NormalK() =>
            unchecked((sbyte)ParamAtoi("非红名K值", 90));

        // ── Loop time ──
        public bool IsLoopTimeVal() => Enabled("循环时间_值");

        // ═══════════════════════════════════════════════════════════════
        // 7.1 Missing method stubs (reverse-engineered from common.pas / NpcFuc.pas)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>给物品并带绑定状态</summary>
        public int GiveItemBind(string itemName, int count, int bindFlag)
        {
            if (!Enabled("屏蔽自动绑定")) return 0;
            if (_npc == null) return 0;
            if (M2Share.UserEngine.GetStdItemIdx(itemName) <= 0) return 0;
            int given = 0;
            for (int i = 0; i < count && _player.IsEnoughBag(); i++)
            {
                var userItem = new TUserItem();
                if (M2Share.UserEngine.CopyToUserItemFromName(itemName, ref userItem))
                {
                    if (bindFlag > 0) userItem.btValue[8] = 1;
                    _player.m_ItemList.Add(userItem);
                    _player.SendAddItem(userItem);
                    given++;
                }
            }
            return given;
        }

        /// <summary>装备投保操作 (检查"装备投保" toggle)</summary>
        public int InsuranceItem(int bodyPos, int insureVal)
        {
            if (!Enabled("装备投保")) return 0;
            return EquipInsurance(bodyPos, insureVal);
        }

        /// <summary>发送投保报文给客户端</summary>
        public int SendInsuranceMsg(int roleId, string msg)
        {
            if (!Enabled("投保报文")) return 0;
            // Send message to self (the script's player context)
            _player.SysMsg(msg, MsgColor.Green, MsgType.Hint);
            return roleId;
        }

        /// <summary>大背包切换 — 支持临时大背包</summary>
        public int SwitchBigBag(string bagName, int isTemp)
        {
            if (isTemp != 0 && !Enabled("临时大背包")) return 0;
            if (isTemp == 0 && !Enabled("大背包")) return 0;
            if (_npc != null) _npc.GotoLable_GiveItem(_player, bagName, 1);
            return 1;
        }

        /// <summary>安全区禁止丢物检查 (应在物品丢弃前调用)</summary>
        public bool IsDropAllowed()
        {
            if (!Enabled("安全区禁止丢物")) return true;
            // If safe-zone no-drop is ON (1), check if player is in safe zone
            if (_player.m_PEnvir != null && _player.m_PEnvir.Flag.boSAFE)
                return false;
            if (_player.InSafeZone())
                return false;
            return true;
        }

        /// <summary>全身装备修复 (英雄修装备)</summary>
        public int RepairAllEquip(int isHero)
        {
            if (isHero != 0 && !Enabled("英雄修装备a")) return 0;
            int count = 0;
            var items = _player.m_UseItems;
            foreach (var item in items)
            {
                if (item != null && item.Dura < item.DuraMax)
                {
                    item.Dura = item.DuraMax;
                    _player.SendUpdateItem(item);
                    count++;
                }
            }
            return count;
        }

        /// <summary>屏蔽元宝增减消息 (在发送元宝变化消息前调用)</summary>
        public bool ShouldHideGoldMsg() => Enabled("屏蔽元宝增减信息");
        /// <summary>屏蔽元宝数据库日志 (在记录元宝日志前调用)</summary>
        public bool ShouldHideGoldLog() => Enabled("屏蔽元宝数据库日志");
        /// <summary>屏蔽属性提升提示</summary>
        public bool ShouldHideAttrUp() => Enabled("屏蔽属性提升提示");
        /// <summary>屏蔽排行榜</summary>
        public bool ShouldHideRank() => Enabled("屏蔽排行榜");
        /// <summary>禁止发言不提示</summary>
        public bool ShouldBanChatSilent() => Enabled("禁止发言不提示");
        /// <summary>删除技能不提示</summary>
        public bool ShouldDelSkillSilent() => Enabled("删除技能不提示");
        /// <summary>升级技能不提示</summary>
        public bool ShouldUpSkillSilent() => Enabled("升级技能不提示");

        /// <summary>禁止玩家交易地图检查</summary>
        public bool IsTradeBanned()
        {
            if (!Enabled("禁止交易地图")) return false;
            return _player?.m_PEnvir?.sMapName?.Length == 15;
        }

        /// <summary>
        /// 禁止在地图名满 15 字的地图里切换宝宝休息状态。
        ///
        /// 眼神在宿主 <c>0x00623A73</c>（原字节 <c>80 B0 C7 04 00 00 01</c>
        /// = <c>xor byte [eax+0x4C7],1</c>，即休息标志的翻转）装 trampoline，
        /// 续跑点 <c>0x00623A7A</c>，安装点 <c>0x100AABB6 call 0x10032FD0</c>，
        /// 门控 <c>0x100AAB35 cmp [edi+0x948],0 / je</c>。
        /// 桩体模板存在 .rdata，每个 dword 存一个码字节
        /// （<c>0x102D1700 / 0x102D2940 / 0x102D33B0 / 0x102D16C0</c> 各 4 个 +
        /// <c>0x100AAB6C mov dword [ebp-0x4A4],0xE9</c>），拼出 17 字节：
        /// <code>
        ///   80 B8 15 01 00 00 0F   cmp byte [eax+0x115], 0x0F
        ///   74 07                  je  skip
        ///   80 B0 C7 04 00 00 01   xor byte [eax+0x4C7], 1     ← 原指令
        ///   E9 &lt;rel32&gt;             jmp 0x00623A7A
        /// </code>
        /// <c>[obj+0x115]</c> 是 <c>m_sMapName: string[15]</c>（Delphi ShortString，
        /// 长度字节就在 +0x115）：<c>0x006AFD1E lea eax,[ebx+0x115]</c> /
        /// <c>0x006AFD27 mov cl,0x0F</c> / <c>0x006AFD29 call 0x004039E4</c> 之后
        /// 紧接着写 <c>[ebx+0x12C]=CurrX</c>、<c>[ebx+0x130]=CurrY</c>。
        ///
        /// 因为赋值时按 <c>cl = 15</c> 截断，长度字节等于 15 的充要条件是
        /// **原地图名长度 &gt;= 15**，不是恰好等于 15。
        /// </summary>
        public bool IsPetRestBlocked()
        {
            if (!Enabled("禁止宝宝休息")) return false;
            return _player?.m_PEnvir?.sMapName?.Length >= 15;
        }

        /// <summary>限制摆摊区域检查 (返回true=允许摆摊)</summary>
        public bool IsStallAllowed()
        {
            if (!Enabled("限制摆摊")) return true;
            if (_player.m_Abil.Level < LimitStall_Level()) return false;
            int x = _player.m_nCurrX, y = _player.m_nCurrY;
            int lx = LimitStall_LeftX(), ly = LimitStall_LeftY();
            int rx = LimitStall_RightX(), ry = LimitStall_RightY();
            return x >= lx && x <= rx && y >= ly && y <= ry;
        }

        /// <summary>关闭摆摊检查</summary>
        public bool IsStallClosed() => IsCloseStall();

        /// <summary>指定地图编号摆摊</summary>
        public int GetStallMapId() => GetParamInt("摆摊地图", 3);

        /// <summary>检查是否允许摆摊穿人</summary>
        public bool IsStallPassThrough() => Enabled("摆摊穿人");

        /// <summary>物品掉落时检查地面物品消失时间</summary>
        public int GetFloorItemTimeout()
        {
            return TryGetFloorItemTimeout(out var timeoutMilliseconds)
                ? timeoutMilliseconds
                : 0;
        }

        public bool TryGetFloorItemTimeout(out int timeoutMilliseconds)
        {
            timeoutMilliseconds = 0;
            if (!PatchToggleOn("地面物品消失时间")) return false;

            // 600 was the M2Server constant (0x77A3FD cmp edx,0x927C0), not the plugin's
            // fallback: when the key is absent the loader seeds 300 seconds
            // (0x100B01AA C7 80 00 0D 00 00 2C 01 00 00 -> mov [cfg+0xD00],0x12C).
            // Enable arm 0x100AAF86 imul edx,eax,0x3E8 then memcpy 4 bytes to 0x77A3FF.
            var seconds = Math.Max(0, GetParamInt("地面物品消失时间_时间", 300));
            timeoutMilliseconds = seconds > int.MaxValue / 1000
                ? int.MaxValue
                : seconds * 1000;
            return true;
        }

        /// <summary>全服击杀提示发送</summary>
        public bool ShouldSendKillNotice() => Enabled("全服击杀提示");

        /// <summary>多职业装备检查</summary>
        public bool IsMultiJobEquip() => Enabled("装备多职业");

        /// <summary>装备转生穿戴判定</summary>
        public bool IsRebirthWearCheck() => Enabled("装备转生穿戴判定a");

        /// <summary>角色多阵营检查</summary>
        public bool IsMultiFactionEnabled() => Enabled("角色多阵营");

        /// <summary>Email防刷检查</summary>
        public bool IsMailAntiSpamEnabled() => Enabled("邮件防刷");

        // 等级禁言没有"等级阈值"这个量。开关本身是 config.json 顶层的单个整数键
        // "等级禁言"（生产 D:\光头卧龙\mud2.0\Mir200\Gs1\config.json 取 1，全表 380 键
        // 里没有任何 "等级禁言_*" 参数键），禁言状态整个落在 S(1,1) 上：7=禁言、
        // 8=解除禁言（面板原文见 YanshenLegacy23ReplicaPanels.cs 的说明）。生产
        // LogonQuest.pas 的 49 处调用只写 7 和 8，等级判断由脚本自己做（注释
        // 「已升至[15]级解除禁言」用的是 15）。此前这里把 SetS(1,1,7) 里的 7 读成
        // 了"7 级以下禁言"的等级阈值并硬编码返回，属凭空发明；S(1,1) 各模式的实际
        // 效果无字节证据（插件加壳），按 fail-closed 不实现。
        // 开关本身仍可由 IsLevelMute() 读取。

        /// <summary>
        /// 人物爆率调整 is a code-patch override of THumanKind's equip-drop
        /// worker <c>sub_73FC70</c>, not a runtime multiplier. Gated by
        /// <c>[edi+0x5D0]</c> at 0x100B9BBA (that slot is 人物爆率调整:
        /// loader 0x100BABEC stores the converted toggle there). Off means
        /// the host immediates stay at stock 21 / 90 / 2.
        /// </summary>
        public bool TryGetDeathEquipDropPatch(bool redName, out int denominator, out int capImm)
        {
            denominator = 0;
            capImm = 2;
            if (!PatchToggleOn("人物爆率调整")) return false;
            // Red path is a bare imm32 (0x73FCB8). Non-red is
            // `[esi+0x18c] + imm8` (0x73FCC1/0x73FCC7). The addend is the
            // patched byte; the +0x18c dword is a native field this switch
            // does not rewrite and is not identified in C# — treated as 0.
            // LastHiter[+0x579] subtract at 0x73FD08 is likewise unpatched
            // native and omitted here.
            denominator = redName ? RedNameK() : NormalK();
            // 0x73FD0B cmp [ebp-8],0 / jge / xor — native floors before Random.
            if (denominator < 0) denominator = 0;
            capImm = unchecked((sbyte)(MaxEquipCount() - 1));
            return true;
        }

        /// <summary>脚本控制人物爆率</summary>
        public bool IsScriptDropRateEnabled() => Enabled("脚本控制人物爆率");

        /// <summary>脚本控制头发外显</summary>
        public bool IsScriptHairEnabled() => Enabled("脚本控制头发外显");

        /// <summary>新怪物爆率启用</summary>
        public bool IsNewMonsterDropEnabled() => Enabled("新怪物爆率");

        /// <summary>攻沙脚本控制</summary>
        public bool IsSiegeScriptEnabled() => Enabled("攻沙脚本控制");

        /// <summary>持久战修改 — 攻城时长修改(分钟)</summary>
        public int GetSiegeModMinute() => GetParamInt("攻城修改_分钟", 0);
        /// <summary>持久战修改 — 攻城时长修改(天数)</summary>
        public int GetSiegeModDay() => GetParamInt("攻城修改_天数", 0);
        /// <summary>持久战修改 — 攻城时长修改(小时)</summary>
        public int GetSiegeModHour() => GetParamInt("攻城修改_小时", 0);
        /// <summary>持久战修改 — 攻城时长(分钟)</summary>
        public int GetSiegeDuration() => GetParamInt("攻城时长_分钟", 0);

        /// <summary>队伍职业限制</summary>
        public bool IsGamePartnerLimitEnabled() => Enabled("战队职业限制");

        /// <summary>专职变性 (转职/变性功能)</summary>
        public bool IsChangeJobEnabled() => Enabled("专职变性");

        /// <summary>毫秒级CD记录启用</summary>
        public bool IsMsCDEnabled() => Enabled("毫秒级cd记录");

        /// <summary>AddLimLF函数修改</summary>
        public bool IsAddLimLFModified() => Enabled("AddLimLF函数修改");

        /// <summary>IncActivePoint函数修改</summary>
        public bool IsIncActivePointModified() => Enabled("IncActivePoint函数修改");

        /// <summary>ServerSay函数</summary>
        public bool IsServerSayEnabled() => Enabled("ServerSay函数");

        /// <summary>SetNoKillMapLv脚本触发</summary>
        public bool IsNoKillMapLvEnabled() => Enabled("SetNoKillMapLv脚本触发");

        /// <summary>设置玩家称号函数</summary>
        public bool IsSetTitleEnabled() => Enabled("设置玩家称号函数");

        /// <summary>获取沙城归属</summary>
        public bool IsGetCastleEnabled() => Enabled("获取沙城归属");

        /// <summary>禁止宝宝休息</summary>
        public bool IsPetNoRestEnabled() => Enabled("禁止宝宝休息");

        /// <summary>宠物吸血a</summary>
        public bool IsPetVampireEnabled() => Enabled("宠物吸血a");

        /// <summary>宝宝叛变属性a</summary>
        public bool IsPetRebelAttrEnabled() => Enabled("宝宝叛变属性a");

        /// <summary>宝宝自动叛变</summary>
        public bool IsPetAutoRebelEnabled() => Enabled("宝宝自动叛变");

        /// <summary>下线宝宝死亡</summary>
        public bool IsPetDieOfflineEnabled() => Enabled("下线宝宝死亡");

        /// <summary>英雄攻速移速</summary>
        public bool IsHeroSpeedEnabled() => Enabled("英雄攻速移速");

        /// <summary>英雄千分比免伤</summary>
        public bool IsHeroDmgReductionEnabled() => Enabled("英雄千分比免伤");

        /// <summary>英雄野蛮</summary>
        public bool IsHeroBarbarianEnabled() => Enabled("英雄野蛮");

        /// <summary>读取英雄装备</summary>
        public bool IsHeroReadEquipEnabled() => Enabled("读取英雄装备");

        /// <summary>英雄穿戴触发</summary>
        public bool IsHeroWearTriggerEnabled() => Enabled("英雄穿戴触发");

        /// <summary>英雄物理攻击触发</summary>
        public bool IsHeroPhysTriggerEnabled() => Enabled("英雄物理攻击触发");

        /// <summary>英雄魔法攻击触发</summary>
        public bool IsHeroMagicTriggerEnabled() => Enabled("英雄魔法攻击触发");

        /// <summary>高级英雄倍功暴击</summary>
        public bool IsHeroAdvancedPowerCritEnabled() => Enabled("高级英雄倍功暴击");

        /// <summary>主号分身术a</summary>
        public bool IsMainCloneEnabled() => Enabled("主号分身术a");

        /// <summary>主号高级暴击</summary>
        public bool IsMainAdvCritEnabled() => Enabled("主号高级暴击");

        /// <summary>召唤神兽触发</summary>
        public bool IsShenShouTriggerEnabled() => Enabled("召唤神兽触发");

        /// <summary>召唤骷髅触发</summary>
        public bool IsKuLouTriggerEnabled() => Enabled("召唤骷髅触发");

        /// <summary>修改召唤神兽(新版神兽逻辑)</summary>
        public bool IsModifyShenShouEnabled() => Enabled("修改召唤神兽");

        /// <summary>读取英雄极品</summary>
        public bool IsHeroReadExtremeEnabled() => Enabled("英雄读取极品");

        /// <summary>give极品 (脚本给极品装备)</summary>
        public bool IsGiveExtremeEnabled() => Enabled("give极品");

        /// <summary>随身仓库</summary>
        public bool IsPortableStorageEnabled() => Enabled("随身仓库");

        /// <summary>新呼唤宝宝</summary>
        public bool IsNewCallPetEnabled() => Enabled("新呼唤宝宝");

        /// <summary>自定义召唤怪物a</summary>
        public bool IsCustomCallEnabled() => Enabled("自定义召唤怪物a");

        /// <summary>复活戒指cd修改</summary>
        public bool IsReviveCDEnabled() => Enabled("复活戒指改cd");

        /// <summary>复活戒指概率</summary>
        public bool IsReviveChanceEnabled() => Enabled("复活戒指概率");

        /// <summary>复活戒指重设</summary>
        public bool IsReviveResetEnabled() => Enabled("复活戒指重设");

        /// <summary>复活戒指重设_无敌时间开关</summary>
        public bool IsReviveImmuneEnabled() => Enabled("复活戒指重设_无敌时间");

        /// <summary>复活戒指重设_重设时间开关</summary>
        public bool IsReviveResetTimeEnabled() => Enabled("复活戒指重设_重设时间");

        /// <summary>切换暴击报文</summary>
        public bool IsSwitchCritMsgEnabled() => Enabled("切换暴击报文");

        /// <summary>野蛮麻痹 (野蛮冲撞带麻痹效果)</summary>
        public bool IsBarbarianParalysisEnabled() => Enabled("野蛮麻痹");

        /// <summary>野蛮等级 (野蛮冲撞等级限制)</summary>
        public bool IsBarbarianLevelEnabled() => Enabled("野蛮等级");

        /// <summary>盘古给与封号</summary>
        public bool IsPgGiveTitleEnabled() => Enabled("盘古给与封号");

        /// <summary>盘古高级属性</summary>
        public bool IsPgAdvancedAttrEnabled() => Enabled("盘古高级属性");

        /// <summary>法师群毒开关</summary>
        public bool IsMageGroupPoisonEnabled() => Enabled("法师群毒");

        /// <summary>群毒值开关</summary>
        public bool IsGroupPoisonValEnabled() => Enabled("群毒值");

        /// <summary>战士合击开关</summary>
        public bool IsWarriorComboEnabled() => Enabled("战士合击");

        /// <summary>法道合击开关</summary>
        public bool IsWizTaoComboEnabled() => Enabled("法道合击");

        /// <summary>道士合击系数开关</summary>
        public bool IsTaoComboFactorEnabled() => Enabled("道士合击系数");

        /// <summary>技能等级突破最大值</summary>
        public bool IsLevelBreakMaxEnabled() => Enabled("技能等级突破_最大值");

        /// <summary>主号全局法速开关</summary>
        public bool IsMainGlobalSpeedEnabled() => Enabled("主号全局法速");

        /// <summary>主号施法速度开关</summary>
        public bool IsMainCastSpeedEnabled() => Enabled("主号施法速度");

        /// <summary>循环时间值开关</summary>
        public bool IsLoopTimeValEnabled() => Enabled("循环时间_值");

        /// <summary>删除英雄技能</summary>
        public bool IsDelHeroSkillEnabled() => Enabled("删除英雄技能");
    }
}
