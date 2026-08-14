using GameSvr;

namespace GameSvr.Plugins
{
    /// <summary>
    /// 眼神2(第2页) + 扩展页 · <c>sub_100795C0</c> / <c>sub_10067C90</c> 等插件侧流水线中
    /// 已逐字节解语义、且 C# 落点与函数内序一致的键。
    /// </summary>
    internal static class YanshenPage2ExtBehaviors
    {
        /// <summary>
        /// 魔法伤害吸血 + 火墙不吸血门。原生在切割臂之后、返伤害之前
        /// （<c>0x1007AAC9</c> 攻击吸血 → <c>0x1007AAEB</c> 火墙不吸血 → <c>0x1007ABB8</c>）。
        /// </summary>
        /// <remarks>
        /// <code>
        /// 0x1007AAC9  cmp [cfg+0x124],0x1F4     ; 攻击吸血
        /// 0x1007AAD9  cmp [ebp+0x18],0x6AC8C8   ; 攻击方 TPlayObject
        /// 0x1007AAEB  cmp [cfg+0x1a4],0x1F4     ; 火墙不吸血
        /// 0x1007AAF7  cmp [ebp+0x1c],0x16       ; magicId 22 = 火墙
        /// 0x1007AAFB  je  0x1007ABB8            ; 开则跳过整段吸血
        /// 0x1007AB01  GetS(attacker,1,0xB2)    ; S(1,178)==100 且受害玩家 ⇒ 跳过
        /// 0x1007AB3C  GetS(attacker,1,0x81)    ; S(1,129) 千分比
        /// 0x1007AB68  divsd [0x102C8950]=1000.0
        /// 0x1007ABB1  call 0x769DB4 IncHealthSpell
        /// </code>
        /// </remarks>
        internal static void ApplyMagicDamageVamp(TBaseObject source, TBaseObject target,
            int damage, int skillId)
        {
            if (damage <= 0 || source is not TPlayObject attacker)
                return;
            if (!ToggleOn(attacker, "攻击吸血"))
                return;

            // 0x1007AAEB / 0x1007AAF7 / 0x1007AAFB
            if (skillId == SpellsDef.SKILL_EARTHFIRE &&
                ToggleOn(attacker, "火墙不吸血"))
                return;

            // 0x1007AB1C / 0x1007AB25 / 0x1007AB2C
            if (target is TPlayObject &&
                attacker.TryGetScriptVar('S', 1, 178, out int gate) &&
                gate == 100)
                return;

            if (!attacker.TryGetScriptVar('S', 1, 129, out int permille) || permille <= 0)
                return;

            int vamp = unchecked((int)((long)Math.Truncate(
                damage * (permille / 1000.0d))));
            if (vamp > 0)
                attacker.IncHealthSpell(vamp, 0);
        }

        /// <summary>
        /// 麻痹中不被麻痹a：目标 <c>[+0x168]</c> 状态位图首 dword 非零时拒绝再次麻痹。
        /// </summary>
        /// <remarks>
        /// 208 <c>0x1009029D</c> <c>cmp [cfg+0x6C0],0x1F4</c> /
        /// <c>0x100902B4 cmp [target+0x168],0</c> → 早退 <c>ret 1</c>。
        /// 207 交叉核实：<c>0x100827A4</c> 同一 <c>cmp [esi+0x168],0</c> 臂（键位
        /// <c>cfg+0x6A0</c> vs 208 <c>+0x6C0</c>，以 208 键名为准）。
        /// </remarks>
        internal static bool ShouldImmuneParalysisWhileStatusActive(TBaseObject target)
        {
            if (target == null)
                return false;
            if (!ToggleOn(null, "麻痹中不被麻痹a"))
                return false;
            // 208/207 均只测 bitset 首 dword（obj+0x168）。
            return target.m_nCharStatus != 0;
        }

        static bool ToggleOn(TPlayObject? player, string chineseKey)
        {
            var pm = M2Share.PluginManager;
            if (pm == null)
                return false;
            return new YanshenApi(player, null, pm).PatchToggleOn(chineseKey);
        }
    }
}
