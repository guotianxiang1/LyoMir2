using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Magic 191 (凝冰) — port of sub_6EF340, 0x6EF340..0x6EF493.
    /// Delphi register convention: eax = Self (caster), edx = arg1, ecx = arg2.
    /// 0x6EF351 `mov ebx,ecx` makes ebx the TARGET and 0x6EF356 `mov esi,eax`
    /// makes esi the CASTER; ebx and esi are callee-saved across every call in
    /// the body, so which object each side effect lands on is unambiguous.
    ///
    /// arg1 ([ebp-4]) is only tested for nil at 0x6EF36A and never read again.
    /// It is the resolved magic record supplied by the dispatcher, so it has no
    /// counterpart in this signature.
    /// </summary>
    public partial class TBaseObject
    {
        /// <summary>0x6EF3AF / 0x6EF420 `mov edx,0xBF`.</summary>
        internal const int NativeMagic191Id = 191;

        /// <summary>The same 0xBF, used as the caster's cooldown-table key.</summary>
        internal const uint NativeMagic191ColdTimeKey = 0xBF;

        /// <summary>0x6EF396 / 0x6EF3F8 `mov dl,0x3E` — the state applied to the
        /// target, and the state whose prior presence refuses the cast.</summary>
        internal const byte NativeMagic191StateId = 0x3E;

        /// <summary>0x6EF385 `mov dl,0x10` — an independent refusal on the target.
        /// </summary>
        internal const byte NativeMagic191BlockingStateId = 0x10;

        /// <summary>0x6EF3F4 `mov cx,8`. sub_76B478 (VMT+0x1A8) widens it with
        /// `movzx eax,di` at 0x76B489 and converts with
        /// `imul ecx,eax,0x3E8` at 0x76B48C, so 8 is SECONDS and the node is
        /// created with 8000 ms — not 300000.</summary>
        internal const int NativeMagic191StateSeconds = 8;

        /// <summary>8 * 1000, the duration sub_7730D0 stores at node+2.</summary>
        internal const int NativeMagic191StateDurationMilliseconds =
            NativeMagic191StateSeconds * 1000;

        /// <summary>sub_76B478 forwards its own stack argument as the node VALUE
        /// (0x76B482 `movzx eax,word [ebp+8]` / 0x76B486 `push eax`, landing in
        /// [ebp+0xC] of sub_7730D0 and stored at node+0xA by 0x77316A). The call
        /// site pushes 0 at 0x6EF3F2, so the value is zero. The 8 is the duration,
        /// not the value.</summary>
        internal const int NativeMagic191StateValue = 0;

        /// <summary>0x6EF407 `add eax,0x493E0` — 300000 ms, written to the
        /// TARGET's protection deadline, not to any cooldown.</summary>
        internal const int NativeMagic191ProtectionMilliseconds = 0x493E0;

        /// <summary>Default cooldown from sub_6EF4F0 (VMT+0x14C):
        /// 0x6EF4F8 `mov esi,0x493E0`.</summary>
        internal const int NativeMagic191CooldownMilliseconds = 0x493E0;

        /// <summary>0x6EF4B0, 16 GBK bytes
        /// C4 BF B1 EA B4 A6 D3 DA B1 A3 BB A4 D7 B4 CC AC.</summary>
        internal const string NativeMagic191ProtectedMessage = "目标处于保护状态";

        /// <summary>0x6EF4CC (22 bytes) and 0x6EF4EC (2 bytes), joined around the
        /// remaining seconds by the three-part concat at 0x6EF463. The seconds come
        /// from `idiv 0x3E8` at 0x6EF449, i.e. truncating integer division.</summary>
        internal const string NativeMagic191CooldownMessagePrefix = "[凝冰]技能冷却时间还有";

        internal const string NativeMagic191CooldownMessageSuffix = "秒";

        /// <summary>obj+0x300, read at 0x6EF3C0 `mov eax,[ebx+0x300]` and written
        /// at 0x6EF40C `mov [ebx+0x300],eax`. ebx is the TARGET at both points, so
        /// this is a per-victim immunity window and belongs on TBaseObject: any
        /// actor can be the victim.</summary>
        public int m_dwMagic191FreezeDeadline;

        /// <summary>
        /// sub_6EF340. Returns the byte at [ebp-5]: 0 on every refusal, 1 only
        /// after the state, the deadline and the cooldown have all been written.
        /// </summary>
        internal bool TryActivateNativeMagic191(TBaseObject target, int now)
        {
            // 0x6EF378 call sub_767498(caster, target).
            if (!IsNativeMagic191ProperTarget(target))
            {
                return false;
            }

            // 0x6EF389 / 0x6EF39A: both are `jne` on the TARGET's bit, so either
            // one already set refuses silently.
            if (target.HasNativeActiveState(NativeMagic191BlockingStateId) ||
                target.HasNativeActiveState(NativeMagic191StateId))
            {
                return false;
            }

            // 0x6EF3B8 call [caster.vmt+0x1F4] — the cooldown is queried on the
            // CASTER while the deadline below is read from the TARGET.
            var coldRemaining = QueryNativeColdTime(NativeMagic191ColdTimeKey);

            // 0x6EF3C6 `cmp eax,[ebp-0xC]` / 0x6EF3C9 `ja` is UNSIGNED, and the
            // else-arm zeroes eax outright, so an already-past deadline can never
            // produce a positive remainder.
            var protectionRemaining = 0;
            if (unchecked((uint)target.m_dwMagic191FreezeDeadline) >
                unchecked((uint)now))
            {
                protectionRemaining =
                    unchecked(target.m_dwMagic191FreezeDeadline - now);
            }
            // 0x6EF3D4 `jle` — zero or negative falls through to the cooldown test.
            if (protectionRemaining > 0)
            {
                SendNativeMagic191Notice(NativeMagic191ProtectedMessage);
                return false;
            }

            // 0x6EF3F0 `jne`, not `jle`: ANY nonzero remainder refuses, including a
            // negative one that a `jle` reading would have let through.
            if (coldRemaining != 0)
            {
                SendNativeMagic191Notice(NativeMagic191CooldownMessagePrefix
                    + coldRemaining / 1000
                    + NativeMagic191CooldownMessageSuffix);
                return false;
            }

            // 0x6EF3FE call [target.vmt+0x1A8] -> sub_76B478 -> [vmt+0x1EC] =
            // sub_7730D0, which prepends to obj+0xDC. That list is the timed
            // ability list (m_TimedAbilityHead), so this must not go through the
            // TBaseObject.NativeBodyStateDuration scaffolding: native has one
            // duration list and one walker.
            target.AddTimedAbilityInternal(NativeMagic191StateId,
                NativeMagic191StateValue,
                NativeMagic191StateDurationMilliseconds, 0);

            // 0x6EF40C — on the TARGET.
            target.m_dwMagic191FreezeDeadline =
                unchecked(now + NativeMagic191ProtectionMilliseconds);

            // 0x6EF429 call [caster.vmt+0x1F0] with edx = 0xBF, ecx = the value
            // sub_6EF4F0 returned, and Total pushed as 0 at 0x6EF412.
            ArmNativeColdTime(NativeMagic191ColdTimeKey,
                GetNativeMagic191CooldownMilliseconds(), 0);

            // 0x6EF42F `mov byte [esi+0x308],0` clears a caster byte this port has
            // not mapped; see the note on NativeMagic191CasterResetOffset.
            return true;
        }

        /// <summary>caster+0x308, zeroed at 0x6EF42F on the success path only. The
        /// field has no identified C# counterpart, so the store is not reproduced.
        /// </summary>
        internal const int NativeMagic191CasterResetOffset = 0x308;

        /// <summary>
        /// sub_6EF4F0, the VMT+0x14C override carried only by the two player-side
        /// classes (VMT 0x62EF8C and 0x6AC8C8; every other THumanKind descendant
        /// holds sub_770590, a bare `xor eax,eax`):
        ///   0x6EF4F8  mov esi,0x493E0            ; 300000 default
        ///   0x6EF4FF  mov dx,0xBF / call [vmt+0xE8]  ; resolve the caster's record
        ///   0x6EF514  je  0x6EF534               ; no record -> default
        ///   0x6EF519  call 0x4C896C              ; effective level
        ///   0x6EF51E  dec al / je -> esi=0x493E0 ; level 1 -> 300000
        ///   0x6EF522  dec al / je -> esi=0x1D4C0 ; level 2 -> 120000
        ///   otherwise                            ; -> 300000
        /// Only level 2 diverges. sub_4C896C computes
        /// min(rec[+0xC] + rec[+0x18], rec[+0x00][+0x1A]) and none of those three
        /// offsets has been matched to a TUserMagic/TMagic member, so the level-2
        /// arm is deliberately not guessed: this returns the default and is virtual
        /// so the branch can be added once the record layout is pinned.
        /// </summary>
        protected virtual int GetNativeMagic191CooldownMilliseconds()
            => NativeMagic191CooldownMilliseconds;

        /// <summary>
        /// The refusal notices at 0x6EF3E3 and 0x6EF473, both
        /// `mov cx,0xFFDB` + `call [caster.vmt+0xD4]`. sub_73C8F4 forwards ecx and
        /// stamps ident 0x2774; cx unpacks as FColor 0xDB / BColor 0xFF, which is
        /// the btGreenMsgFColor / btGreenMsgBColor pair, i.e. MsgColor.Green — the
        /// same reading LeaveTechCommand.cs:29 already established for this literal.
        /// Both notices go to the CASTER (eax = esi at 0x6EF3DF and 0x6EF46F).
        /// </summary>
        protected virtual void SendNativeMagic191Notice(string message)
            => SysMsg(message, MsgColor.Green, MsgType.Hint);

        /// <summary>
        /// sub_767498, the shared target filter, in its own instruction order:
        ///   0x7674A1  test esi,esi / je                      ; nil
        ///   0x7674A7  call sub_772DA8 -> byte [target+0x74]  ; death
        ///   0x7674B0  cmp byte [target+0x73],0               ; ghost
        ///   0x7674B6  cmp edi,esi                            ; target == self
        ///   0x7674BA  cmp byte [target+0x2E0],0              ; UNMAPPED
        ///   0x7674C3  cmp byte [target+0x2E5],0              ; UNMAPPED
        ///   0x7674CC  cmp [target+0x128],[self+0x128]        ; same environment
        ///   0x7674DA  HasState(target, 0x34)                 ; two-seat mount
        ///   0x7674E7  add al,0x10 / sub al,2 / jae           ; kind, rejects 0xF0/0xF1
        ///   0x7674F7  call [self.vmt+0x20]                   ; per-class hook
        /// The two byte fields at +0x2E0 and +0x2E5 have no identified C# member
        /// and the VMT+0x20 hook is not reproduced here, so this filter is a
        /// SUBSET of the native one: it can only be more permissive, never less.
        /// </summary>
        private bool IsNativeMagic191ProperTarget(TBaseObject target)
        {
            if (target == null || ReferenceEquals(target, this))
            {
                return false;
            }
            if (target.m_boGhost || target.m_boDeath)
            {
                return false;
            }
            if (!ReferenceEquals(target.m_PEnvir, m_PEnvir))
            {
                return false;
            }
            if (target.HasNativeActiveState(0x34))
            {
                return false;
            }
            var kind = unchecked((byte)(target.m_btRaceServer + 0x10));
            return kind >= 2;
        }
    }
}
