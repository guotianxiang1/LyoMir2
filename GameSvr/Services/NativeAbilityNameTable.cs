using System;
using System.Collections.Generic;

namespace GameSvr
{
    /// <summary>
    /// The native ability-name table at 0x7D4E9C and the lookup sub_78FB6C that
    /// turns a name into the ability code every named-bonus path uses.
    ///
    /// sub_78FB6C @0x78FB6C:
    ///   0x78FB92  compare against the constant at 0x78FBF8 (灵媒) -&gt; code 0xFF
    ///   0x78FBAA  otherwise walk the 158 AnsiString slots from 0x7D4E9C with
    ///             ESI running 1..0x9E and return ESI on the first exact match
    ///   0x78FB8C  no match leaves EDI = -1, and the caller at 0x746E34 stores it
    ///             as a word, so an unknown name becomes code 0xFFFF rather than 0
    ///
    /// The consumer chain is 0x746D6C (rebuild the shenYou bonus block at
    /// [container+0x375]) -&gt; 0x75F548 (apply each non-zero code) -&gt; 0x75F588 -&gt;
    /// 0x78E830 (apply one named bonus to [container+0x48] / [container+0x1F8]).
    ///
    /// Generated from flat_image.bin; the strings are the GBK bytes the native
    /// comparison at 0x40591C sees, so equality here matches equality there.
    /// </summary>
    public static class NativeAbilityNameTable
    {
        /// <summary>0x78FB9E `mov edi,0xFF` — the code the sentinel name yields.</summary>
        public const int SentinelCode = 0xFF;

        /// <summary>The constant at 0x78FBF8 that 0x78FB92 compares first.</summary>
        public const string SentinelName = "灵媒";

        /// <summary>0x78FBC3 `cmp esi,0x9F` — codes run 1..158.</summary>
        public const int Count = 158;

        /// <summary>0x78FB8C `or edi,-1`, stored as a word by the caller.</summary>
        public const int NotFoundCode = 0xFFFF;

        private static readonly string[] _names =
        {
            "攻击下限", "攻击上限", "魔法下限", "魔法上限",
            "道术下限", "道术上限", "防御下限", "防御上限",
            "魔御下限", "魔御上限", "体力值", "魔法值",
            "准确", "敏捷", "魔法躲避", "幸运",
            "诅咒", "攻击速度", "目标爆率", "防爆",
            "攻击吸血", "内力恢复速率", "内力恢复速度", "内功伤害",
            "内功减免", "内伤等级", "暴击等级", "负重",
            "合击威力", "麻痹抗性", "神圣", "药品魔法值回复",
            "药品体力值回复", "内力值上限", "强身等级", "聚魔等级",
            "主属性", "中毒恢复", "狂暴等级", "神圣攻击下限",
            "神圣攻击上限", "神圣魔法下限", "神圣魔法上限", "神圣道术下限",
            "神圣道术上限", "体力值百分比", "魔法值百分比", "神圣主属性下限",
            "神圣主属性上限", "神圣幸运", "装备主属性", "护体神盾强化",
            "击破", "麻痹强化", "龙神之怒", "乾坤借力",
            "怒之火雨增强", "怒之剑术增强", "怒之火符增强", "内功吸收",
            "连击威力增强", "合击等级", "扭转乾坤", "伤害百分比吸收",
            "魔血值", "冰冻抗性", "钢筋铁骨", "灭世",
            "强化重生", "觉醒", "法术伤害增强", "护身神技",
            "至尊护身神技", "白日门乾坤", "合击伤害抗性", "火墙伤害抗性",
            "近战伤害抗性", "金钟罩身", "龙神护体", "合击伤害减少",
            "准确百分比", "敏捷百分比", "麻痹时间增加", "龙神技能CD减少",
            "龙神之怒CD减少", "防麻时间增加", "扭转乾坤CD减少", "魔意麻痹神技",
            "道意麻痹神技", "伤害增加", "合击伤害减免", "合击威力增加",
            "裂石", "凝冰", "连击伤害抗性", "药品魔血值回复",
            "真龙护体", "致命几率", "致命伤害增加", "防致命几率",
            "致命伤害减少", "魔法伤害抗性", "道术伤害抗性", "龙神技能抗性",
            "十步一杀伤害增加", "天雷乱舞每秒伤害增加", "怒噬回天回血增加", "嗜血杀戮伤害增加",
            "复仇火焰伤害增加", "毁灭神符伤害增加", "刺术下限", "刺术上限",
            "神圣刺术下限", "神圣刺术上限", "血祭刃扇伤害增加", "升龙破",
            "神龙附体", "怒之暴击术增强", "召唤神龙护卫", "金元护体护盾时间",
            "金元护体护盾次数", "木元护体血量提升", "木元护体血量回复", "木元护体时间",
            "水元持续时间", "召唤水元伤害", "水元幸运", "水元断筋概率",
            "火元持续时间", "召唤火元伤害", "火元幸运", "火元祸乱概率",
            "火元祸乱伤害", "火元祸乱时间", "召唤土元伤害", "土元持续时间",
            "主属性百分比", "神圣主属性下限百分比", "神圣主属性上限百分比", "魔血值百分比",
            "伤害百分比减免", "神圣防御", "重击", "唯我独尊CD减少",
            "金攻击元素", "金防御元素", "木攻击元素", "木防御元素",
            "水攻击元素", "水防御元素", "火攻击元素", "火防御元素",
            "土攻击元素", "土防御元素", "低级坐骑装备属性增加", "中级坐骑装备属性增加",
            "高级坐骑装备属性增加", "魔法命中",
        };

        private static readonly Dictionary<string, int> _byName = BuildIndex();

        private static Dictionary<string, int> BuildIndex()
        {
            // 0x78FBAF walks the slots in ascending order and returns on the first
            // hit, so a duplicated name resolves to its lowest code.
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < _names.Length; i++)
            {
                if (!map.ContainsKey(_names[i]))
                    map[_names[i]] = i + 1;
            }
            return map;
        }

        public static IReadOnlyList<string> Names => _names;

        /// <summary>The name a 1-based code denotes, or null when out of range.</summary>
        public static string GetName(int code)
            => code >= 1 && code <= _names.Length ? _names[code - 1] : null;

        /// <summary>sub_78FB6C. Returns 0xFF for the sentinel and 0xFFFF for a miss.</summary>
        public static int Lookup(string name)
        {
            if (string.Equals(name, SentinelName, StringComparison.Ordinal))
                return SentinelCode;
            return _byName.TryGetValue(name ?? string.Empty, out var code) ? code : NotFoundCode;
        }
    }
}
