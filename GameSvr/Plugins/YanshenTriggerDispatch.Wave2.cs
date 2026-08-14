using GameSvr.PasEngine;

namespace GameSvr.Plugins
{
    /// <summary>
    /// 第二批接线：<c>@Herobaoji</c> / <c>@pickpre</c> / <c>@MyKill</c>。
    ///
    /// <para>三条的原生桩体都是本轮从眼神转储里逐字节解出来的
    /// （<c>staging/yanshen208_strparam_runtime_dump_20260719/yanshen2_0_8_dll.memory.bin</c>，
    /// 基址 0x10000000），解码规则见 <see cref="YanshenTriggerDispatch"/> 的族说明：
    /// 安装器 <c>0x10032DB0..0x10032E40</c> 逐个 dword 取低字节写机器码，只有
    /// 「<c>0xE8</c>/<c>0xE9</c> 后面紧跟一个 &gt; 0xFF 的 dword」才展开成 rel32，
    /// 数组末尾恒为 <c>0xE9</c> 由安装器补上回跳 resume 的 rel32。三条各自的模板地址、
    /// dword 数、逐条语义都写在注册表对应记录的 <c>Note</c> 里。</para>
    ///
    /// <para><b>两个跨条共用的字段判定</b>（三条里有两条要用）：</para>
    /// <list type="bullet">
    /// <item><c>[obj+0x128]</c> = <c>m_PEnvir</c>。全仓已定（<c>0x6DA125</c> 的 boNOMAGIC 读、
    /// <c>0x6EC0F8</c> 的 boMINE 读、<c>0x765D76</c> 的空指针门都走这个偏移）。</item>
    /// <item><c>[m_PEnvir+0x48]</c> = <c>sMapDesc</c>。由 <c>0x6EA452..0x6EA4B8</c> 的
    /// <c>Format</c> 实参向量定案：格式串 <c>0x6EA584</c> 是
    /// <c>'%s在%s[%d,%d]施放%s，请大家前往观看.'</c>，四个 TVarRec 依次是
    /// <c>vtString(obj+0x106)</c> / <c>vtAnsiString([[obj+0x128]+0x48])</c> /
    /// <c>vtInteger(obj+0x12C)</c> / <c>vtInteger(obj+0x130)</c>；仓库里这段的既有端口
    /// <c>TPlayObject.NativeFireworkText.cs</c> 第二个 <c>{1}</c> 用的正是
    /// <c>m_PEnvir.sMapDesc</c>（并有 <c>NativeFireworkTextCompatCheck</c> 守着）。</item>
    /// </list>
    /// </summary>
    public static partial class YanshenTriggerDispatch
    {
        /// <summary>
        /// 原生桩体取的是 <c>[[obj+0x128]+0x48]</c>，即**地图对象**的 sMapDesc，
        /// 不是 <c>obj.m_sMapName</c> 那份缓存。对象还没入图时原生会读到 nil 长串，
        /// Delphi 的 nil AnsiString 转 Variant 得到空串，这里用 <c>?? string.Empty</c> 对齐。
        /// </summary>
        private static string NativeMapDescOf(TBaseObject actor)
            => actor?.m_PEnvir?.sMapDesc ?? string.Empty;

        // ── 英雄倍攻和暴击（@Herobaoji，宿主 sub_76C804 的 0x76C816） ─────────────

        /// <summary>
        /// 英雄倍攻和暴击（<c>@Herobaoji</c>）。挂在 <c>TBaseObject.GetAttackPower</c>
        /// 的 <c>0x76C816</c>，即 nPower 钳零之后、幸运掷点之前，改写的是
        /// <b>nBasePower</b>（原生 edi），而不是 <c>@baoji</c> 那条改写的返回值（原生 esi）。
        /// <para>返回改写后的 nBasePower；任何一道门没过就原样返回入参。</para>
        /// </summary>
        public static int FireHerobaoji(TBaseObject attacker, int basePower)
        {
            // +0x000 `cmp ebx,0x400000` / +0x00C `cmp [ebx],0x6AC8C8`(排除 TPlayer) /
            // +0x018 `cmp [ebx],0x660E80`(排除 TWhiteSkeleton) / +0x024..+0x042 只放行
            // TTaosHero(0x685CA0) / TWarHero(0x685968) / TMagHero(0x685FD8) —— 这三个是
            // THeroAct(0x685630) 的全部直接子类，C# 侧对应的具体类就是 HeroObject
            // （TFieldHero 走 AnimalObject，不在这三个 VMT 里）。
            if (!Armed || attacker is not HeroObject hero) return basePower;

            // +0x056 `mov esi,[ebx+0x68C]` / +0x05C `cmp esi,0x410000` /
            // +0x068 `cmp [esi],0x6AC8C8` —— 主人必须在且必须是 TPlayer。
            if (hero.m_Master is not TPlayObject master) return basePower;
            if (!Enabled("英雄倍攻和暴击")) return basePower;

            // +0x089..+0x0A7 先验槽 48：key [bank+0x180]==0x419 且 value [bank+0x184]==0x522。
            // 键 = group*1000+index（sub_6E42CC），0x419=1049 → S(1,49)；0x522=1314。
            // 这一对就是插件 0x100CE4EA 播种 S(1,1..150) 时留下的印记 —— 原生靠它确认
            // 「银行是插件排布过的那一份」，随后才敢按裸偏移直读。没播种就整条不发射，
            // 与原生 key 对不上时 `jne` 全放弃一致。
            if (!master.TryGetScriptVar('S', 1, 49, out var seedMark) || seedMark != 1314)
                return basePower;

            // +0x0AD `mov ecx,[esi+0x15C]` = 槽 43 的 value = S(1,44)（播种后槽 n 即 index n+1）。
            // +0x0B3 `cmp ecx,0 / jle +0x0DF`：非正只跳过倍攻，仍继续暴击判定。
            if (master.TryGetScriptVar('S', 1, 44, out var powerRate) && powerRate > 0)
                basePower = ScaleHeroNoGuard(basePower, powerRate);

            // +0x0DF `[esi+0x164]` = 槽 44 = S(1,45)；+0x0EE `[esi+0x16C]` = 槽 45 = S(1,46)。
            // 两把键各自要求 > 0，否则 bail。
            if (!master.TryGetScriptVar('S', 1, 45, out var critRate) || critRate <= 0)
                return basePower;
            if (!master.TryGetScriptVar('S', 1, 46, out var critChance) || critChance <= 0)
                return basePower;

            // +0x0FD `mov eax,0x64` / +0x103 `call 0x403B4C` / +0x109 `cmp eax,edx` / jg bail。
            // 与 @baoji 同形：Random(100) 取 [0,99]，比较是 <=。
            if (M2Share.RandomNumber.Random(100) > critChance) return basePower;

            // +0x132 pushal / +0x134 `mov edx,[ebp-4]`(= 主人) / +0x150 `call [ebx+0x44]`。
            DispatchPlain(master, "@Herobaoji");
            return ScaleHeroSaturating(basePower, critRate);
        }

        /// <summary>
        /// 第一次缩放 +0x0BC..+0x0DD。<b>溢出保护是死代码</b>：<c>imul</c> 之后的
        /// <c>+0x0C1 E9 0B 00 00 00 jmp +0x0D1</c> 把 <c>+0x0C6 jo</c> 与
        /// <c>+0x0CC mov eax,0x7FFFFFFF</c> 整段跳了过去，所以乘积溢出时**不封顶**，
        /// 直接把截断后的 32 位值送进除法。也没有 <c>@baoji</c> 那两道
        /// <c>cmp …,0x3E8</c> 预压缩门。
        /// </summary>
        private static int ScaleHeroNoGuard(int value, int percent)
            => DivHundredNative(unchecked(value * percent));

        /// <summary>
        /// 第二次缩放 +0x111..+0x130。这次 <c>jo</c> 可达，但饱和值
        /// <c>0x7FFFFFFF</c> 是写进 <c>eax</c> 后**继续落到同一段除法**的
        /// （<c>+0x121 mov eax,0x7FFFFFFF</c> 直接 fall through 到 <c>+0x126</c>），
        /// 所以封顶结果是 <c>0x7FFFFFFF / 100 = 21474836</c>，不是 <c>int.MaxValue</c>。
        /// </summary>
        private static int ScaleHeroSaturating(int value, int percent)
            => DivHundredNative(TryMul(value, percent, out var product) ? product : int.MaxValue);

        // ── 捡物触发（@pickpre，宿主 ClientPickUpItem 的 0x6B770C） ───────────────

        /// <summary>
        /// 捡物触发（<c>@pickpre</c>）。纯通知，无门。原生桩体重放
        /// <c>8B 55 FC 8B C3</c>（<c>AddItemToBag</c> 的两条实参装载）后派发，
        /// 也就是说它跑在 <c>0x6B7713 call [vmt+0x248]</c> <b>之前</b>、
        /// <c>DeleteFromMap</c> 成功之后。金币臂在更早处就 return，不经过这里。
        /// </summary>
        /// <param name="picker">This_Player，原生 <c>ebx</c> = self。</param>
        /// <param name="stdItemName">
        /// 第一个 Variant：原生 <c>[[ebp-4]+0x1C]+4</c> 处的 ShortString，
        /// 即被捡物品的 <c>TStdItem</c> 名（TStdItem <c>+4</c> 起是 ShortString）。
        /// </param>
        public static void FirePickPre(TPlayObject picker, string stdItemName)
        {
            if (!Armed || picker == null) return;
            if (!Enabled("捡物触发")) return;

            DispatchWithParams(picker, "@pickpre",
                PasValue.FromString(stdItemName ?? string.Empty),
                PasValue.FromString(NativeMapDescOf(picker)));
        }

        // ── 被击杀触发（@MyKill，宿主 TCreature.Run 的 0x766624） ─────────────────

        /// <summary>
        /// 被击杀触发（<c>@MyKill</c>）。纯通知。原生挂在 <c>TCreature.Run</c> 里
        /// 「未死 → HP 见底 → 复活尝试失败」之后、<c>call [vmt+0x84]</c>（<c>Die</c>）
        /// <b>之前</b>，桩体开头重放 <c>8B 45 FC 8B 10</c>。
        /// <para>三道门：死者必须是 <c>TPlayer</c>；<c>m_ExpHitter</c>（原生 <c>+0x34C</c>）
        /// 必须非空且也必须是 <c>TPlayer</c>。所以怪物打死人、无凶手自然死都不发。</para>
        /// <para>This_Player 是<b>死者</b>，两个 Variant 都取自<b>凶手</b>。</para>
        /// </summary>
        public static void FireMyKill(TBaseObject dying)
        {
            if (!Armed) return;
            // +0x006 `cmp edx,0x6AC8C8 / jne bail`：[ebp-4] 必须是 TPlayer。
            if (dying is not TPlayObject victim) return;
            // +0x012 `mov ebx,[eax+0x34C]` / +0x018 `cmp ebx,0x400000 / jb bail`
            // / +0x024 `cmp [ebx],0x6AC8C8 / jne bail`。
            if (victim.m_ExpHitter is not TPlayObject killer) return;
            if (!Enabled("被击杀触发")) return;

            DispatchWithParams(victim, "@MyKill",
                PasValue.FromString(killer.m_sCharName ?? string.Empty),
                PasValue.FromString(NativeMapDescOf(killer)));
        }
    }
}
