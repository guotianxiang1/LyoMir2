using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic id 191 (凝冰). DoSpell trampoline @0x6EDFCF pushes nTargetX,
    /// nTargetY and the literal 0xBF, then calls TPlayer VMT+0x148:
    ///   006EDFE5  ff 97 48 01 00 00  call dword ptr [edi+0x148]
    ///   006EDFEB  34 01              xor al,1
    ///   006EDFED  88 45 fa           mov [ebp-6],al
    /// so a false return is a hard reject (0x27F, mana already spent).
    /// TPlayer VMT+0x148 = 0x6EF340 (VMT base 0x6AC8C8 passes the Delphi
    /// self-pointer check dword[V-0x4C] == V); THumanKind's inherited
    /// 0x770594 is overridden. All three stack arguments are dead in the
    /// callee, which is why none of them appear below.
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>Cooldown key, the literal 0xBF pushed at 0x6EDFD7 and
        /// reloaded as edx at 0x6EF3AF / 0x6EF420.</summary>
        private const int NativeSkill191ColdTimeKey = 0xBF;

        /// <summary>State the target picks up. 0x6EF3F8 `mov dl,0x3E`.</summary>
        private const byte NativeSkill191FreezeState = 0x3E;

        /// <summary>0x6EF3F4 `mov cx,8`, and VMT+0x1A8 = 0x76B478 turns that
        /// into milliseconds with `imul ecx,eax,0x3E8` @0x76B48C before
        /// handing it to the real add at VMT+0x1EC. Eight SECONDS — the
        /// 0x493E0 next to it is the target's immunity window, not this.
        /// </summary>
        private const int NativeSkill191FreezeMilliseconds = 8 * 1000;

        /// <summary>0x6EF407 `add eax,0x493E0` written into target+0x300.
        /// </summary>
        private const int NativeSkill191ImmuneWindowMilliseconds = 300000;

        /// <summary>Blocked when already present: 0x6EF385 `mov dl,0x10`.
        /// </summary>
        private const byte NativeSkill191BlockingState = 0x10;

        /// <summary>VMT+0x14C = 0x6EF4F0. esi is seeded with 0x493E0
        /// @0x6EF4F8 and only the two `dec al` arms move it, so level 1,
        /// level 0, level >= 3 and "player has not learnt 191" all end up on
        /// the same 300000.</summary>
        private const int NativeSkill191CooldownDefault = 0x493E0;

        /// <summary>0x6EF52F `mov esi,0x1D4C0`, reached by the second
        /// `dec al` — i.e. effective level 2 only.</summary>
        private const int NativeSkill191CooldownLevel2 = 0x1D4C0;

        /// <summary>target+0x300, an ABSOLUTE tick. Read at 0x6EF3C0 and
        /// stamped at 0x6EF40C; nothing clears it, so a frozen target stays
        /// immune for five minutes from the moment it was frozen.</summary>
        internal int m_dwNativeSkill191ImmuneUntil;

        internal bool TryActivateNativeSkill191Freeze(TUserMagic userMagic,
            TBaseObject target)
        {
            return TryActivateNativeSkill191Freeze(userMagic, target,
                HUtil32.GetTickCount());
        }

        internal bool TryActivateNativeSkill191Freeze(TUserMagic userMagic,
            TBaseObject target, int now)
        {
            // 0x6EF36A `cmp [ebp-4],0` / je out. The UserMagic is only used
            // as a presence test; the level comes from the player's own magic
            // list further down, not from this pointer.
            if (userMagic == null)
            {
                return false;
            }

            // 0x6EF378 call sub_767498, 0x6EF389 / 0x6EF39A the two state
            // probes (sub_772960). Order matters: all three run before the
            // cooldown is even read, so a bad target never burns the message.
            if (!IsProperTarget(target) ||
                target.HasNativeActiveState(NativeSkill191BlockingState) ||
                target.HasNativeActiveState(NativeSkill191FreezeState))
            {
                return false;
            }

            // 0x6EF3B8 VMT+0x1F4, read BEFORE the immunity test but reported
            // after it.
            int coolDownRemaining =
                GetNativeColdTimeRemaining(NativeSkill191ColdTimeKey);

            // 0x6EF3C6 `cmp eax,[ebp-0xC]` / `ja` is UNSIGNED, and the
            // `test eax,eax` / `jle` that follows is signed, so a window more
            // than 2^31 ms ahead silently reads as expired. Reproduced rather
            // than normalised.
            int immuneRemaining =
                unchecked((uint)target.m_dwNativeSkill191ImmuneUntil) >
                    unchecked((uint)now)
                    ? unchecked(target.m_dwNativeSkill191ImmuneUntil - now)
                    : 0;
            if (immuneRemaining > 0)
            {
                SendNativeSkill191Hint("目标处于保护状态");
                return false;
            }

            if (coolDownRemaining != 0)
            {
                // 0x6EF443 `mov ecx,0x3E8` / 0x6EF448 `cdq` / `idiv ecx` —
                // SIGNED, unlike id 151's unsigned `div` at 0x745A6F.
                int seconds = coolDownRemaining / 1000;
                SendNativeSkill191Hint(
                    $"[凝冰]技能冷却时间还有{seconds}秒");
                return false;
            }

            // 0x6EF3FE VMT+0x1A8 on the TARGET (eax = ebx = target), value 0
            // and the trailing word argument 0 both pushed at 0x76B486.
            target.AddTimedAbilityInternal(NativeSkill191FreezeState, 0,
                NativeSkill191FreezeMilliseconds, 0);
            target.m_dwNativeSkill191ImmuneUntil =
                unchecked(now + NativeSkill191ImmuneWindowMilliseconds);
            SetNativeColdTime(NativeSkill191ColdTimeKey,
                ResolveNativeSkill191CooldownMilliseconds(), now);
            // 0x6EF42F `mov byte [esi+0x308],0` also runs here. The field has
            // no located reader, so modelling it would add a write-only
            // member; recorded instead of guessed.
            return true;
        }

        /// <summary>
        /// VMT+0x14C = sub_6EF4F0. Note it re-finds the magic by id 0xBF
        /// through VMT+0xE8 (sub_741628, first match on word[[rec]+0x10],
        /// cl = 0 so no hotkey filter) instead of using the UserMagic the
        /// cast came in with.
        ///   006EF4F8  be e0 93 04 00  mov esi,0x493E0
        ///   006EF507  ff 93 e8 00 00 00  call [ebx+0xE8]   (dx=0xBF, ecx=0)
        ///   006EF514  74 1e              je  0x6EF534      (not learnt)
        ///   006EF519  e8 ..              call 0x4C896C     (effective level)
        ///   006EF51E  fe c8 / 74 06      dec al; je -> 0x493E0
        ///   006EF522  fe c8 / 74 09      dec al; je -> 0x1D4C0
        ///   006EF526  eb 0c              jmp 0x6EF534      (fall through)
        /// </summary>
        private int ResolveNativeSkill191CooldownMilliseconds()
        {
            TUserMagic magic = FindNativeSkill191Magic();
            if (magic == null)
            {
                return NativeSkill191CooldownDefault;
            }

            int effectiveLevel =
                TPlayObject.GetNativeMagicProducerEffectiveLevel(magic);
            return effectiveLevel == 2
                ? NativeSkill191CooldownLevel2
                : NativeSkill191CooldownDefault;
        }

        private TUserMagic FindNativeSkill191Magic()
        {
            if (m_MagicList == null)
            {
                return null;
            }
            for (var index = 0; index < m_MagicList.Count; index++)
            {
                TUserMagic candidate = m_MagicList[index];
                if (candidate?.MagicInfo != null &&
                    candidate.MagicInfo.wMagicID == SpellsDef.SKILL_191)
                {
                    return candidate;
                }
            }
            return null;
        }

        /// <summary>VMT+0xD4 with cx = 0xFFDB at 0x6EF3D6 and 0x6EF46B.
        /// </summary>
        private void SendNativeSkill191Hint(string text)
        {
            if (this is TPlayObject)
            {
                SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, 0xDB, 0xFF, 0, text);
            }
        }
    }
}
