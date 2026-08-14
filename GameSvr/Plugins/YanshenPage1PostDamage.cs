using GameSvr;

namespace GameSvr.Plugins
{
    /// <summary>
    /// 眼神2(第1页) · <c>sub_100795C0</c> 伤害后处理流水线中<b>已解语义、可落 C# 点</b>的键。
    ///
    /// <para>整页 34 键零 M2Server 补丁（<c>ys_page1_census.tsv patch_label=no</c>）。
    /// 20 键原版无第四处读点 ⇒ C# 仅 <see cref="YanshenApi"/> 访问器、无引擎消费者 =
    /// 1:1，<b>不得</b>为它们写实现（<c>YanshenPage1CensusCheck</c> 反臆造闸门）。</para>
    ///
    /// <para><c>sub_100795C0</c> 唯一静态 call 点：
    /// delayed 转储 <c>0x10F2D759 E8 … call 0x100795C0</c> / <c>add esp,0x18</c>
    /// （6 个 cdecl 实参）落在 Themida 远端区；宿主入口仍不可证。函数内序：
    /// 主号高级暴击 <c>0x10079FB1</c> → 高级英雄倍功暴击 <c>0x1007A014</c> →
    /// 英雄千分比免伤 <c>0x1007A8A7</c> → 五法术切割分发 <c>0x1007AEAD</c> →
    /// 返伤害 <c>0x1007BFDC mov eax,[ebp+0x10]</c>。</para>
    /// </summary>
    internal static class YanshenPage1PostDamage
    {
        /// <summary>烈火切割 magicId；DLL <c>0x1007AEF7 cmp id,0x3EF</c>。</summary>
        internal const int FireCuttingMagicId = 0x3EF;

        // 五切割臂：magicId / 配置键 / S(1,index) / 消费者 cmp 槽（cfg+off, 0x1F4）。
        // 共同前置（以冰咆哮臂 0x1007AF0C 为样板）：
        //   cmp [cfg+off],0x1F4 / jle skip
        //   attacker TPlayObject（0x1007A95E cmp [ebp+0x18],0x6AC8C8 在切割段前已筛）
        //   bank=[attacker+0x804]；[bank+0x180]==0x419(S键1049) 且 [bank+0x184]==0x522(1314)
        //   对应 C#：S(1,49)==1314（<see cref="TPlayObject.YanshenSeedLoginSVars"/>）
        //   槽 tag/value 对 → C#：TryGetScriptVar('S',1,index)&gt;0
        //   净效：add [ebp+0x10], slotValue（加法，无上限）0x1007AF67 add [ebp+0x10],ebx
        static readonly (int MagicId, string Toggle, int SIndex)[] SpellCuttingArms =
        {
            (SpellsDef.SKILL_SNOWWIND, "冰咆哮切割", 116),   // 0x1007AF12 cmp [cfg+0x688],0x1F4 ; id 33 0x1007AEF5
            (SpellsDef.SKILL_EARTHFIRE, "火墙切割", 117),    // 0x1007AF78 cmp [cfg+0x698],0x1F4 ; id 22 0x1007AEBD
            (FireCuttingMagicId, "烈火切割", 118),           // 0x1007AFDD cmp [cfg+0x690],0x1F4 ; id 0x3EF
            (SpellsDef.SKILL_LIGHTENING, "雷电术切割", 119), // 0x1007B043 cmp [cfg+0x68C],0x1F4 ; id 11 0x1007AED6
            (SpellsDef.SKILL_FIRECHARM, "火符切割", 120),    // 0x1007B0A6 cmp [cfg+0x694],0x1F4 ; id 13 0x1007AEE3
        };

        /// <summary>
        /// 英雄千分比免伤。守方是英雄类时读<b>主人</b>的 <c>S(1,58)</c>，向零截断减伤。
        /// </summary>
        /// <remarks>
        /// 208 <c>0x1007A8A7</c> <c>cmp [cfg+0x108],0x1F4</c> → 守方类
        /// <c>0x685CA0/0x685968/0x685FD8</c> → <c>[守方+0x68C]</c> 主人 →
        /// <c>GetS(主人,1,0x3A)</c> → <c>0 &lt; v ≤ 0x3E8</c> →
        /// <c>damage -= cvttsd2si(damage*v/1000.0)</c>。
        /// 207 交叉核实：同流水线 <c>0x1006DA87</c> 门 <c>[cfg+0x104],0x1F4</c>（208 键位
        /// <c>+0x108</c>，以 208 生产键为准）/ <c>0x1006DAD4 push 0x3A</c> 同一
        /// <c>S(1,58)</c> 语义。
        /// </remarks>
        internal static int ApplyHeroPermilleReduction(TBaseObject defender, int damage)
        {
            if (damage <= 0 || defender is not HeroObject hero)
                return damage;

            var master = hero.m_Master as TPlayObject;
            if (master == null || !ToggleOn(master, "英雄千分比免伤"))
                return damage;

            if (!master.TryGetScriptVar('S', 1, 58, out int permille) ||
                permille <= 0 || permille > 1000)
            {
                return damage;
            }

            int reduction = unchecked((int)Math.Truncate(
                damage * (permille / 1000.0d)));
            return unchecked(damage - reduction);
        }

        /// <summary>
        /// 五法术切割。C# 落点：<see cref="TBaseObject.ResolveFullMagicDamage"/> 内
        /// <c>ApplyNativeMagicCritical</c> 之后（与 DLL 内 crit→切割序一致）。
        /// 键关 ⇒ 整段跳过（<c>cmp …,0x1F4 / jle</c>）。
        /// </summary>
        internal static int ApplySpellCutting(TBaseObject source, TBaseObject target,
            int damage, int skillId)
        {
            _ = target;
            if (damage <= 0 || source is not TPlayObject attacker)
                return damage;

            // 0x1007AF46 cmp [bank+0x184],0x522
            if (!attacker.TryGetScriptVar('S', 1, 49, out int seed) || seed != 1314)
                return damage;

            foreach (var (magicId, toggle, sIndex) in SpellCuttingArms)
            {
                if (skillId != magicId)
                    continue;
                if (!ToggleOn(attacker, toggle))
                    return damage;
                if (!attacker.TryGetScriptVar('S', 1, sIndex, out int bonus) || bonus <= 0)
                    return damage;
                return unchecked(damage + bonus);
            }

            return damage;
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
