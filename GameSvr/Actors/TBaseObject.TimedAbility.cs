using System.IO;
using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        private const int TimedAbilityMessage = 3555;
        private const int TimedAbilityListMessage = 3554;
        private const uint TimedAbilityProcessInterval = 500;
        private const byte TimedAbilityValueGateState = 16;
        private const byte TimedAbilityGlobalBlockState = 52;

        private sealed class TimedAbilityNode
        {
            public byte Flag;
            public byte InternalType;
            public int RemainingMilliseconds;
            public int LastTick;
            public int Value;
            public TimedAbilityNode Next;
        }

        private TimedAbilityNode m_TimedAbilityHead;
        private int m_TimedAbilityProcessTick;

        /// <summary>
        /// 眼神中文隧道 `!!!!hq取sj戳` 读的就是本字段（原生 <c>[obj+0xE0]</c>）：
        /// 0x1005E68A <c>8B80E0000000 mov eax,[Self+0xE0]</c>。
        /// 它不是"当前时间"，而是状态走查闩 —— 0x772FF5 <c>mov [ebx+0xE0],esi</c>
        /// 每轮走查用 GetTickCount 硬写一次，走查本身被 0x772FEA <c>cmp eax,0x1F4</c>
        /// 限制成 500 ms 一次（本文件 STATE-08/09，见 docs/eqv_shard11_20260814.md）。
        /// 只读暴露给插件层，不给写口。
        /// </summary>
        internal int NativeTimedAbilityLatchTick => m_TimedAbilityProcessTick;

        public void AddTimedAbility(int scriptType, int value, int seconds)
        {
            if (!IsSupportedTimedAbilityType(scriptType))
            {
                return;
            }

            var internalType = (byte)(scriptType + 32);
            var duration = seconds == -1 ? -1 : unchecked(seconds * 1000);
            AddTimedAbilityInternal(internalType, value, duration, 0);
        }

        /// <summary>
        /// Native <c>MakePosion</c> = VMT+0xC8 @0x76B3C8 taken by state id rather
        /// than by legacy poison slot. The public <see cref="MakePosion"/> gates on
        /// <c>nType &lt; MAX_STATUS_ATTRIBUTE</c> (12) because it also mirrors into
        /// the legacy <c>m_wStatusTimeArr</c>; native has no such gate — it hands
        /// <c>dl</c> straight to AddState. TDebuffTrapEvent needs ids 0x11 and 0x18,
        /// which have no legacy slot (slots 0..11 map to state ids 31..20), so there
        /// is nothing to mirror and the gate would just swallow the call.
        /// <para>
        /// Body reproduced: @0x76B413 <c>imul ecx, eax, 0x3E8</c> (seconds to ms)
        /// then @0x76B41F <c>call [ebx+0x1EC]</c> = AddState(id, ms, flag 0, value).
        /// The ImmuneCheck (@0x76B3D8), the state-0x34 veto (@0x76B3E1) and the
        /// "id 0x12 removes 0x1A" companion (@0x76B3EE) all live inside
        /// CanAddNativeTimedAbility / AddTimedAbilityInternal already.
        /// </para>
        /// </summary>
        internal bool ApplyNativeStateSeconds(byte stateId, int seconds, int value)
        {
            return AddTimedAbilityInternal(stateId, value,
                unchecked(seconds * 1000), 0);
        }

        // 原生 AddState = VMT+0x1EC @0x7730D0，形参 (eax=self, edx=stateId,
        // ecx=durationMs, [ebp+0xC]=value, [ebp+8]=flag)。它是**虚槽**，直接调用点遍布
        // 引擎（0x76B41F MakePosion、0x7732C3/0x773342/0x77335B 状态联动、0x5FABC5
        // TStoneFoxBossMon.Initialize 等），不止 poison 一条路，所以子类需要能直接调它。
        internal bool AddTimedAbilityInternal(byte internalType, int value,
            int duration, byte newNodeFlag)
        {
            if (!CanAddNativeTimedAbility(internalType))
            {
                return false;
            }

            var node = FindTimedAbilityInternal(internalType);
            var abilityChanged = false;

            if (node == null)
            {
                node = new TimedAbilityNode
                {
                    Flag = newNodeFlag,
                    InternalType = internalType,
                    RemainingMilliseconds = duration,
                    Value = value,
                    Next = m_TimedAbilityHead
                };
                m_TimedAbilityHead = node;
                abilityChanged = true;
            }
            else
            {
                node.Flag = newNodeFlag;
                if (value > node.Value)
                {
                    node.Value = value;
                    node.RemainingMilliseconds = duration;
                    abilityChanged = true;
                }
                else if (value == node.Value && duration > node.RemainingMilliseconds)
                {
                    node.RemainingMilliseconds = duration;
                }
            }

            if (abilityChanged)
            {
                if ((node.InternalType == 0 || node.InternalType == 45) &&
                    this is TPlayObject player)
                {
                    // STATE-26(a) — SetState @0x772974 pre-hook, bytes verified:
                    //   77297F  84 C0                 test al, al     ; id==0 ?
                    //   772981  74 04                 je   0x772987   ; yes -> hook
                    //   772983  2C 2D                 sub  al, 0x2D   ; id==0x2D ?
                    //   772985  75 0C                 jne  0x772993
                    //   772987  33 D2                 xor  edx, edx
                    //   77298D  FF 91 D8 01 00 00     call [ecx+0x1D8]
                    // edx is always 0, so both ids run the same virtual.
                    // TPlayObject VMT 0x6AC8C8+0x1D8 = 0x6EE2AC (cmp byte
                    // [self+0x1914],0 / clear pending / SM 0xD57=3415), which
                    // is CancelNativeType51PendingForTimedAbility. Default
                    // VMT+0x1D8 is 0x772A98 `ret`. The hook lives inside
                    // abilityChanged because native SetState is only reached
                    // from VMT+0x60 (0x77327C), which AddState calls only on
                    // a new node or a higher value (0x773131 / 0x773189).
                    player.CancelNativeType51PendingForTimedAbility();
                }
                SetNativeActiveState(node.InternalType);
                ApplyNativeTimedAbilityMutation(node.InternalType);
                if (node.InternalType == 18 && HasNativeActiveState(NativeState26Type))
                {
                    RemoveTimedAbilityInternal(NativeState26Type);
                }
                if (node.InternalType == 20 && node.Value > 3)
                {
                    // STATE-29 @0x7732AC, bytes verified:
                    //   7732AC  83 7E 0A 03            cmp  dword [esi+0xA], 3
                    //   7732B0  0F 8E AB 00 00 00      jle  0x773361
                    //   7732B6  6A 01                  push 1          ; -> [ebp+0xC]
                    //   7732B8  6A 00                  push 0          ; -> [ebp+8]
                    //   7732BA  83 C9 FF               or   ecx, -1    ; permanent
                    //   7732BD  B2 13                  mov  dl, 0x13
                    //   7732C3  FF 97 EC 01 00 00      call [edi+0x1EC]
                    // AddState reads value from [ebp+0xC] (0x7730E0 mov edi,[ebp+0xc])
                    // and flag from [ebp+8] (0x77310C mov dl,[ebp+8]), so the first push
                    // is the value and the second is the flag: value=1, flag=0. The two
                    // were swapped, which left GetNativeTimedAbilityValue(19) returning 0
                    // so every consumer that tiers on state 0x13's level fell to the
                    // bottom tier, and set Flag=1 on a record native leaves at 0.
                    AddTimedAbilityInternal(19, 1, -1, 0);
                }
                if (node.InternalType == 50)
                {
                    // STATE-30 — gained mutation @0x77332E, bytes verified:
                    //   77328F  8A 46 01 / 2C 12 / 74 7F   id==0x12
                    //   773297  2C 02 / 74 11              id==0x14
                    //   77329B  2C 06 / 74 2F              id==0x1A
                    //   77329F  2C 18 / 0F 84 87 00 00 00  id==0x32 -> 0x77332E
                    //   77332E  8A 43 72                   mov  al, byte [ebx+0x72] ; m_btJob
                    //   773331  3C 01 / 75 15              cmp  al,1 / jne job2
                    //   773335  6A 01 / 6A 00 / 83 C9 FF   value=1, flag=0, ecx=-1
                    //   77333C  B2 07                      mov  dl, 7
                    //   773342  FF 97 EC 01 00 00          call [edi+0x1EC] AddState
                    //   77334A  3C 02 / 75 13              cmp  al,2 / jne skip
                    //   773355  B2 08                      mov  dl, 8
                    //   77335B  FF 97 EC 01 00 00          call [edi+0x1EC]
                    // Job 0 and any other value do nothing. Permanent (ecx=-1).
                    if (m_btJob == 1)
                    {
                        AddTimedAbilityInternal(7, 1, -1, 0);
                    }
                    else if (m_btJob == 2)
                    {
                        AddTimedAbilityInternal(8, 1, -1, 0);
                    }
                }
            }

            if (abilityChanged && RequiresTimedAbilityRecalc(node.InternalType))
            {
                MarkAbilityRecalcPending();
            }
            SendTimedAbilityState(node, false);
            node.LastTick = HUtil32.GetTickCount();
            return true;
        }

        internal bool AddNativeBubbleTimedAbility(byte level, ushort seconds)
        {
            if (HasNativeActiveState(20))
            {
                return false;
            }

            AddTimedAbilityInternal(20, level,
                unchecked(seconds * 1000), 0);
            return true;
        }

        public void ProcessTimedAbilities()
        {
            ProcessTimedAbilities(HUtil32.GetTickCount());
        }

        public void ProcessTimedAbilities(int now)
        {
            ProcessNativeSkill152Status(now);
            if (unchecked((uint)(now - m_TimedAbilityProcessTick)) <
                TimedAbilityProcessInterval)
            {
                return;
            }
            m_TimedAbilityProcessTick = now;

            TimedAbilityNode previous = null;
            var node = m_TimedAbilityHead;
            TimedAbilityNode expiredHead = null;

            while (node != null)
            {
                var next = node.Next;
                var expired = false;
                if (node.RemainingMilliseconds != -1)
                {
                    node.RemainingMilliseconds = unchecked(
                        node.RemainingMilliseconds - (now - node.LastTick));
                    node.LastTick = now;
                    expired = node.RemainingMilliseconds <= 0;
                }

                if (expired)
                {
                    ClearNativeActiveState(node.InternalType);
                    if (previous == null)
                    {
                        m_TimedAbilityHead = next;
                    }
                    else
                    {
                        previous.Next = next;
                    }
                    node.Next = expiredHead;
                    expiredHead = node;
                }
                else
                {
                    previous = node;
                }
                node = next;
            }

            // Native first detaches the whole expired batch, then invokes callbacks
            // through the reversed temporary list (oldest state first).
            node = expiredHead;
            while (node != null)
            {
                var next = node.Next;
                SendTimedAbilityState(node, true);
                OnNativeTimedStateLost(node.InternalType);
                RemoveTimedAbilityCompanion(node.InternalType);
                if (RequiresTimedAbilityRecalc(node.InternalType))
                {
                    MarkAbilityRecalcPending();
                }
                node = next;
            }
        }

        public bool RemoveTimedAbility(int scriptType)
        {
            if (!IsSupportedTimedAbilityType(scriptType))
            {
                return false;
            }

            var internalType = (byte)(scriptType + 32);
            return RemoveTimedAbilityInternal(internalType);
        }

        /// <summary>
        /// Narrow assembly-internal entry for native callers that already hold
        /// the internal state id, such as sub_6D321C removing state 25.
        /// </summary>
        internal bool RemoveNativeTimedAbilityByInternalType(byte internalType)
            => RemoveTimedAbilityInternal(internalType);

        private bool RemoveTimedAbilityInternal(byte internalType)
        {
            if (!HasNativeActiveState(internalType))
            {
                return false;
            }

            ClearNativeActiveState(internalType);
            TimedAbilityNode previous = null;
            var node = m_TimedAbilityHead;
            while (node != null)
            {
                if (node.InternalType == internalType)
                {
                    if (previous == null)
                    {
                        m_TimedAbilityHead = node.Next;
                    }
                    else
                    {
                        previous.Next = node.Next;
                    }

                    SendTimedAbilityState(node, true);
                    OnNativeTimedStateLost(node.InternalType);
                    RemoveTimedAbilityCompanion(node.InternalType);
                    if (RequiresTimedAbilityRecalc(node.InternalType))
                    {
                        MarkAbilityRecalcPending();
                    }
                    return true;
                }

                previous = node;
                node = node.Next;
            }
            return false;
        }

        private void RemoveTimedAbilityCompanion(byte internalType)
        {
            if (internalType == 20)
            {
                RemoveTimedAbilityInternal(19);
            }
        }

        /// <summary>
        /// Per-state work on expiry. Native runs this from the state-lost
        /// virtual (base 0x77337C, TPlayObject override 0x741578) and dispatches
        /// on the record's type byte at 0x742692:
        ///   0x742692  33 C0                 xor eax, eax
        ///   0x742694  8A C3                 mov al, bl          ; node type
        ///   0x742696  83 C0 F2              add eax, -0xE
        ///   0x742699  83 F8 5C              cmp eax, 0x5C
        ///   0x74269C  0F 87 A0 05 00 00     ja  0x742C42        ; silent default
        ///   0x7426A2  FF 24 85 A9 26 74 00  jmp [eax*4+0x7426A9]
        /// so the message domain is state 14..106 and everything outside is
        /// silent. Each speaking arm is `mov cx,0xFFDB / mov edx,&lt;str&gt; /
        /// call [vmt+0xD4]`, i.e. colour 0xDB, type 0xFF - the same pair the
        /// state-75 arm already uses.
        ///
        /// Only the arms the legacy per-second loop used to own are wired up
        /// here; the rest of the 46-arm table is catalogued in
        /// docs/m_state2_20260813.md and is still MISSING.
        /// </summary>
        private void OnNativeTimedStateLost(byte internalType)
        {
            switch (internalType)
            {
                case 23:
                    // Legacy slot 8. Native has no separate hide flag - 0x76B438
                    // derives it as `[self+0x2E4] != 0 || HasState(0x17)` - but
                    // C# still carries m_boHideMode, so it has to be cleared
                    // where the state dies.
                    m_boHideMode = false;
                    break;
                // Legacy slots 9 and 10 (STATE_DEFENCEUP / STATE_MAGDEFENCEUP)
                // used to send their "回复正常" text from here. Those are arms
                // 0x74296D and 0x742955 of the lost table, which native reaches
                // through VMT+0x14, not through this virtual — 0x77337C's whole
                // body is `call [edi+0x14]`, the 0x773254 recalc probe and the
                // state-20 companion, with no message call of its own. They now
                // live in DispatchNativeStateLostArm with the literal 0xDB/0xFF
                // pair the arms encode, instead of SysMsg's configurable colours
                // and boShowPreFixMsg prefix. Leaving them here as well would
                // have sent each text twice once the table landed.
                case 20:
                    // Legacy slot 11 (STATE_BUBBLEDEFENCEUP). Silent in the
                    // native table (index 20 - 14 = 6 maps to the default arm).
                    m_boAbilMagBubbleDefence = false;
                    break;
            }
        }

        public bool HasTimedAbility(int scriptType)
        {
            return IsSupportedTimedAbilityType(scriptType) &&
                   FindTimedAbilityInternal((byte)(scriptType + 32)) != null;
        }

        public int GetTimedAbilityValue(int scriptType)
        {
            if (!IsSupportedTimedAbilityType(scriptType))
            {
                return 0;
            }
            return GetNativeTimedAbilityValue((byte)(scriptType + 32));
        }

        public int GetTimedAbilityRemainingMilliseconds(int scriptType)
        {
            if (!IsSupportedTimedAbilityType(scriptType))
            {
                return 0;
            }
            return FindTimedAbilityInternal((byte)(scriptType + 32))?.RemainingMilliseconds ?? 0;
        }

        public int GetTimedHolyDefense(int baseHolyDefense)
        {
            var result = baseHolyDefense;
            for (var node = m_TimedAbilityHead; node != null; node = node.Next)
            {
                switch (node.InternalType)
                {
                    case 96:
                        result = unchecked(result + node.Value);
                        break;
                    case 100:
                        // STATE-40 — band handler for 0x64 @0x773A45, bytes verified:
                        //   773A45  8B 87 14 03 00 00  mov  eax, [edi+0x314]
                        //   773A4B  89 45 C4           mov  [ebp-0x3C], eax
                        //   773A4E  33 C0 / 89 45 C8   mov  [ebp-0x38], 0     ; zero-extend
                        //   773A53  DF 6D C4           fild qword [ebp-0x3C]  ; hence (uint)
                        //   773A56  DB 43 0A           fild dword [ebx+0xA]   ; node value
                        //   773A59  D8 35 94 3B 77 00  fdiv dword [0x773B94]  ; = 100.0f
                        //   773A5F  DE C9              fmulp st(1)
                        //   773A61  E8 1A FB C8 FF     call 0x403580          ; @TRUNC
                        //   773A66  01 87 14 03 00 00  add  [edi+0x314], eax
                        // 0x403580 sets RC=11 (`66 81 4C 24 02 00 0F  or word [esp+2],0xF00`)
                        // before fistp, so it truncates toward zero. The sibling helper
                        // 0x403574 is a bare fistp on the default control word and therefore
                        // rounds half to even; this recompute never calls it. `add [edi+0x314],
                        // eax` consumes only the low dword of @TRUNC's qword result.
                        //
                        // Operand order and precision: native evaluates
                        // current * (value / 100) on the x87 stack, C# evaluates
                        // (current * value) / 100 in double. Delphi's control word lives at
                        // 0x7A2024 = 0x1372 (fldcw sites 0x4034E8 / 0x4045C3), so PC=11 and
                        // RC=00 - every native intermediate carries a 64-bit significand
                        // rounded half-to-even, against double's 53.
                        //
                        // Measured, not assumed (staging/_x87_msched*.py emulates both
                        // roundings with exact rationals):
                        //  - The two forms agree wherever value/100 is exact in both widths,
                        //    which covers every fixture this path is audited on.
                        //  - They disagree by one unit on roughly 0.2% of (current, value)
                        //    pairs even at small magnitudes. Smallest case: current=15,
                        //    value=420. Native rounds 4.2 down to a 64-bit significand,
                        //    15 * that = 62.999999999999999997, @TRUNC -> 62, result 77.
                        //    C# gets 15*420 = 6300 then /100 = 63.0 exactly, result 78.
                        //  - Merely swapping to current * (value / 100.0) in double does NOT
                        //    close the gap; it mismatches on about the same fraction, because
                        //    the residue comes from significand width, not from the order.
                        // Reproducing native exactly needs 64-bit-significand emulation
                        // (UInt128), which is a separate, separately audited change. The
                        // ordering is left as-is because it is measurably no worse.
                        var percent = unchecked((int)(long)(
                            unchecked((uint)result) * (double)node.Value / 100.0));
                        result = unchecked(result + percent);
                        break;
                }
            }
            return result;
        }

        internal static bool IsNativeTimedAbilityType(int scriptType)
        {
            return scriptType >= 0 && scriptType <= 28 ||
                   scriptType >= 43 && scriptType <= 46 ||
                   scriptType >= 58 && scriptType <= 62 ||
                   scriptType >= 64 && scriptType <= 69 ||
                   scriptType == 74;
        }

        internal static bool IsSupportedTimedAbilityType(int scriptType)
        {
            return scriptType switch
            {
                0 or 1 or 2 or 4 or 5 or 6 or 7 or 8 or 9 or 12 or 13 or 17 or 27 or 43 or 44 or 45 or 59 or 60 or 61 or 62 or 64 or 68 => true,
                _ => false
            };
        }

        // STATE-21: Apply gate (VMT+0x1E8 @ EA 0x772F84 base implementation).
        // Called inside the native add function at 0x7730E9. If this returns false,
        // the entire application aborts SILENTLY with no messages, no bitset changes,
        // no list mutations - the caller sees no evidence the attempt was made.
        // Virtual to allow subclass-specific gates (e.g., TFoxBossMon has override at 0x5FA508).
        //
        // Three veto paths in strict sequence (§7.3 of native sub_772F84):
        // 1. @ 0x772F92: State 52 (csZaiBieRenMaShang) active → refuse ALL
        // 2. @ 0x772FA1: State 16 value >= 5 → refuse states 45, 53 only
        // 3. @ 0x772FB8: ImmuneCheck (sub_773C44) → refuse if immune
        internal virtual bool CanAddNativeTimedAbility(byte internalType)
        {
            return CanAddNativeTimedAbilityCreature(internalType);
        }

        // The 0x772F84 body itself, reachable non-virtually so the THumanKind
        // override @0x7465D4 can express its `call 0x772F84` at 0x7465E6 without
        // re-entering the virtual slot. See CanAddNativeTimedAbilityHumanKind in
        // TBaseObject.NativeMakePosion.cs.
        protected bool CanAddNativeTimedAbilityCreature(byte internalType)
        {
        // STATE-11: Apply gate (VMT+0x1E8 @ EA 0x772F84 base implementation).
        // Called inside the native add function at 0x7730E9. If this returns false,
        // the entire application aborts SILENTLY with no messages, no bitset changes,
        // no list mutations - the caller sees no evidence the attempt was made.
        // Virtual to allow subclass-specific gates (e.g., TFoxBossMon has override at 0x5FA508).
        //
        // Base gate checks (91 classes use this implementation):
        // - Range: state_id > 0x6F (111) -> refuse
        // - State 52 (riding someone else's horse) active -> refuse ALL
        // - State 16 (immunity) active with value >= 5 -> refuse states 45, 53
        // - State 16 active -> refuse states {0, 13, 24, 26, 28, 29, 30, 31} via IsBlockedByNativeState16
        // - State 18 active OR state 26 deadline not expired -> refuse state 26
        //
        // STATE-20/21/23/24: Additional gate checks documented in method body below.
            if (internalType > NativeActiveStateMax)
            {
                return false;
            }

            // Native @ 0x772F92: Veto path 1 - state 52 blocks ALL states
            if (HasNativeActiveState(TimedAbilityGlobalBlockState))
            {
                return false;
            }

            // Native @ 0x772FA1: Veto path 2 - state 16 with value >= 5 blocks states 45 and 53
            if (HasNativeActiveState(TimedAbilityValueGateState) &&
                GetNativeTimedAbilityValue(TimedAbilityValueGateState) >= 5)
            {
                if (internalType == 45 || internalType == 53)
                {
                    return false;
                }
            }

            // Native @ 0x772FB8: Veto path 3 - ImmuneCheck (sub_773C44)
            if (IsImmuneToTimedAbility(internalType))
            {
                return false;
            }

            return true;
        }

        // Native @ 0x773C44: ImmuneCheck - two independent immunity conditions
        private bool IsImmuneToTimedAbility(byte internalType)
        {
            // Native @ 0x773C51: Part 1 - state 16 present AND state is blockable
            if (HasNativeActiveState(TimedAbilityValueGateState) &&
                IsBlockedByNativeState16(internalType))
            {
                return true;
            }

            // Native @ 0x773C70: Part 2 - petrify immunity window for state 26 only.
            // 0x773C7B (75 0D jne 0x773C8A) skips the deadline compare when state 18
            // is present, so the native predicate is (state 18 OR now < deadline).
            if (internalType == NativeState26Type &&
                (HasNativeActiveState(18) ||
                 IsNativeState26DeadlineActive(HUtil32.GetTickCount())))
            {
                return true;
            }

            return false;
        }

        internal int GetNativeTimedAbilityValue(byte internalType)
        {
            return FindTimedAbilityInternal(internalType)?.Value ?? 0;
        }

        internal bool TryGetNativeTimedAbilityValue(byte internalType,
            out int value)
        {
            var node = FindTimedAbilityInternal(internalType);
            value = node?.Value ?? 0;
            return node != null;
        }

        internal int GetNativeTimedAbilityRemainingMilliseconds(
            byte internalType)
        {
            return FindTimedAbilityInternal(internalType)?
                .RemainingMilliseconds ?? 0;
        }

        internal bool ReduceNativeTimedAbilityRemaining(byte internalType,
            int milliseconds)
        {
            var node = FindTimedAbilityInternal(internalType);
            if (node == null)
            {
                return false;
            }

            node.RemainingMilliseconds = unchecked(
                node.RemainingMilliseconds - milliseconds);
            return true;
        }

        private TimedAbilityNode FindTimedAbilityInternal(byte internalType)
        {
            if (!HasNativeActiveState(internalType))
            {
                return null;
            }

            return FindTimedAbilityNode(internalType);
        }

        private TimedAbilityNode FindTimedAbilityNode(byte internalType)
        {
            for (var node = m_TimedAbilityHead; node != null; node = node.Next)
            {
                if (node.InternalType == internalType)
                {
                    return node;
                }
            }
            return null;
        }

        protected void ClearTimedAbilitiesOnExit()
        {
            for (var node = m_TimedAbilityHead; node != null; node = node.Next)
            {
                ClearNativeActiveState(node.InternalType);
            }

            m_TimedAbilityHead = null;
            m_TimedAbilityProcessTick = 0;
            m_boAbilityRecalcPending = false;
            ClearNativeSkill152StateOnExit();
        }

        // Native leaf @0x773254 decides which state changes set the recalc-pending
        // flag. Verbatim:
        //   0x773254  80 C2 F8              add dl, 0xF8      ; dl = internalType - 8
        //   0x773257  80 FA 67              cmp dl, 0x67      ; unsigned, 103
        //   0x77325A  77 0A                 ja  0x773266      ; out of range -> CF=0
        //   0x77325C  83 E2 7F              and edx, 0x7F
        //   0x77325F  0F A3 15 6C 32 77 00  bt  [0x77326C], edx
        //   0x773266  0F 92 C0              setb al
        // So the BIT INDEX IS BIASED BY -8 and the domain is internalType [8,111].
        // Callers 0x773366 / 0x773399 feed it the node's InternalType and set
        // Self+0x438 (m_boAbilityRecalcPending) on a true result.
        //
        // Decoded with the -8 bias, the 37 set bits are internalType:
        //   14, 21, 22, 32..44, 46, 47, 48, 75, 76, 77, 78, 85,
        //   90..94, 96..101, 103, 104
        //
        // Two bugs used to live here and had to be fixed together:
        //  1. the index was applied unbiased, which misjudged 41 of the 112 types
        //     (20 lost their recalc, 21 gained one native never performs);
        //  2. byte 5 had been hand-patched from 0x01 to 0x11 to force internalType
        //     44 true under the unbiased index. 44 is genuinely in the native set
        //     and falls out correctly once the bias is right, so the extra bit is
        //     removed - left in place it would additionally recalc type 52, which
        //     native does not.
        private static readonly byte[] NativeRecalcBitmap = new byte[14]
        {
            0x40, 0x60, 0x00, 0xFF, 0xDF, 0x01, 0x00,
            0x00, 0x78, 0x20, 0x7C, 0xBF, 0x01, 0x00
        };

        private const int NativeRecalcBitmapBias = 8;

        private static bool RequiresTimedAbilityRecalc(byte internalType)
        {
            int biased = internalType - NativeRecalcBitmapBias;
            if (biased < 0 || biased > 0x67)
                return false;

            return (NativeRecalcBitmap[biased / 8] & (1 << (biased % 8))) != 0;
        }

        protected void MarkAbilityRecalcPending()
        {
            m_boAbilityRecalcPending = true;
        }

        protected virtual void QueueTimedAbilitySnapshotAfterRecalc()
        {
        }

        protected void ConsumeAbilityRecalcPending()
        {
            if (!m_boAbilityRecalcPending)
            {
                return;
            }

            RecalcAbilitys();
            QueueTimedAbilitySnapshotAfterRecalc();
            m_boAbilityRecalcPending = false;
        }

        /// <summary>
        /// Native TPlayObject VMT+0x14 = 0x6D7628, which is: call the inherited
        /// notifier 0x741884 (status broadcast + the gained/lost arm), then the
        /// TPlayObject-only state-25 pair, then build the 3555 record. The three
        /// steps below are those three, in that order.
        /// <para>
        /// The 0x741884 arm tables live in TBaseObject.NativeStateArms.cs. State
        /// 75 predates them and is still spelled out here; it belongs to a later
        /// batch and is deliberately left untouched. No state reaches both, since
        /// 75 is absent from both switches.
        /// </para>
        /// <para>
        /// The TPlayObject-only state-25 arms of 0x6D7628 are emitted after the
        /// inherited tables and before the 3555 record below. They are a separate
        /// override, not part of the 99-arm tables.
        /// </para>
        /// </summary>
        private void SendTimedAbilityState(TimedAbilityNode node, bool removed)
        {
            SendRefMsg(Grobal2.RM_CHARSTATUSCHANGED, 0,
                unchecked((ushort)m_nHitSpeed), 0, 0, string.Empty,
                GetBodyStateBuffer());

            if (!removed)
            {
                // Native VMT+0x14 (sub_741884) runs the GAINED dispatch table
                // immediately after the inherited broadcast at 0x7418B9, and the
                // only site that pushes the gained flag is 0x77318C — the tail of
                // the add routine, one instruction before the GetTickCount that
                // becomes node.LastTick. That is exactly this call, so the table
                // belongs here. ecx there is
                // `node.RemainingMilliseconds / 1000` (signed idiv @0x773191).
                OnNativeTimedStateGained(node.InternalType,
                    node.RemainingMilliseconds / 1000);
            }

            // State 75 is dispatch band B's gained arm 0x741EFD and lost arm
            // 0x742A0B, both already reproduced here. Verified against the
            // literals: gained is 0x742F00 (declen 16, BB F0 C7 BD BF B9 D0 D4
            // CB B2 BC E4 CC E1 B8 DF = "火墙抗性瞬间提高") + IntToStr(di) +
            // 0x742C94 ("秒"), lost is 0x743470 (declen 16, BB F0 C7 BD BF B9
            // D0 D4 BB D8 B8 B4 D5 FD B3 A3 = "火墙抗性回复正常"), and both
            // carry cx=0xFFDB. It is therefore deliberately absent from
            // OnNativeTimedStateGained / OnNativeTimedStateLost — adding it
            // there would send the text twice.
            // The hero branch below has no counterpart in sub_741884 (the hero
            // VMTs share TPlayer's +0xD4 = sub_73C8F4, which posts to self with
            // no "(英雄) " prefix); it predates this reversing pass and is left
            // untouched.
            if (node.InternalType == 75)
            {
                var text = removed
                    ? "火墙抗性回复正常"
                    : $"火墙抗性瞬间提高{unchecked((ushort)(node.RemainingMilliseconds / 1000))}秒";
                // 0x741EFD gained / 0x742A0B lost — 同属 31×属性提升提示 jmp 族。
                if (Plugins.YanshenPangu1Patches.ShouldSuppressAttrUpHint(text))
                {
                    SendTimedAbilityClientState(node.InternalType,
                        node.RemainingMilliseconds, node.Value, removed);
                    return;
                }
                if (this is HeroObject hero)
                {
                    if (hero.m_Master is TPlayObject master)
                    {
                        master.SendMsg(hero, Grobal2.RM_SYSMESSAGE, 0,
                            0xDB, 0xFF, 0, "(英雄) " + text);
                    }
                }
                else if (this is TPlayObject)
                {
                    SendMsg(this, Grobal2.RM_SYSMESSAGE, 0,
                        0xDB, 0xFF, 0, text);
                }
            }
            else if (removed)
            {
                DispatchNativeStateLostArm(node.InternalType);
                DispatchNativeStateLostTextBatchC(node.InternalType);
            }
            else
            {
                // 0x77318C `mov ecx,0x3E8 / cdq / idiv ecx` then `movzx eax,di`
                // in the arm: signed divide toward zero, low 16 bits printed.
                DispatchNativeStateGainedArm(node.InternalType,
                    unchecked((ushort)(node.RemainingMilliseconds / 1000)));
                DispatchNativeStateGainedTextBatchC(node.InternalType,
                    node.RemainingMilliseconds);
            }

            if (this is TPlayObject && node.InternalType == 25)
            {
                if (removed)
                {
                    // 0x6D76A6: cx=0xFFDB, text @0x6D7774.
                    SendNativeStateArmMsg("反外挂惩罚时间结束",
                        NativeStateArmBuffColor, NativeStateArmBuffType);
                }
                else
                {
                    // 0x6D7668..0x6D7698: movzx eax,di after signed ms/1000.
                    var seconds = unchecked((ushort)(node.RemainingMilliseconds / 1000));
                    SendNativeStateArmMsg("反外挂惩罚" + seconds + "秒",
                        NativeStateArmAlertColor, NativeStateArmAlertType);
                }
            }

            SendTimedAbilityClientState(node.InternalType,
                node.RemainingMilliseconds, node.Value, removed);
        }

        protected virtual void SendTimedAbilityClientState(byte internalType,
            int remainingMilliseconds, int value, bool removed)
        {
        }

        internal static (ClientPacket Header, byte[] Body) BuildTimedAbilityClientState(
            byte internalType, int remainingMilliseconds, int value, bool removed)
        {
            var header = Grobal2.MakeDefaultMsg(TimedAbilityMessage,
                removed ? 0 : remainingMilliseconds, internalType, 0, 0);
            if (removed)
            {
                return (header, Array.Empty<byte>());
            }

            using var stream = new MemoryStream(10);
            using var writer = new BinaryWriter(stream);
            writer.Write(internalType);
            writer.Write((byte)0);
            writer.Write(remainingMilliseconds);
            writer.Write(value);
            return (header, stream.ToArray());
        }

        // 战神 sub_6E99B8 @0x006E99B8 — the "send my whole timed-ability list" packet.
        // It is one of the login-burst list packets dispatched by the login virtual
        // sub_6E9A98 (dword refs at VMT slots 0x62F190 / 0x6ACACC), which runs exactly
        // once per player login: srv_AppearTimes 3554 = 50,911 = the SM_LOGON count.
        // Walks the timed-ability list head at [self+0xDC] (m_TimedAbilityHead — the
        // same list 3555's node-getter sub_773B98 reads at 0x773BBA `mov eax,[esi+0xDC]`),
        // emits one 10-byte record per node, then sends via [obj+0x254]:
        //   0x6E99DE  mov eax,[eax+0xDC]                 ; list head
        //   0x6E9A14  mov dl,[node+1] / 0x6E9A17 mov [buf+i*10],dl     ; +0 = InternalType
        //   0x6E9A1D  mov byte [buf+i*10+1],0                          ; +1 = 0
        //   0x6E9A28  mov edx,[node+2] / 0x6E9A2B mov [buf+i*10+2],edx ; +2 = RemainingMs
        //   0x6E9A35  mov edx,[node+0xA]/0x6E9A38 mov [buf+i*10+6],edx ; +6 = Value
        //   0x6E9A40  mov eax,[node+0xE]                               ; next
        //   0x6E9A4C  push ebx                            ; Param  = record count
        //   0x6E9A4D  push 0 / push 0                     ; Tag = Series = 0
        //   0x6E9A54  push [ebp-0xC]                      ; Buf
        //   0x6E9A55  mov eax,ebx / add eax,eax / lea eax,[eax+eax*4] ; Len = count*10
        //   0x6E9A5D  xor ecx,ecx                         ; Recog = 0
        //   0x6E9A5F  mov dx,0xDE2                        ; ident 3554
        //   0x6E9A68  call [ebx+0x254]
        // Each 10-byte record is byte-identical to the non-removed body produced by
        // BuildTimedAbilityClientState above. An empty list still sends (je 0x6E9A4C
        // skips the loop but not the count=0 / Len=0 send), matching how 4612 fires.
        internal (ClientPacket Header, byte[] Body) BuildTimedAbilityListState()
        {
            var count = 0;
            for (var node = m_TimedAbilityHead; node != null; node = node.Next)
            {
                count++;
            }

            var header = Grobal2.MakeDefaultMsg(TimedAbilityListMessage, 0, count, 0, 0);
            if (count == 0)
            {
                return (header, Array.Empty<byte>());
            }

            using var stream = new MemoryStream(count * 10);
            using var writer = new BinaryWriter(stream);
            for (var node = m_TimedAbilityHead; node != null; node = node.Next)
            {
                writer.Write(node.InternalType);
                writer.Write((byte)0);
                writer.Write(node.RemainingMilliseconds);
                writer.Write(node.Value);
            }
            return (header, stream.ToArray());
        }

        private void ApplyTimedAbilityBonuses()
        {
            for (var node = m_TimedAbilityHead; node != null; node = node.Next)
            {
                var value = node.Value;
                switch (node.InternalType - 32)
                {
                    case 0:
                        m_WAbil.DC = AddTimedRange(m_WAbil.DC, value);
                        break;
                    case 1:
                        m_WAbil.MC = AddTimedRange(m_WAbil.MC, value);
                        break;
                    case 2:
                        m_WAbil.SC = AddTimedRange(m_WAbil.SC, value);
                        break;
                    case 3:
                        // STATE-47 — band handler for 0x23 @0x77357F, bytes verified:
                        //   77357F  66 8B 43 0A            mov  ax, word [ebx+0xA]
                        //   773583  66 01 46 10            add  word [esi+0x10], ax
                        //   773587  E9 91 05 00 00         jmp  0x773B1D
                        // esi = Self+0x264 (callers `lea edx,[ebx+0x264]` @0x60A8B9 /
                        // 0x73DD6B / 0x73E43E), so esi+0x10 = Self+0x274.
                        // 0x7729C4 broadcasts that word on SM_CHARSTATUSCHANGED 657
                        // (`66 8B 90 74 02 00 00  mov dx, word [eax+0x274]`), which
                        // C# SendTimedAbilityState already sends as m_nHitSpeed.
                        m_nHitSpeed = unchecked((ushort)(m_nHitSpeed +
                            (ushort)value));
                        break;
                    case 10:
                        // STATE-37 — band handler for 0x2A @0x773636, bytes verified:
                        //   773636  83 7B 0A 01            cmp  dword [ebx+0xA], 1
                        //   77363A  0F 85 DD 04 00 00      jne  default (0x773B1D)
                        //   773640  8D 87 64 02 00 00      lea  eax, [edi+0x264]
                        // ×1.2 six dwords esi+0x28/2C/30/34/38/3C (DC/MC/SC lo+hi):
                        //   fild dword / fld tbyte [0x773B5C] / fmulp / call 0x403580 @TRUNC
                        //   0x773B5C = 9A 99 99 99 99 99 99 99 FF 3F  (80-bit extended 1.2)
                        // ×1.5 six dwords esi+0x18/1C/20/24/4C/54 (AC/MAC lo+hi, MaxHP, MaxMP):
                        //   fild dword / fmul dword [0x773B68] / call 0x403580 @TRUNC
                        //   0x773B68 = 00 00 C0 3F  (float32 1.5)
                        // @TRUNC @0x403580: `66 81 4C 24 02 00 0F` or word [esp+2],0x0F00
                        // then fistp — toward-zero, not @ROUND 0x403574.
                        // 0..50000 integer scan: trunc(n*1.2) agrees between the 80-bit
                        // constant and IEEE double, so double 1.2 is used here.
                        if (value != 1)
                        {
                            break;
                        }
                        m_WAbil.DC = ScaleTimedRange(m_WAbil.DC, 1.2);
                        m_WAbil.MC = ScaleTimedRange(m_WAbil.MC, 1.2);
                        m_WAbil.SC = ScaleTimedRange(m_WAbil.SC, 1.2);
                        m_WAbil.AC = ScaleTimedRange(m_WAbil.AC, 1.5);
                        m_WAbil.MAC = ScaleTimedRange(m_WAbil.MAC, 1.5);
                        m_WAbil.MaxHP = TruncMulNative(m_WAbil.MaxHP, 1.5);
                        m_WAbil.MaxMP = TruncMulNative(m_WAbil.MaxMP, 1.5);
                        break;
                    case 11:
                        // STATE-32 H08 — state 0x2B (internalType 43): SC += value
                        // on both lo/hi words (@0x77356E, esi+0x38/0x3C = SC).
                        // Body/evidence in TBaseObject.StateRecompute.cs.
                        ApplyRecomputeState2B_ScBoost(value);
                        break;
                    case 4:
                        m_WAbil.MaxHP = unchecked(m_WAbil.MaxHP + value);
                        break;
                    case 5:
                        m_WAbil.MaxMP = unchecked(m_WAbil.MaxMP + value);
                        break;
                    case 6:
                        m_wSpeedPoint = unchecked((ushort)(m_wSpeedPoint +
                            (ushort)value));
                        break;
                    case 7:
                        m_nAntiMagic = unchecked((ushort)(m_nAntiMagic +
                            (ushort)value));
                        break;
                    case 8:
                        m_WAbil.AC = AddTimedRange(m_WAbil.AC, value);
                        break;
                    case 9:
                        m_WAbil.MAC = AddTimedRange(m_WAbil.MAC, value);
                        break;
                    case 12:
                        m_WAbil.MaxWeight = AddTimedWord(m_WAbil.MaxWeight,
                            m_WAbil.MaxWeight, ushort.MaxValue);
                        m_WAbil.MaxWearWeight = AddTimedWord(m_WAbil.MaxWearWeight,
                            m_WAbil.MaxWearWeight, ushort.MaxValue);
                        m_WAbil.MaxHandWeight = AddTimedWord(m_WAbil.MaxHandWeight,
                            m_WAbil.MaxHandWeight, ushort.MaxValue);
                        break;
                    case 22:
                        // STATE-38: State 0x36 handler (internalType 54, case 22).
                        // Native EA 0x7735E0: Subtracts value from AC/MAC low and high,
                        // floored at 0. Native uses MAX helper (0x4C7004) for each field.
                        // Bytes: 8B 56 18 2B 53 0A 33 C0 E8 17 3A D5 FF 89 46 18 ...
                        // Fields: esi+0x18/0x1C/0x20/0x24 = Self+0x27C/0x280/0x284/0x288
                        // (ACLow, ACHigh, MACLow, MACHigh in working ability record).
                        m_WAbil.AC = SubtractTimedRange(m_WAbil.AC, value);
                        m_WAbil.MAC = SubtractTimedRange(m_WAbil.MAC, value);
                        break;
                    case 43:
                        AddNativeHqFastness(value);
                        break;
                    case 44:
                        AddNativeUnionFastness(value);
                        break;
                    case 45:
                        AddNativeNearHitFastness(value);
                        break;
                    case 59:
                        ApplyTimedJobAttack(value);
                        break;
                    case 60:
                        m_wNativeDrugJobBonus = unchecked((ushort)(
                            m_wNativeDrugJobBonus + (ushort)value));
                        break;
                    case 61:
                        m_wEffectStrength = unchecked((ushort)(m_wEffectStrength +
                            (ushort)value));
                        break;
                    case 62:
                        m_wEffectResistance = unchecked((ushort)(m_wEffectResistance +
                            (ushort)value));
                        break;
                    case 65:
                        // STATE-32 H1A — state 0x61 (internalType 97): job-scaled
                        // MaxHP/MaxMP (@0x773A71). Body/evidence in
                        // TBaseObject.StateRecompute.cs.
                        ApplyRecomputeState61_JobMaxHpMp(value);
                        break;
                    case 71:
                        // STATE-32 H1B — state 0x67 (internalType 103): DC/MC/SC
                        // HIGH word += value (@0x773ADC). Body/evidence in
                        // TBaseObject.StateRecompute.cs.
                        ApplyRecomputeState67_UpperCombat(value);
                        break;
                    case 72:
                        // STATE-32 H1C — state 0x68 (internalType 104): AC/MAC
                        // lo+hi += value (@0x773AF9). Body/evidence in
                        // TBaseObject.StateRecompute.cs.
                        ApplyRecomputeState68_AcMac(value);
                        break;
                }
            }
        }

        private void ApplyTimedJobAttack(int value)
        {
            switch (m_btJob)
            {
                case 0:
                    m_WAbil.DC = AddTimedUpper(m_WAbil.DC, value);
                    break;
                case 1:
                    m_WAbil.MC = AddTimedUpper(m_WAbil.MC, value);
                    break;
                case 2:
                    m_WAbil.SC = AddTimedUpper(m_WAbil.SC, value);
                    break;
            }
        }

        private static int AddTimedRange(int ability, int value)
        {
            return HUtil32.MakeLong(
                unchecked((ushort)(HUtil32.LoWord(ability) + value)),
                unchecked((ushort)(HUtil32.HiWord(ability) + value)));
        }

        /// <summary>
        /// STATE-37: toward-zero multiply matching @TRUNC (0x403580).
        /// <c>(int)(value * factor)</c> truncates toward zero for both signs.
        /// </summary>
        private static int TruncMulNative(int value, double factor)
        {
            return unchecked((int)(value * factor));
        }

        /// <summary>
        /// STATE-37: native stores AC/DC/MC/SC as two dwords at Self+0x264+off;
        /// C# packs each pair as LoWord/HiWord of one int, same as AddTimedRange.
        /// </summary>
        private static int ScaleTimedRange(int ability, double factor)
        {
            return HUtil32.MakeLong(
                unchecked((ushort)TruncMulNative(HUtil32.LoWord(ability), factor)),
                unchecked((ushort)TruncMulNative(HUtil32.HiWord(ability), factor)));
        }

        /// <summary>
        /// STATE-38: Subtracts value from both low and high components of a range ability,
        /// with zero floor. Native EA 0x7735E0 uses MAX helper (0x4C7004) for each field:
        /// mov edx,[esi+N]; sub edx,[ebx+0xA]; xor eax,eax; call 0x4C7004; mov [esi+N],eax.
        /// The MAX helper returns max(edx, eax=0), equivalent to Math.Max(x - v, 0).
        /// Used for state 0x36 AC/MAC subtraction.
        /// </summary>
        private static int SubtractTimedRange(int ability, int value)
        {
            int low = HUtil32.LoWord(ability);
            int high = HUtil32.HiWord(ability);
            return HUtil32.MakeLong(
                unchecked((ushort)Math.Max(low - value, 0)),
                unchecked((ushort)Math.Max(high - value, 0)));
        }

        private static int AddTimedUpper(int ability, int value)
        {
            return HUtil32.MakeLong(HUtil32.LoWord(ability),
                unchecked((ushort)(HUtil32.HiWord(ability) + value)));
        }

        private static ushort AddTimedWord(ushort current, int value, int maximum)
        {
            return unchecked((ushort)(current + value));
        }

    }
}
