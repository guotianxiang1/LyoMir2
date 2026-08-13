using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        // ── Why this type exists ────────────────────────────────────────────
        //
        // Native keeps exactly one store for timed states. AddState @0x7730D0
        // allocates an 18-byte record (0x764E00: `mov eax,0x12 / call 0x402FA0`,
        // freed by 0x764E10: `mov edx,0x12 / call 0x402FD0`) and pushes it onto
        // the singly-linked list whose head is Self+0xDC:
        //
        //   +0x00 byte   flag          0x77310F  mov [eax], dl        ; [ebp+8]
        //   +0x01 byte   internalType  0x773164  mov [eax+1], bl
        //   +0x02 dword  remaining ms  0x77315E  mov [eax+2], edx     ; [ebp-4]
        //   +0x06 dword  lastTick      0x7731B3  mov [edx+6], eax     ; GetTickCount
        //   +0x0A dword  value         0x77316A  mov [eax+0xA], edi   ; [ebp+0xC]
        //   +0x0E ptr    next          0x773176  mov [eax+0xE], edx   ; old head
        //                              0x77317C  mov [esi+0xDC], eax  ; prepend
        //
        // Plus a 112-bit presence bitset at Self+0x168 (bts @0x77299B, btr
        // @0x7729B9, bt @0x772968, all guarded by `cmp ..,0x6F / ja` and masked
        // `and ..,0x7F`). Durations are MILLISECONDS: MakePoison @0x76B413 does
        // `imul ecx, eax, 0x3E8` on a 16-bit second count before calling
        // AddState, and the expiry helper @0x7730AC subtracts a raw tick delta.
        //
        // There is no second array. C# had grown one - `ushort[12]` counted in
        // SECONDS, expired by its own once-per-second loop - and the two stores
        // drifted apart exactly as REPLICATION_RULES 4.18 describes. This type
        // deletes the second store while keeping the ~150 call sites that spell
        // it `m_wStatusTimeArr[slot]` compiling and, more importantly, reading
        // and writing the one real store.
        //
        // ── The slot mapping ────────────────────────────────────────────────
        //
        // Legacy slot i is native state 31 - i. Three independent proofs:
        //
        //  1. Wire bits. GetCharStatus projects slot i onto bit 31 - i
        //     (`0x80000000 >> i`), and native ships the raw bitset where state s
        //     is bit s (0x7729D4 `lea edx,[eax+0x168]`, RefMsg 0x291 = 657).
        //  2. MakePoison. Native @0x76B3C8 forwards the caller's state id
        //     straight to AddState; C# MakePosion(nType) already maps it as
        //     31 - nType.
        //  3. The expiry message table. Native's state-lost dispatch @0x742692
        //     (`add eax,-0xE / cmp eax,0x5C / ja / jmp [eax*4+0x7426A9]`) emits
        //     "防御力回复正常" for state 22, and STATE_DEFENCEUP is slot 9;
        //     31 - 9 = 22. Likewise state 21 for STATE_MAGDEFENCEUP (slot 10).
        //
        // ── Unit conversion ─────────────────────────────────────────────────
        //
        // Reads round UP so that "node alive" and "slot > 0" cannot disagree: a
        // node with 500 ms left must not read as 0 and silently unblock the ~60
        // action gates written as `m_wStatusTimeArr[POISON_STONE] == 0`.
        //
        // Permanent nodes (remaining == -1, native's sentinel at 0x7730B1
        // `cmp dword [eax+2],-1 / je`) read as PermanentSeconds. That value is
        // 60000 because the legacy loop already skipped `>= 60000`, so the two
        // existing writers that meant "never expire" spelled it `6 * 10 * 1000`.
        private const ushort PermanentSeconds = 60000;

        /// <summary>Legacy slot count. Slots 0..11 are native states 31..20.</summary>
        internal const int LegacyStatusSlotCount = 12;

        /// <summary>Legacy slot <paramref name="slot"/> is native state 31 - slot.</summary>
        internal static byte LegacyStatusSlotToNativeState(int slot)
        {
            return unchecked((byte)(31 - slot));
        }

        /// <summary>
        /// Array-shaped facade over the native state list. Holds no storage of
        /// its own; every read and write lands on the <c>Self+0xDC</c> node.
        /// <para>
        /// A class rather than a struct, because the call sites write through it
        /// as <c>m_wStatusTimeArr[slot] = value</c>. Reaching a struct indexer's
        /// setter through a property mutates the temporary the getter returned,
        /// which C# rejects outright (CS1612) - and had it been reached through a
        /// field instead, it would have compiled and silently dropped every write.
        /// </para>
        /// </summary>
        public sealed class LegacyStatusTimeView
        {
            private readonly TBaseObject _owner;

            internal LegacyStatusTimeView(TBaseObject owner)
            {
                _owner = owner;
            }

            public int Length => LegacyStatusSlotCount;

            public int GetLowerBound(int dimension) => 0;

            public int GetUpperBound(int dimension) => LegacyStatusSlotCount - 1;

            public ushort this[int slot]
            {
                get
                {
                    if (_owner == null || slot < 0 || slot >= LegacyStatusSlotCount)
                    {
                        return 0;
                    }

                    // FindTimedAbilityInternal, not the raw list walk: native's
                    // FindState @0x773BB1 tests the bitset first
                    // (`call 0x772960 / test al,al / je -> nil`), so a record
                    // whose bit has been cleared is invisible.
                    var node = _owner.FindTimedAbilityInternal(
                        LegacyStatusSlotToNativeState(slot));
                    if (node == null)
                    {
                        return 0;
                    }
                    if (node.RemainingMilliseconds == -1)
                    {
                        return PermanentSeconds;
                    }
                    if (node.RemainingMilliseconds <= 0)
                    {
                        return 0;
                    }

                    // Round up: the node is still alive, so the slot must not
                    // read 0 while the sub-second remainder burns down.
                    var seconds = (node.RemainingMilliseconds + 999) / 1000;
                    return seconds >= PermanentSeconds
                        ? (ushort)(PermanentSeconds - 1)
                        : (ushort)seconds;
                }
                set
                {
                    if (_owner == null || slot < 0 || slot >= LegacyStatusSlotCount)
                    {
                        return;
                    }
                    _owner.SetLegacyStatusSlot(slot, value);
                }
            }

            /// <summary>Snapshot in legacy seconds, for the save record.</summary>
            public ushort[] ToArray()
            {
                var copy = new ushort[LegacyStatusSlotCount];
                for (var i = 0; i < LegacyStatusSlotCount; i++)
                {
                    copy[i] = this[i];
                }
                return copy;
            }

            /// <summary>
            /// Replays a saved snapshot back onto the node list, silently.
            ///
            /// The two callers - the login restore in <c>UsrEngn</c> and the elf
            /// hand-off in <c>Monster</c> - used to assign a plain array, which
            /// broadcast nothing and ran no gate. Going through the indexer here
            /// would fire <c>AddState</c>'s 657 packet and its gained-side
            /// dispatch at a point where the actor is not on a map yet, so the
            /// restore builds the nodes directly instead.
            /// </summary>
            public void CopyFrom(ushort[] source)
            {
                _owner?.RestoreLegacyStatusSlots(source);
            }
        }

        private LegacyStatusTimeView _legacyStatusTimeView;

        /// <summary>
        /// Native's only timed-state store, addressed the legacy way. Slot i is
        /// native state 31 - i and the unit is seconds; the backing node is in
        /// milliseconds.
        ///
        /// Deliberately a get-only property. The old <c>ushort[12]</c> field
        /// could be reassigned, and several call sites did exactly that while
        /// leaving the node list untouched; making it unassignable turns every
        /// one of those into a compile error rather than a silent divergence.
        /// </summary>
        public LegacyStatusTimeView m_wStatusTimeArr =>
            _legacyStatusTimeView ??= new LegacyStatusTimeView(this);

        /// <summary>
        /// Legacy-shaped write. A duration-only assignment carries no value, so
        /// this cannot go through AddState: AddState @0x773117 only refreshes on
        /// `value &gt; node.Value` (`cmp edi,eax / jle`) and @0x773140 only extends
        /// on a longer duration (`cmp eax,[ebp-4] / jge`), which would make the
        /// many `slot = 1` writes ("expire almost immediately") no-ops. Existing
        /// nodes therefore get their remaining time set directly, which is what
        /// the legacy array meant; only a fresh node goes through AddState so the
        /// 0x772F84 gate, the bitset and the gained-side dispatch all still run.
        /// </summary>
        private void SetLegacyStatusSlot(int slot, ushort seconds)
        {
            var internalType = LegacyStatusSlotToNativeState(slot);
            if (seconds == 0)
            {
                RemoveTimedAbilityInternal(internalType);
                return;
            }

            var node = FindTimedAbilityInternal(internalType);
            if (node == null)
            {
                AddTimedAbilityInternal(internalType, 0,
                    seconds >= PermanentSeconds ? -1 : seconds * 1000, 0);
                return;
            }

            node.RemainingMilliseconds =
                seconds >= PermanentSeconds ? -1 : seconds * 1000;
            node.LastTick = HUtil32.GetTickCount();
        }

        /// <summary>
        /// Drops every node in the legacy band (native states 20..31) without
        /// broadcasting. Replaces the old <c>m_wStatusTimeArr = new ushort[12]</c>
        /// reset, which zeroed the array and left the node list untouched.
        ///
        /// Silent on purpose: both callers - <c>Initialize</c> and
        /// <c>TPlayObject.ClearStatusTime</c>, the latter from the dead-on-login
        /// branch in <c>UsrEngn</c> - run before the actor is on a map, so the
        /// 657 broadcast and the expiry messages that <c>RemoveTimedAbilityInternal</c>
        /// would fire have no audience and no native counterpart. This matches
        /// <c>ClearTimedAbilitiesOnExit</c>, which also drops nodes quietly.
        /// </summary>
        internal void ClearLegacyStatusSlots()
        {
            for (var slot = 0; slot < LegacyStatusSlotCount; slot++)
            {
                var internalType = LegacyStatusSlotToNativeState(slot);
                if (!HasNativeActiveState(internalType))
                {
                    continue;
                }
                ClearNativeActiveState(internalType);
                UnlinkTimedAbilityNode(internalType);
            }
        }

        /// <summary>
        /// Rebuilds the legacy band from a seconds snapshot without broadcasting.
        /// Node fields mirror AddState's fresh-record writes @0x773150-0x77317C:
        /// flag 0, type, remaining ms, value 0, LastTick = GetTickCount
        /// (0x7731AB <c>call 0x408340</c> / 0x7731B3 <c>mov [edx+6],eax</c>).
        /// </summary>
        private void RestoreLegacyStatusSlots(ushort[] source)
        {
            ClearLegacyStatusSlots();
            if (source == null)
            {
                return;
            }

            var now = HUtil32.GetTickCount();
            for (var slot = 0; slot < LegacyStatusSlotCount && slot < source.Length; slot++)
            {
                var seconds = source[slot];
                if (seconds == 0)
                {
                    continue;
                }

                var internalType = LegacyStatusSlotToNativeState(slot);
                m_TimedAbilityHead = new TimedAbilityNode
                {
                    Flag = 0,
                    InternalType = internalType,
                    RemainingMilliseconds =
                        seconds >= PermanentSeconds ? -1 : seconds * 1000,
                    LastTick = now,
                    Value = 0,
                    Next = m_TimedAbilityHead
                };
                SetNativeActiveState(internalType);
            }
        }

        /// <summary>Detaches a node from the Self+0xDC list without side effects.</summary>
        private void UnlinkTimedAbilityNode(byte internalType)
        {
            TimedAbilityNode previous = null;
            for (var node = m_TimedAbilityHead; node != null; node = node.Next)
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
                    return;
                }
                previous = node;
            }
        }
    }
}
