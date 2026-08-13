using SystemModule;
using GameSvr;

namespace GameSvr.Plugins
{
    /// <summary>
    /// Trampoline-gated overlays that rewrite native MagXXX immediates.
    /// Off (json 0) must leave the host formula untouched — these helpers
    /// therefore all no-op when the matching config key is off.
    ///
    /// Packed S keys are group*1000+index (GetS 0x6DF1B4 via 0x6E42CC).
    /// The trampolines read the insertion-ordered pair array at [player+0x804]
    /// and check the packed key before the value; C# uses the keyed GetS
    /// equivalent, which is the player-visible contract after the plugin's
    /// S(1,1..150) fill. VMT gate 0x6AC8C8 = TPlayObject only.
    /// </summary>
    internal static class YanshenSkillPatches
    {
        // Packed keys confirmed from trampoline cmp immediates:
        // 0x43B=1083 S(1,83) 火球, 0x43C=1084 S(1,84) 雷电,
        // 0x434=1076 S(1,76) 爆裂, 0x437=1079 S(1,79) 雷光,
        // 0x43F=1087 S(1,87) 冰咆哮, 0x438=1080 S(1,80) 激光,
        // 0x442=1090 S(1,90) 火雨.
        public static void MainAttr(TPlayObject player, int magicId,
            int defaultLo, int defaultHi, out int lo, out int hi)
        {
            lo = defaultLo;
            hi = defaultHi;
            if (player == null || !MapMainAttr(magicId, out var toggle, out var index))
                return;
            if (!ToggleOn(player, toggle))
                return;
            if (!player.TryGetScriptVar('S', 1, index, out int v))
                return;
            // 100 → SC +0x29C/+0x2A0; 200 → DC +0x28C/+0x290; else MC.
            // Ice-storm trampoline 0x100D9E76: cmp esi,0x64 / jmp SC;
            // cmp esi,0xC8 / DC; fallback MC 0x76F2CB 8B BB 94 02 00 00.
            if (v == 100)
            {
                lo = HUtil32.LoWord(player.m_WAbil.SC);
                hi = HUtil32.HiWord(player.m_WAbil.SC);
            }
            else if (v == 200)
            {
                lo = HUtil32.LoWord(player.m_WAbil.DC);
                hi = HUtil32.HiWord(player.m_WAbil.DC);
            }
        }

        public static int Range(TPlayObject player, int magicId, int nativeDefault)
        {
            if (player == null || !MapRange(magicId, out var toggle, out var index))
                return PanguRange(player, magicId, nativeDefault);
            if (!ToggleOn(player, toggle))
                return PanguRange(player, magicId, nativeDefault);
            if (!player.TryGetScriptVar('S', 1, index, out int v) || v <= 0)
                return PanguRange(player, magicId, nativeDefault);
            return v;
        }

        /// <summary>
        /// 盘古页的四个范围开关改写的是同一个 range 槽，但走的是配置立即数而不是
        /// S 变量：插件 0x100B1FC0 里 <c>mov byte [0x76F271|0x76F643|0x76F301|
        /// 0x76F3BE], al</c> 覆盖宿主 <c>6A imm8</c>（爆裂/冰咆哮/火雨原版 1，
        /// 雷光原版 2），al = min(atoi(_范围值), 0xFF)。
        ///
        /// 与上面的 S 变量支路互斥：眼神的 trampoline 挂在
        /// 0x76F26B(7B)/0x76F300(9B)/0x76F63D(9B)，字节跨度把盘古的目标地址整个
        /// 罩住，同一时刻只会有一边生效。生产 config 里三个眼神键
        /// （爆裂火焰范围及系数 / 冰咆哮范围 / 地狱雷光范围）都是 0，所以这里把
        /// 盘古放在 S 变量之后：S 没给出覆盖值时才落到盘古的立即数。
        /// 火雨（59/63）没有对应的眼神键，只有盘古这一路。
        /// </summary>
        public static int PanguRange(TPlayObject player, int magicId,
            int nativeDefault)
        {
            if (player == null || !MapPanguRange(magicId, out var toggle))
                return nativeDefault;
            if (M2Share.PluginManager == null)
                return nativeDefault;
            var api = new YanshenApi(player, null, M2Share.PluginManager);
            switch (toggle)
            {
                case "盘古爆裂火焰范围":
                    return api.IsPgBlastFlameRange()
                        ? api.PgBlastFlameRangeVal() : nativeDefault;
                case "盘古地狱雷光范围":
                    return api.IsPgHellLightRange()
                        ? api.PgHellLightRangeVal() : nativeDefault;
                case "盘古冰咆哮的范围":
                    return api.IsPgIceStormRange()
                        ? api.PgIceStormRangeVal() : nativeDefault;
                default:
                    return api.IsPgFireRainRange()
                        ? api.PgFireRainRangeVal() : nativeDefault;
            }
        }

        /// <summary>
        /// 0x76FE44 reads range via `8A 45 14 mov al,[ebp+0x14]` — low 8 bits only.
        /// </summary>
        public static byte RangeByte(TPlayObject player, int magicId, int nativeDefault)
        {
            return unchecked((byte)Range(player, magicId, nativeDefault));
        }

        /// <summary>
        /// Fireball 0x100D9974 / lightning 0x100D9D45: S&gt;0 writes the range
        /// slot and `C7 45 xx 03 000000` (category 3 AREA). Off / S≤0 keeps
        /// the native (range, category) pair. Ice/blast/hell already start as 3.
        /// </summary>
        public static void ProducerDispatch(TPlayObject player, int magicId,
            int nativeRange, ushort nativeCategory,
            out byte range, out ushort category)
        {
            category = nativeCategory;
            range = unchecked((byte)nativeRange);
            if (player == null || !MapRange(magicId, out var toggle, out var index))
                return;
            if (!ToggleOn(player, toggle))
                return;
            if (!player.TryGetScriptVar('S', 1, index, out int v) || v <= 0)
                return;
            range = unchecked((byte)v);
            if (magicId == SpellsDef.SKILL_FIREBALL ||
                magicId == SpellsDef.SKILL_FIREBALL2 ||
                magicId == SpellsDef.SKILL_LIGHTENING)
                category = 3;
        }

        /// <summary>
        /// 地狱雷光 divisor. Host 0x76F61A is `B9 0A 000000 mov ecx,10`.
        /// Trampoline 0x100D8A3D: S(1,74)==0x432 and value&gt;0 replaces ecx.
        /// </summary>
        public static int HellLightDivisor(TPlayObject player)
        {
            const int native = 10;
            if (player == null || !ToggleOn(player, "地狱雷光系数"))
                return native;
            if (!player.TryGetScriptVar('S', 1, 74, out int v) || v <= 0)
                return native;
            return v;
        }

        /// <summary>
        /// 爆裂火焰 S(1,78) / 激光 S(1,89) 乘性系数。
        /// Native: if damage &gt;= 10000 then (damage/100)*N else (damage*N)/100,
        /// both idiv truncated. N&lt;=0 keeps the original.
        /// </summary>
        public static int ScaleDamage(TPlayObject player, int magicId, int damage)
        {
            if (player == null || !MapScale(magicId, out var toggle, out var index))
                return damage;
            if (!ToggleOn(player, toggle))
                return damage;
            if (!player.TryGetScriptVar('S', 1, index, out int n) || n <= 0)
                return damage;
            return ScaleByHundred(damage, n);
        }

        /// <summary>
        /// 嗜血术倍数. Host 0x76FC2B is `call [ebx+0xCC]` (GetAttackPower).
        /// TPlayObject → S(1,91); hero VMT → master TPlayObject S(1,92).
        /// Formula: imul / idiv 100, skipped when value &lt;= 0.
        /// </summary>
        public static int BloodSuck(TBaseObject caster, int damage)
        {
            if (caster is TPlayObject player)
            {
                if (!ToggleOn(player, "嗜血术倍数"))
                    return damage;
                if (!player.TryGetScriptVar('S', 1, 91, out int n) || n <= 0)
                    return damage;
                return ScaleByHundredSimple(damage, n);
            }

            if (caster is HeroObject hero && hero.m_Master is TPlayObject master)
            {
                if (!ToggleOn(master, "嗜血术倍数"))
                    return damage;
                if (!master.TryGetScriptVar('S', 1, 92, out int n) || n <= 0)
                    return damage;
                return ScaleByHundredSimple(damage, n);
            }

            return damage;
        }

        /// <summary>
        /// 野蛮等级. Host 0x768F67 is `movzx eax, word [eax+0x278]` (native level).
        /// Trampoline 0x100DB11C: S(1,95)==0x447 and value&gt;0 replaces eax.
        /// </summary>
        public static int BarbarianLevel(TPlayObject player, int nativeLevel)
        {
            if (player == null || !ToggleOn(player, "野蛮等级"))
                return nativeLevel;
            if (!player.TryGetScriptVar('S', 1, 95, out int v) || v <= 0)
                return nativeLevel;
            return v;
        }

        static bool ToggleOn(TPlayObject player, string key)
        {
            if (M2Share.PluginManager == null)
                return false;
            return new YanshenApi(player, null, M2Share.PluginManager)
                .PatchToggleOn(key);
        }

        static int ScaleByHundred(int damage, int n)
        {
            // Blast/laser trampoline: cmp eax,0x2710 / jl imul-first.
            if (damage >= 10000)
                return unchecked((damage / 100) * n);
            return ScaleByHundredSimple(damage, n);
        }

        static int ScaleByHundredSimple(int damage, int n)
        {
            // 2-operand `imul eax,edx` wraps to 32-bit, then `cdq; idiv 100`.
            return unchecked(damage * n / 100);
        }

        static bool MapMainAttr(int magicId, out string toggle, out int index)
        {
            switch (magicId)
            {
                case SpellsDef.SKILL_FIREBALL:
                case SpellsDef.SKILL_FIREBALL2:
                    toggle = "火球主属性切换"; index = 83; return true;
                case SpellsDef.SKILL_LIGHTENING:
                    toggle = "雷电主属性切换"; index = 84; return true;
                case SpellsDef.SKILL_SHOOTLIGHTEN:
                    toggle = "激光电影可换主属性"; index = 80; return true;
                case SpellsDef.SKILL_FIREBOOM:
                    toggle = "爆裂火焰可换主属性"; index = 76; return true;
                case SpellsDef.SKILL_LIGHTFLOWER:
                    toggle = "地狱雷光可换主属性"; index = 79; return true;
                case SpellsDef.SKILL_SNOWWIND:
                    toggle = "冰咆哮主属性切换"; index = 87; return true;
                case SpellsDef.SKILL_59:
                case SpellsDef.SKILL_63:
                    toggle = "火雨主属切换"; index = 90; return true;
                default:
                    toggle = null; index = 0; return false;
            }
        }

        static bool MapRange(int magicId, out string toggle, out int index)
        {
            switch (magicId)
            {
                case SpellsDef.SKILL_FIREBALL:
                case SpellsDef.SKILL_FIREBALL2:
                    toggle = "火球自定义范围"; index = 86; return true;
                case SpellsDef.SKILL_LIGHTENING:
                    toggle = "雷电自定义范围"; index = 85; return true;
                case SpellsDef.SKILL_FIREBOOM:
                    toggle = "爆裂火焰范围及系数"; index = 77; return true;
                case SpellsDef.SKILL_LIGHTFLOWER:
                    toggle = "地狱雷光范围"; index = 75; return true;
                case SpellsDef.SKILL_SNOWWIND:
                    toggle = "冰咆哮范围"; index = 88; return true;
                default:
                    toggle = null; index = 0; return false;
            }
        }

        /// <summary>
        /// 站点归属由 <c>call sub_76FE44</c> 的实参序列定回宿主函数：
        /// 0x76F270 在 sub_76F21C（爆裂火焰 23），0x76F300 在 sub_76F2AC
        /// （冰咆哮 33），0x76F3BD 在 sub_76F33C（59/63 共用体），
        /// 0x76F642 在地狱雷光体（0x76F61A `mov ecx,10` 的除法之后）。
        /// </summary>
        static bool MapPanguRange(int magicId, out string toggle)
        {
            switch (magicId)
            {
                case SpellsDef.SKILL_FIREBOOM:
                    toggle = "盘古爆裂火焰范围"; return true;
                case SpellsDef.SKILL_LIGHTFLOWER:
                    toggle = "盘古地狱雷光范围"; return true;
                case SpellsDef.SKILL_SNOWWIND:
                    toggle = "盘古冰咆哮的范围"; return true;
                case SpellsDef.SKILL_59:
                case SpellsDef.SKILL_63:
                    toggle = "盘古流星火雨范围"; return true;
                default:
                    toggle = null; return false;
            }
        }

        static bool MapScale(int magicId, out string toggle, out int index)
        {
            switch (magicId)
            {
                case SpellsDef.SKILL_FIREBOOM:
                    toggle = "爆裂火焰范围及系数"; index = 78; return true;
                case SpellsDef.SKILL_SHOOTLIGHTEN:
                    toggle = "激光范围及系数"; index = 89; return true;
                default:
                    toggle = null; index = 0; return false;
            }
        }
    }
}
