using System.Collections.Concurrent;
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
        public void GivePis(int elementType, int bodyPos, int value)
        {
            if (!Enabled("自定义元素")) return;
            if (bodyPos < 0 || bodyPos >= _player.m_UseItems.Length) return;
            var item = _player.m_UseItems[bodyPos];
            if (item == null) return;
            SetElementValue(item, elementType, value);
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

        /// <summary>NPCOK框给物品加17元素, ClientItemID=OK框物品ID</summary>
        public int NpcGiveItemYs(int clientItemId, int[] ys)
        {
            if (!Enabled("自定义元素")) return 0;
            if (ys == null || ys.Length < 1) return 0;
            var found = FindOwnedItemByClientId(clientItemId);
            if (found == null) return 0;
            for (int i = 0; i < Math.Min(ys.Length, 17); i++)
                SetElementValue(found, i + 1, ys[i]);
            RefreshOwnedItem(found);
            return 1;
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
            if (item.btValue != null && item.btValue.Length >= 9) return;

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
        public int SetEquipElement(int bodyPos, int elemId, int value) { GivePis(elemId, bodyPos, value); return value; }

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
            return GetExtremeValue(item, jpIdx);
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
            if (SetExtremeValue(item, jpIdx, value))
            {
                if (types == 0) _player.SendUpdateItem(item);
                return value;
            }
            return 0;
        }

        /// <summary>装备持久操作: types 0=查询 1=增加 2=减少 3=设置</summary>
        public int EquipDura(int bodyPos, int value, int opType)
        {
            if (!Enabled("自定义元素")) return 0;
            if (bodyPos < 0 || bodyPos >= _player.m_UseItems.Length) return 0;
            var item = _player.m_UseItems[bodyPos];
            if (item == null) return 0;
            return opType switch { 0 => item.Dura, 1 => (item.Dura = (ushort)(item.Dura + value)), 2 => (item.Dura = (ushort)Math.Max(0, item.Dura - value)), 3 => (item.Dura = (ushort)value), _ => item.Dura };
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
            if (!EnabledAll("眼神特殊函数", "怪物伤害触发技能特效")) return 0;
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

        /// <summary>设置宝宝属性</summary>
        public int SetPetAttr(string monName, int id, int ac, int dc, int dcMax, int mac, int mc, int sc, int gs, int ys, int hp, int maxHp)
        {
            if (!EnabledAll("眼神特殊函数", "怪物伤害触发技能特效")) return 0;
            int count = 0;
            foreach (var slave in _player.m_SlaveList)
            {
                if (slave == null) continue;
                if (string.IsNullOrEmpty(monName) || slave.m_sCharName == monName)
                {
                    slave.m_WAbil.AC = HUtil32.MakeLong(ac, ac);
                    slave.m_WAbil.DC = HUtil32.MakeLong(dc, dcMax);
                    slave.m_WAbil.MAC = HUtil32.MakeLong(mac, mac);
                    slave.m_WAbil.MC = HUtil32.MakeLong(mc, mc);
                    slave.m_WAbil.SC = HUtil32.MakeLong(sc, sc);
                    slave.m_WAbil.HP = Math.Max(0, hp);
                    slave.m_WAbil.MaxHP = Math.Max(0, maxHp);
                    if (slave.m_WAbil.HP > slave.m_WAbil.MaxHP) slave.m_WAbil.HP = slave.m_WAbil.MaxHP;
                    slave.RecalcAbilitys();
                    count++;
                }
            }
            return count;
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

        /// <summary>与 NPC 给予灵符同一 reason（PasApiBridge.NativeGive / 魔塔奖励都用 23001）。</summary>
        private const int RecycleLingFuReason = 23001;

        /// <summary>自动回收 — 按JSON配置回收背包物品, -999=JSON语法错误</summary>
        public int AutoRecycle()
        {
            if (!Enabled("高级回收")) return 0;
            try
            {
                var recycleConfig = _pluginManager?.GetRecycleConfigSnapshot();
                if (recycleConfig == null) return -999;
                if (!RecycleBagModelResolved()) return 0;

                var recycled = 0;
                for (int i = _player.m_ItemList.Count - 1; i >= 0; i--)
                {
                    var item = _player.m_ItemList[i];
                    if (item == null) continue;
                    var itemName = M2Share.UserEngine.GetStdItemName(item.wIndex);
                    if (!recycleConfig.TryGetItemRule(itemName, out var rule, out var stackable))
                        continue;
                    if (!RecycleTypeOpen(rule)) continue;
                    if (!stackable && !RecycleQualityAllowed(item, rule)) continue;
                    if (TryRecycleOne(item, itemName, rule)) recycled++;
                }
                return recycled;
            }
            catch (Exception ex)
            {
                M2Share.MainOutMessage("[异常] AutoRecycle " + ex.Message);
                return -999;
            }
        }

        /// <summary>
        /// 无限背包 把额外格子存在 M2 背包之外（Gs1\MyJson\bags\&lt;角色名&gt;.bin），C# 还没有
        /// 复刻那个容器，所以回收只能看见 m_ItemList。生产 items\config.json 用的是
        /// "无限背包_是否固定":"固定格子"（额外格子=144，变量v1=10/变量v2=1 在这条分支下不参与
        /// 计算——V(10,1) 在生产里是"商店装备"回收开关，拿它算格子数显然不是本意）。
        /// "V变量控制格子" 那条分支的格子数取自 GetV(变量v1,变量v2)，没有任何字节证据，
        /// 保持关闭：宁可一件不回收，也不能对着一个没复刻的容量模型删东西。
        /// </summary>
        private bool RecycleBagModelResolved()
        {
            var manager = _pluginManager;
            if (manager == null) return true;
            if (!IsEnabledValue(manager.GetItemConfigValue("无限背包_是否勾选"))) return true;
            return PluginManager.NormalizeConfigValue(
                manager.GetItemConfigValue("无限背包_是否固定")) as string == "固定格子";
        }

        /// <summary>总开关：GetV(v1,v2)==关闭值 时该类型停止回收；省略则失去开关效果。</summary>
        private bool RecycleTypeOpen(RecycleRule rule)
        {
            if (!rule.HasMasterSwitch) return true;
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
        /// 结算一件物品。产出与删除必须一起成立：任何一路产出算不出来、落不了账，
        /// 或者物品删不掉，都整件放弃，绝不出现删了不给的中间态。
        /// </summary>
        private bool TryRecycleOne(TUserItem item, string itemName, RecycleRule rule)
        {
            // 倍率：GetV=200 表示 2 倍 ⇒ 单价*GetV/100，先乘后除；小于等于 0 表示无效，按 1 倍。
            var rate = rule.HasRate ? ReadPlayerV(rule.RateGroup, rule.RateIndex) : 0;
            if (!TryScaleRecyclePrice(rule.Yuanbao, rate, out var yuanbao) ||
                !TryScaleRecyclePrice(rule.Gold, rate, out var gold) ||
                !TryScaleRecyclePrice(rule.LingFu, rate, out var lingFu) ||
                !TryScaleRecyclePrice(rule.Exp, rate, out var exp) ||
                !TryScaleRecyclePrice(rule.HasOther ? rule.OtherValue : 0, rate, out var other))
                return false;

            // 元宝走 NativeYuanbaoManager 的异步 DB 往返，结算成败要等回调，没法和 DelBagItem
            // 放进同一次调用里确认 ⇒ 会产出元宝的物品一律不回收。
            if (yuanbao > 0) return false;

            // 灵符在开了信用点服务时会改走限时灵符账户，落到哪个账户无从验证，同样不回收。
            if (lingFu > 0 && M2Share.CreditCardService?.Enabled == true) return false;

            // 预检：IncGold 在超过每角色 m_nGoldMax 时返回 false（0x6D7930 cmp ebx,[eax+0x68C]）。
            if (gold > 0 && (long)_player.m_nGold + gold > _player.m_nGoldMax) return false;

            // 其他 走 0x1006BCB7（可叠材料）/ 0x1006CDB4（物品种类）两段同构代码：
            //   0x1006BCBC 7E 6F        jle  —— 缩放后 <= 0 就整段不写 SetV
            //   0x1006BCC2 0F AF F8     imul —— 和其余四路一样吃倍率
            //   0x1006BCFD 7D 02 / 0x1006BCFF 33 C0  —— 累加基数的负值钳到 0
            var otherStored = 0;
            var otherTotal = 0;
            var otherPays = other > 0;
            if (otherPays)
            {
                if (!PlayerVarWritable(rule.OtherGroup, rule.OtherIndex)) return false;
                // 回滚要还原真实旧值，所以钳位只用于累加，不覆盖 otherStored。
                otherStored = ReadStoredPlayerV(rule.OtherGroup, rule.OtherIndex);
                var accumulated = (long)Math.Max(0, otherStored) + other;
                if (accumulated > int.MaxValue) return false;
                otherTotal = (int)accumulated;
            }

            var goldPaid = 0;
            var lingFuPaid = 0;
            var otherWritten = false;

            if (gold > 0)
            {
                if (!_player.IncGold(gold)) return false;
                goldPaid = gold;
            }

            if (lingFu > 0)
            {
                if (!_player.AddNativeLingFu(RecycleLingFuReason, lingFu))
                {
                    RollbackRecycleGold(goldPaid);
                    return false;
                }
                lingFuPaid = lingFu;
            }

            if (otherPays)
            {
                WritePlayerV(rule.OtherGroup, rule.OtherIndex, otherTotal);
                otherWritten = true;
            }

            // GainExp 没有返回值也撤不回来，所以放在删除之前：删除万一失败，玩家是多拿了经验
            // 又留下了物品，方向上只会多给，不会少给。
            if (exp > 0) _player.GainExp(exp);

            if (_player.DelBagItem(item.MakeIndex, itemName)) return true;

            if (otherWritten) WritePlayerV(rule.OtherGroup, rule.OtherIndex, otherStored);
            RollbackRecycleLingFu(lingFuPaid);
            RollbackRecycleGold(goldPaid);
            return false;
        }

        private static bool TryScaleRecyclePrice(int unitPrice, int rate, out int amount)
        {
            amount = 0;
            if (unitPrice <= 0) return true;
            var scaled = rate > 0 ? (long)unitPrice * rate / 100 : unitPrice;
            if (scaled < 0 || scaled > int.MaxValue) return false;
            amount = (int)scaled;
            return true;
        }

        private void RollbackRecycleGold(int amount)
        {
            if (amount <= 0) return;
            _player.m_nGold -= amount;
            _player.GoldChanged();
        }

        private void RollbackRecycleLingFu(int amount)
        {
            if (amount <= 0) return;
            _player.m_nLingFu = unchecked(_player.m_nLingFu - amount);
            _player.RefreshNativeLingFu();
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
            if (group == 0)
                return index >= 1 && index <= 100
                    ? player.m_ScriptVGroup0[index]
                    : NativeScriptVarMiss;
            if (group < 0 || index <= 0) return NativeScriptVarMiss;
            return player.m_ScriptVVars != null &&
                   player.m_ScriptVVars.TryGetValue(group * 1000 + index, out var value)
                ? value
                : NativeScriptVarMiss;
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
            if (group == 0)
                return index >= 1 && index <= 100 ? player.m_ScriptVGroup0[index] : 0;
            return player.m_ScriptVVars != null &&
                   player.m_ScriptVVars.TryGetValue(group * 1000 + index, out var value)
                ? value
                : 0;
        }

        /// <summary>SetV 的收参门：0x6DF2B3/0x6DF2B7 两个 test 拒掉非正参数，组 0 只收 1..100。</summary>
        private static bool PlayerVarWritable(int group, int index) =>
            group == 0 ? index >= 1 && index <= 100 : group > 0 && index > 0;

        private void WritePlayerV(int group, int index, int value)
        {
            var player = _player;
            if (player == null) return;
            if (group == 0)
            {
                player.m_ScriptVGroup0[index] = value;
                return;
            }
            // 原生 upsert sub_6E4140 没有零值判断，0 也原样写入。
            player.m_ScriptVVars[group * 1000 + index] = value;
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

        /// <summary>物品绑定/解绑: flag 0=绑定 >0=解绑</summary>
        public int BindUnbindItem(int itemId, int flag)
        {
            if (!Enabled("屏蔽自动绑定")) return 0;
            // Find item by MakeIndex in bag
            foreach (var item in _player.m_ItemList)
            {
                if (item != null && item.MakeIndex == itemId)
                {
                    // Use btValue[8] as bind flag: 0=unbound, 1+=bound
                    item.btValue[8] = flag == 0 ? (byte)1 : (byte)0;
                    _player.SendUpdateItem(item);
                    return flag;
                }
            }
            // Also check equipped items
            for (int i = 0; i < _player.m_UseItems.Length; i++)
            {
                var item = _player.m_UseItems[i];
                if (item != null && item.MakeIndex == itemId)
                {
                    item.btValue[8] = flag == 0 ? (byte)1 : (byte)0;
                    _player.SendUpdateItem(item);
                    return flag;
                }
            }
            return flag;
        }

        /// <summary>在地面丢弃物品</summary>
        public int DropItem(int count, int range, string itemName)
        {
            if (_npc == null) return 0;
            // Drop items from bag to ground — uses DelBagItem to remove, item appears on ground via map system
            for (int i = 0; i < count; i++)
            {
                var userItem = _player.CheckItems(itemName);
                if (userItem == null) break;
                _player.DelBagItem(userItem.MakeIndex, M2Share.UserEngine.GetStdItemName(userItem.wIndex));
            }
            return count;
        }

        /// <summary>按身体部位爆装备</summary>
        public int DropEquipByPos(int pos) { if (pos < 0 || pos >= _player.m_UseItems.Length) return 0; var it=_player.m_UseItems[pos]; if(it==null)return 0; _player.DelBagItem(it.MakeIndex,M2Share.UserEngine.GetStdItemName(it.wIndex)); return 1; }

        /// <summary>按装备名字爆装备</summary>
        public int DropEquipByName(string name)
        {
            for (int i = 0; i < _player.m_UseItems.Length; i++)
            {
                var item = _player.m_UseItems[i];
                if (item != null && string.Equals(M2Share.UserEngine.GetStdItemName(item.wIndex), name, StringComparison.OrdinalIgnoreCase))
                { _player.DelBagItem(item.MakeIndex, M2Share.UserEngine.GetStdItemName(item.wIndex)); return 1; }
            }
            return 0;
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

        /// <summary>地图怪物数量查询</summary>
        public int CheckMapMonByName(string mapName, string monName)
        {
            var map = M2Share.MapManager.FindMap(mapName);
            if (map == null) return 0;
            int count = 0;
            var list = new List<TBaseObject>();
            M2Share.UserEngine.GetMapMonster(map, list);
            foreach (var m in list) if (m?.m_sCharName?.IndexOf(monName, StringComparison.OrdinalIgnoreCase) >= 0) count++;
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

        public bool IsOneSword() => Enabled("基本剑术");
        public int OneSwordN() => ParamAtoi("基本剑术_n值", 3);

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
        public bool IsShenShouCount() => Enabled("神兽_数量");
        public bool IsKuLouCount() => Enabled("召唤骷髅_数量");
        public int ShenShouIdx() => GetParamInt("神兽_序号", -1);

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
        public bool IsFixDefense() => Enabled("修复卡防御");
        public bool IsZeroDefSplit() => Enabled("防0拆分");
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
        public bool IsRandomExtreme() => Enabled("随机极品");
        public bool IsGiveExtreme() => Enabled("give极品");
        public int MaxEquipCount() { var v = ParamS("最大装备数量"); if (string.IsNullOrEmpty(v)) return 0; return int.TryParse(v, out var n) ? n : 0; }

        // ── Equipment stat param readers (武器/衣服/头盔/项链/手镯/戒指) ──
        // 武器
        public int WeaponAttrChance_Acc() => GetParamInt("武器属性几率_准确_值", 24);
        public int WeaponAttrChance_Atk() => GetParamInt("武器属性几率_攻击_值", 30);
        public int WeaponAttrChance_Spd() => GetParamInt("武器属性几率_攻速_值", 30);
        public int WeaponAttrChance_Tao() => GetParamInt("武器属性几率_道术_值", 30);
        public int WeaponAttrChance_Mgc() => GetParamInt("武器属性几率_魔法_值", 30);
        public int WeaponRandExtreme() => GetParamInt("武器最随机性_极品_值", 20);
        public int WeaponMaxPts_Acc() => GetParamInt("武器最高点数_准确_值", 13);
        public int WeaponMaxPts_Atk() => GetParamInt("武器最高点数_攻击_值", 7);
        public int WeaponMaxPts_Spd() => GetParamInt("武器最高点数_攻速_值", 13);
        public int WeaponMaxPts_Tao() => GetParamInt("武器最高点数_道术_值", 13);
        public int WeaponMaxPts_Mgc() => GetParamInt("武器最高点数_魔法_值", 13);
        public int WeaponPtsChance_Acc() => GetParamInt("武器点数几率_准确_值", 15);
        public int WeaponPtsChance_Atk() => GetParamInt("武器点数几率_攻击_值", 20);
        public int WeaponPtsChance_Spd() => GetParamInt("武器点数几率_攻速_值", 15);
        public int WeaponPtsChance_Tao() => GetParamInt("武器点数几率_道术_值", 15);
        public int WeaponPtsChance_Mgc() => GetParamInt("武器点数几率_魔法_值", 15);
        // 衣服
        public int ArmorAttrChance_Acc() => GetParamInt("衣服属性几率_准确_值", 7);
        public int ArmorAttrChance_Atk() => GetParamInt("衣服属性几率_攻击_值", 20);
        public int ArmorAttrChance_Spd() => GetParamInt("衣服属性几率_攻速_值", 30);
        public int ArmorAttrChance_Tao() => GetParamInt("衣服属性几率_道术_值", 30);
        public int ArmorAttrChance_Mgc() => GetParamInt("衣服属性几率_魔法_值", 20);
        public int ArmorRandExtreme() => GetParamInt("衣服最随机性_极品_值", 10);
        public int ArmorMaxPts_Acc() => GetParamInt("衣服最高点数_准确_值", 10);
        public int ArmorMaxPts_Atk() => GetParamInt("衣服最高点数_攻击_值", 7);
        public int ArmorMaxPts_Spd() => GetParamInt("衣服最高点数_攻速_值", 7);
        public int ArmorMaxPts_Tao() => GetParamInt("衣服最高点数_道术_值", 7);
        public int ArmorMaxPts_Mgc() => GetParamInt("衣服最高点数_魔法_值", 7);
        public int ArmorPtsChance_Acc() => GetParamInt("衣服点数几率_准确_值", 30);
        public int ArmorPtsChance_Atk() => GetParamInt("衣服点数几率_攻击_值", 20);
        public int ArmorPtsChance_Spd() => GetParamInt("衣服点数几率_攻速_值", 20);
        public int ArmorPtsChance_Tao() => GetParamInt("衣服点数几率_道术_值", 20);
        public int ArmorPtsChance_Mgc() => GetParamInt("衣服点数几率_魔法_值", 20);
        // 头盔
        public int HelmetAttrChance_Acc() => GetParamInt("头盔属性几率_准确_值", 20);
        public int HelmetAttrChance_Atk() => GetParamInt("头盔属性几率_攻击_值", 30);
        public int HelmetAttrChance_Spd() => GetParamInt("头盔属性几率_攻速_值", 20);
        public int HelmetAttrChance_Tao() => GetParamInt("头盔属性几率_道术_值", 30);
        public int HelmetAttrChance_Mgc() => GetParamInt("头盔属性几率_魔法_值", 30);
        public int HelmetRandExtreme() => GetParamInt("头盔最随机性_极品_值", 10);
        public int HelmetMaxPts_Acc() => GetParamInt("头盔最高点数_准确_值", 7);
        public int HelmetMaxPts_Atk() => GetParamInt("头盔最高点数_攻击_值", 7);
        public int HelmetMaxPts_Spd() => GetParamInt("头盔最高点数_攻速_值", 7);
        public int HelmetMaxPts_Tao() => GetParamInt("头盔最高点数_道术_值", 7);
        public int HelmetMaxPts_Mgc() => GetParamInt("头盔最高点数_魔法_值", 7);
        public int HelmetPtsChance_Acc() => GetParamInt("头盔点数几率_准确_值", 20);
        public int HelmetPtsChance_Atk() => GetParamInt("头盔点数几率_攻击_值", 20);
        public int HelmetPtsChance_Spd() => GetParamInt("头盔点数几率_攻速_值", 20);
        public int HelmetPtsChance_Tao() => GetParamInt("头盔点数几率_道术_值", 20);
        public int HelmetPtsChance_Mgc() => GetParamInt("头盔点数几率_魔法_值", 20);
        // 项链
        public int NecklaceAttrChance_Acc() => GetParamInt("项链属性几率_准确_值", 7);
        public int NecklaceAttrChance_Atk() => GetParamInt("项链属性几率_攻击_值", 40);
        public int NecklaceAttrChance_Spd() => GetParamInt("项链属性几率_攻速_值", 30);
        public int NecklaceAttrChance_Tao() => GetParamInt("项链属性几率_道术_值", 30);
        public int NecklaceAttrChance_Mgc() => GetParamInt("项链属性几率_魔法_值", 40);
        public int NecklaceRandExtreme() => GetParamInt("项链最随机性_极品_值", 20);
        public int NecklaceMaxPts_Acc() => GetParamInt("项链最高点数_准确_值", 10);
        public int NecklaceMaxPts_Atk() => GetParamInt("项链最高点数_攻击_值", 7);
        public int NecklaceMaxPts_Spd() => GetParamInt("项链最高点数_攻速_值", 7);
        public int NecklaceMaxPts_Tao() => GetParamInt("项链最高点数_道术_值", 7);
        public int NecklaceMaxPts_Mgc() => GetParamInt("项链最高点数_魔法_值", 7);
        public int NecklacePtsChance_Acc() => GetParamInt("项链点数几率_准确_值", 20);
        public int NecklacePtsChance_Atk() => GetParamInt("项链点数几率_攻击_值", 20);
        public int NecklacePtsChance_Spd() => GetParamInt("项链点数几率_攻速_值", 20);
        public int NecklacePtsChance_Tao() => GetParamInt("项链点数几率_道术_值", 20);
        public int NecklacePtsChance_Mgc() => GetParamInt("项链点数几率_魔法_值", 20);
        // 手镯
        public int BraceletAttrChance_Acc() => GetParamInt("手镯属性几率_准确_值", 7);
        public int BraceletAttrChance_Atk() => GetParamInt("手镯属性几率_攻击_值", 30);
        public int BraceletAttrChance_Spd() => GetParamInt("手镯属性几率_攻速_值", 20);
        public int BraceletAttrChance_Tao() => GetParamInt("手镯属性几率_道术_值", 20);
        public int BraceletAttrChance_Mgc() => GetParamInt("手镯属性几率_魔法_值", 30);
        public int BraceletRandExtreme() => GetParamInt("手镯最随机性_极品_值", 20);
        public int BraceletMaxPts_Acc() => GetParamInt("手镯最高点数_准确_值", 10);
        public int BraceletMaxPts_Atk() => GetParamInt("手镯最高点数_攻击_值", 7);
        public int BraceletMaxPts_Spd() => GetParamInt("手镯最高点数_攻速_值", 7);
        public int BraceletMaxPts_Tao() => GetParamInt("手镯最高点数_道术_值", 7);
        public int BraceletMaxPts_Mgc() => GetParamInt("手镯最高点数_魔法_值", 7);
        public int BraceletPtsChance_Acc() => GetParamInt("手镯点数几率_准确_值", 30);
        public int BraceletPtsChance_Atk() => GetParamInt("手镯点数几率_攻击_值", 20);
        public int BraceletPtsChance_Spd() => GetParamInt("手镯点数几率_攻速_值", 20);
        public int BraceletPtsChance_Tao() => GetParamInt("手镯点数几率_道术_值", 20);
        public int BraceletPtsChance_Mgc() => GetParamInt("手镯点数几率_魔法_值", 20);
        // 戒指
        public int RingAttrChance_Acc() => GetParamInt("戒指属性几率_准确_值", 20);
        public int RingAttrChance_Atk() => GetParamInt("戒指属性几率_攻击_值", 30);
        public int RingAttrChance_Spd() => GetParamInt("戒指属性几率_攻速_值", 20);
        public int RingAttrChance_Tao() => GetParamInt("戒指属性几率_道术_值", 30);
        public int RingAttrChance_Mgc() => GetParamInt("戒指属性几率_魔法_值", 30);
        public int RingRandExtreme() => GetParamInt("戒指最随机性_极品_值", 10);
        public int RingMaxPts_Acc() => GetParamInt("戒指最高点数_准确_值", 7);
        public int RingMaxPts_Atk() => GetParamInt("戒指最高点数_攻击_值", 7);
        public int RingMaxPts_Spd() => GetParamInt("戒指最高点数_攻速_值", 7);
        public int RingMaxPts_Tao() => GetParamInt("戒指最高点数_道术_值", 7);
        public int RingMaxPts_Mgc() => GetParamInt("戒指最高点数_魔法_值", 7);
        public int RingPtsChance_Acc() => GetParamInt("戒指点数几率_准确_值", 30);
        public int RingPtsChance_Atk() => GetParamInt("戒指点数几率_攻击_值", 20);
        public int RingPtsChance_Spd() => GetParamInt("戒指点数几率_攻速_值", 30);
        public int RingPtsChance_Tao() => GetParamInt("戒指点数几率_道术_值", 20);
        public int RingPtsChance_Mgc() => GetParamInt("戒指点数几率_魔法_值", 20);

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
        public bool IsBlockSpam() => Enabled("屏蔽发言频繁禁言功能");
        public bool IsDelSkillSilent() => Enabled("删除技能不提示");
        public bool IsDelHeroSkill() => Enabled("删除英雄技能");
        public bool IsUpSkillSilent() => Enabled("升级技能不提示");
        public bool IsBanChatSilent() => Enabled("禁止发言不提示");
        public bool IsNameColor() => Enabled("名字变色");
        public bool IsLevelMute() => Enabled("等级禁言");
        public bool IsMailAntiSpam() => Enabled("邮件防刷");
        public bool IsPlayerDropRate() => Enabled("人物爆率调整");
        public int PlayerLv1() => GetParamInt("人物等级1_值", 35);
        public int PlayerLv2() => GetParamInt("人物等级2_值", 40);
        public int PlayerLv3() => GetParamInt("人物等级3_值", 48);
        public bool IsScriptDropRate() => Enabled("脚本控制人物爆率");
        public bool IsScriptHair() => Enabled("脚本控制头发外显");
        public bool IsNewMonsterDrop() => Enabled("新怪物爆率");
        public bool IsGetCastle() => Enabled("获取沙城归属");
        public bool IsGuildShow() => Enabled("行会显示");
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
        public bool IsCloseStall() => Enabled("关闭摆摊");
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
        public string MonsterName1() => ParamS("怪物名字1_值", "强化神兽");
        public string MonsterName2() => ParamS("怪物名字2_值", "强化神兽");
        public string MonsterName3() => ParamS("怪物名字3_值", "白虎");
        public bool IsMonsterCount1() => Enabled("怪物数量1_值");
        public int MonsterCount2() => GetParamInt("怪物数量2_值", 2);
        public int MonsterCount3() => GetParamInt("怪物数量3_值", 2);
        public bool IsMonsterDropA() => Enabled("怪物爆率A_值");
        public bool IsMonsterDropB() => Enabled("怪物爆率B_值");
        public bool IsMonsterDropK() => Enabled("怪物爆率K_值");

        // ── Red/Green name K值 params ──
        public int RedNameK() { var v = ParamS("红名K值"); if (string.IsNullOrEmpty(v)) return 0; return int.TryParse(v, out var n) ? n : 0; }
        public int NormalK() { var v = ParamS("非红名K值"); if (string.IsNullOrEmpty(v)) return 0; return int.TryParse(v, out var n) ? n : 0; }

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

        /// <summary>禁止长度为 15 的地图内切换宝宝到休息状态。</summary>
        public bool IsPetRestBlocked()
        {
            if (!Enabled("禁止宝宝休息")) return false;
            return _player?.m_PEnvir?.sMapName?.Length == 15;
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
        public bool IsStallClosed() => Enabled("关闭摆摊");

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
            if (!Enabled("地面物品消失时间")) return false;

            var seconds = Math.Max(0, GetParamInt("地面物品消失时间_时间", 600));
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

        /// <summary>人物爆率调整 — 获取爆率倍率</summary>
        public double GetPlayerDropRateMultiplier()
        {
            if (!Enabled("人物爆率调整")) return 1.0;
            return 1.0; // Default multiplier, overridden by script
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
