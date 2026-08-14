using GameSvr.PasEngine;

namespace GameSvr.Plugins
{
    /// <summary>
    /// 第三批接线：<c>@MyAttack</c> / <c>@MyMagicAttack</c> / <c>@MagicAttack</c>。
    ///
    /// <para>这三条此前被记为「宿主函数未定名、<c>[ebp-4]</c>/<c>[ebp-8]</c> 语义未定」而
    /// fail-closed。本轮用「rel32 调用目标普查」把三个宿主函数的边界定了下来
    /// （<c>staging/_reunpack_work/flat_image.bin</c>，base 0x400000）：</para>
    /// <list type="bullet">
    /// <item><c>sub_76DE1C</c>（唯一 rel32 调用者 <c>0x766E06</c>）＝
    /// <see cref="TBaseObject.ApplyNativeSingleMagicEffect"/>；含 <c>@MyMagicAttack</c>
    /// 的 0x76DE84 与 <c>@MagicAttack</c> 的 0x76DEC0 两个挂载点。</item>
    /// <item><c>sub_76E0B4</c>（唯一 rel32 调用者 <c>0x766E6A</c>）＝
    /// <see cref="TBaseObject.ApplyNativeAreaMagicEffect"/>；含 <c>@MagicAttack</c>
    /// 的 0x76E1AF 挂载点。</item>
    /// <item><c>sub_76E268</c>（29 个 rel32 调用者）＝
    /// <see cref="TBaseObject.ApplyNativeDirectMagicEffect"/>；含 <c>@MyAttack</c>
    /// 的 0x76E35D 挂载点。该函数早已按 VA 逐行注释移植，本轮只是把它与三个触发键接上。</item>
    /// </list>
    ///
    /// <para><b>帧槽语义（逐条由宿主字节定死，不再是推断）：</b></para>
    /// <list type="bullet">
    /// <item><c>sub_76DE1C</c> 序言 <c>0x76DE24 mov [ebp-8],ecx</c> / <c>0x76DE27 mov [ebp-4],edx</c>
    /// / <c>0x76DE2A mov ebx,eax</c> ⇒ <c>[ebp-4]</c> = 被打者、<c>[ebp-8]</c> = 第三个寄存器实参、
    /// <c>ebx</c> = 施法者。<c>[ebp-8]</c> 的身份由 <c>0x76DEAB mov dx,word [ebp-8]</c> →
    /// <c>call 0x772468</c> 钉死 —— 该调用的 C# 端口就是
    /// <c>ConsumeNativeOneShotMagicDamage(payload.SkillId)</c>，故 <b><c>[ebp-8]</c> = SkillId</b>。</item>
    /// <item><c>sub_76E0B4</c> 序言同形，但 <c>[ebp-4]</c>/<c>[ebp-8]</c> 是 <b>X/Y</b>
    /// （<c>0x76E14B cmp [tgt+0x12C],[ebp-4]</c> / <c>0x76E156 cmp [tgt+0x130],[ebp-8]</c>
    /// 就是 <c>isCenter</c> 的判据），SkillId 改从栈参 <c>[ebp+0x14]</c> 来
    /// （<c>0x76E174 mov ecx,[ebp+0x14]</c> 正是喂给 <c>[vmt+0x104]</c> 的那一个）。</item>
    /// <item>两个 <c>@MagicAttack</c> 站点的实参向量因此<b>确认完全一致</b>：调用者
    /// <c>0x766E01</c> 把 <c>[ebx+0x24]</c> 装进 ecx 交给 <c>sub_76DE1C</c>，
    /// <c>0x766E4F</c> 把同一个 <c>[ebx+0x24]</c> 压成 <c>sub_76E0B4</c> 的 <c>[ebp+0x14]</c>。
    /// 这解掉了注册表里「无法确认两处参数向量完全一致」那条卡点。</item>
    /// </list>
    ///
    /// <para><b>一处必须写明的收窄</b>：三条桩体都<b>没有</b>对 This_Player 做类门
    /// （<c>mov edx,ebx</c> 直接把施法者当 This_Player 交给 <c>[vmt+0x48]</c>，
    /// 施法者是怪物时原生照发）。C# 的
    /// <c>NormNpc.TryCallPascalCallback(TPlayObject, …)</c> 签名无法承载非玩家的
    /// This_Player，故本端口<b>只在施法者是 <see cref="TPlayObject"/> 时发射</b>。
    /// 这是签名所迫的收窄，不是新加的原生门 —— 按「有证据才动」的规矩单独记在这里，
    /// 不写进注册表的 <c>Note</c> 冒充原生事实。</para>
    /// </summary>
    public static partial class YanshenTriggerDispatch
    {
        /// <summary>
        /// 第四个 Variant：原生 <c>mov dword [ebp-0x64],2</c>(varSmallint) 先落 0，
        /// 再用 <c>cmp dword [victim],0x6AC8C8</c> 命中才写 1。所以它是<b>整数 1/0</b>，
        /// 不是布尔——用 <c>FromInt</c> 保持脚本侧可见类型一致。
        /// </summary>
        private static PasValue NativeIsPlayerFlag(TBaseObject victim)
            => PasValue.FromInt(victim is TPlayObject ? 1 : 0);

        // ── 攻击触发（@MyAttack，宿主 sub_76E268 的 0x76E35D） ─────────────────────

        /// <summary>
        /// 攻击触发（<c>@MyAttack</c>）。纯通知，桩体<b>无门</b>，尾部重放被覆盖的
        /// <c>68 C8 00 00 00 push 0xC8</c> 后 <c>jmp 0x76E362</c>。
        /// <para>挂载点在 <c>0x76E357 cmp [ebp-8],0 / jle</c> 之后、
        /// <c>0x76E369 call 0x76B4F8</c>（落延时受击消息）之前，即 C# 的
        /// <c>if (damage &gt; 0)</c> 里、<c>SendDelayMsg</c> 之前。</para>
        /// <para>四个 Variant：①<c>[ebp-8]</c> 经 <c>0x41AFE4(cl=0xFC=-4 ⇒ varInteger)</c>
        /// = 伤害；②<c>[esi+0x106]</c> ShortString = 被打者 <c>m_sCharName</c>
        /// （<c>esi</c> = 第二个实参 = 被打者）；③<c>[[ebx+0x128]+0x48]</c> =
        /// 攻击者 <c>m_PEnvir.sMapDesc</c>；④varSmallint 的「被打者是玩家」1/0。</para>
        /// </summary>
        public static void FireMyAttack(TBaseObject attacker, TBaseObject victim,
            int damage)
        {
            if (!Armed || victim == null) return;
            if (attacker is not TPlayObject player) return;
            if (!Enabled("攻击触发")) return;

            DispatchWithParams(player, "@MyAttack",
                PasValue.FromInt(damage),
                PasValue.FromString(victim.m_sCharName ?? string.Empty),
                PasValue.FromString(NativeMapDescOf(attacker)),
                NativeIsPlayerFlag(victim));
        }

        // ── 魔法攻击触发（@MyMagicAttack，宿主 sub_76DE1C 的 0x76DE84） ────────────

        /// <summary>
        /// 魔法攻击触发（<c>@MyMagicAttack</c>）。纯通知。被覆盖的 6 字节
        /// <c>8B F0 85 F6 7E 2C</c> 被桩体<b>拆成两半重放</b>：开头 <c>mov esi,eax</c>
        /// 收下伤害，结尾用 <c>test esi,esi</c> 加两条 jmp 复现 <c>jle 0x76DEB6</c>。
        /// <para>所以它跑在「魔法伤害算完」与「伤害是否为正」之间，即 C# 的
        /// <c>ResolveFullMagicDamage</c> 返回之后、<c>if (damage &gt; 0)</c> 之前 ——
        /// <b>伤害为 0 或负也照发</b>。</para>
        /// <para>五个 Variant：①伤害；②被打者名；③施法者地图描述；
        /// ④被打者是玩家 1/0；⑤<c>[ebp-8]</c> = SkillId。</para>
        /// </summary>
        public static void FireMyMagicAttack(TBaseObject caster, TBaseObject victim,
            int damage, ushort skillId)
        {
            if (!Armed || victim == null) return;
            if (caster is not TPlayObject player) return;
            if (!Enabled("魔法攻击触发")) return;

            DispatchWithParams(player, "@MyMagicAttack",
                PasValue.FromInt(damage),
                PasValue.FromString(victim.m_sCharName ?? string.Empty),
                PasValue.FromString(NativeMapDescOf(caster)),
                NativeIsPlayerFlag(victim),
                PasValue.FromInt(skillId));
        }

        // ── 盘古魔法攻击触发（@MagicAttack，0x76E1AF 与 0x76DEC0 两处） ────────────

        /// <summary>
        /// 盘古魔法攻击触发（<c>@MagicAttack</c>）。纯通知，两个站点各发一次。
        /// 两处桩体都紧挨着 state-26 那道 <c>cmp byte [caster+0x1B6],0</c>
        /// （并在尾部原样重放它），也就是说它跑在
        /// <c>TryApplyNativeState26Single</c> <b>之前</b>：
        /// <list type="bullet">
        /// <item>0x76DEC0 在 <c>sub_76DE1C</c> 的 <c>cmp byte [ebp+8],0</c>（<c>payload.Arg0</c>）
        /// 之后 ⇒ C# 的 <c>if (payload.Arg0) { … }</c> 内；</item>
        /// <item>0x76E1AF 在 <c>sub_76E0B4</c> 的 <c>cmp byte [ebp-0x15],0</c>（<c>isCenter</c>）
        /// 之后 ⇒ C# 的 <c>if (isCenter) { … }</c> 内。</item>
        /// </list>
        /// <para>三个 Variant 两处同形：①varString 被打者名；②varSmallint 被打者是玩家 1/0；
        /// ③varInteger SkillId。</para>
        /// </summary>
        public static void FirePanguMagicAttack(TBaseObject caster,
            TBaseObject victim, ushort skillId)
        {
            if (!Armed || victim == null) return;
            if (caster is not TPlayObject player) return;
            if (!Enabled("盘古魔法攻击触发")) return;

            DispatchWithParams(player, "@MagicAttack",
                PasValue.FromString(victim.m_sCharName ?? string.Empty),
                NativeIsPlayerFlag(victim),
                PasValue.FromInt(skillId));
        }
    }
}
