using System;

namespace GameSvr
{
    /// <summary>
    /// STATE-01/02/03/05 structural scaffolding for the native obj+0xDC duration
    /// list. Exercised only by AuditTools/NativeMagic191Check.
    ///
    /// NOT THE RUNTIME AUTHORITY. The native obj+0xDC list and its 500ms walker
    /// (sub_772FD0) are already implemented by ProcessTimedAbilities /
    /// m_TimedAbilityHead in TBaseObject.TimedAbility.cs, which Run() calls every
    /// frame. Verified at 0x772FD0-0x7730AA: GetTickCount, sub [ebx+0xE0],
    /// cmp eax,0x1F4, jb skip, walk [ebx+0xDC], per-node virtual tick at
    /// VMT+0x58, btr via 0x7729A8, detach the whole expired batch, then replay
    /// callbacks through VMT+0x5C and free via 0x764E10.
    ///
    /// Do not call ProcessNativeBodyStateDurations from Run(). Nothing in GameSvr
    /// populates m_pNativeBodyStateDurationHead, so it would be a dead walk in the
    /// per-frame path, and any node that did appear would clear bits in the shared
    /// obj+0x168 bitset behind the timed-ability list's back. Native has exactly
    /// one duration list and one walker.
    ///
    /// Node layout (18 bytes) as encoded by the native applier:
    ///   +0x00 [1] flags
    ///   +0x01 [1] StateId       (0x773164 mov [eax+1],bl; range 0..0x6F)
    ///   +0x02 [4] DurationMs    (0x77315E store; 0x773191 mov eax,[eax+2] then
    ///                            idiv 0x3E8 to report seconds; -1 = permanent)
    ///   +0x06 [4] TickRef       (0x7731B3 mov [edx+6],eax after GetTickCount)
    ///   +0x0A [4] Value         (0x77316A mov [eax+0xA],edi; read back by
    ///                            GetValue at 0x773C0B mov eax,[eax+0xA])
    ///   +0x0E [4] pNext         (0x773176 prepend to obj+0xDC)
    ///
    /// Bitset: obj+0x168, 112 bits (0..0x6F). The whole image contains exactly
    /// three instructions touching it, all register-indexed: bt at 0x772968,
    /// bts at 0x77299B, btr at 0x7729B9. C# maps it to m_nCharStatus,
    /// m_nCharStatus2, m_nCharStatus3, m_nCharStatus4.
    ///
    /// Coexists with legacy m_wStatusTimeArr[12], which is a separate
    /// second-granularity carrier and is not replaced here.
    /// </summary>
    public partial class TBaseObject
    {
        internal const int NativeBodyStateDurationTickMs = 500;
        internal const int NativeBodyStatePermanent = -1;

        public NativeBodyStateDurationNode m_pNativeBodyStateDurationHead;
        private int m_dwNativeBodyStateTick;

        public sealed class NativeBodyStateDurationNode
        {
            public byte StateId;
            public uint Value;
            public int TickRef;
            public int DurationMs;
            public NativeBodyStateDurationNode Next;
        }

        private static bool NativeBodyStateIdValid(int id)
            => id >= 0 && id <= 0x6F;

        private bool NativeBitsetSet(int id)
        {
            if (!NativeBodyStateIdValid(id))
                return false;
            return SetNativeActiveState(id);
        }

        private bool NativeBitsetClear(int id)
        {
            if (!NativeBodyStateIdValid(id))
                return false;
            return ClearNativeActiveState(id);
        }

        private bool NativeBitsetGet(int id)
        {
            if (!NativeBodyStateIdValid(id))
                return false;
            return HasNativeActiveState(id);
        }

        internal bool NativeApplyBodyState(byte stateId, int durationMs, uint value)
        {
            if (!NativeBodyStateIdValid(stateId))
                return false;

            var now = SystemModule.HUtil32.GetTickCount();
            var existing = FindNativeBodyStateDurationNode(stateId);

            if (existing != null)
            {
                existing.Value = value;
                existing.DurationMs = durationMs;
                existing.TickRef = now;
                NativeBitsetSet(stateId);
                return false;
            }

            var node = new NativeBodyStateDurationNode
            {
                StateId = stateId,
                Value = value,
                DurationMs = durationMs,
                TickRef = now,
                Next = m_pNativeBodyStateDurationHead
            };
            m_pNativeBodyStateDurationHead = node;
            NativeBitsetSet(stateId);
            return true;
        }

        internal bool NativeRemoveBodyState(byte stateId)
        {
            if (!NativeBodyStateIdValid(stateId))
                return false;

            NativeBitsetClear(stateId);

            NativeBodyStateDurationNode prev = null;
            var curr = m_pNativeBodyStateDurationHead;

            while (curr != null)
            {
                if (curr.StateId == stateId)
                {
                    if (prev == null)
                        m_pNativeBodyStateDurationHead = curr.Next;
                    else
                        prev.Next = curr.Next;
                    return true;
                }
                prev = curr;
                curr = curr.Next;
            }
            return false;
        }

        internal int NativeGetBodyStateDurationMs(byte stateId)
        {
            if (!NativeBodyStateIdValid(stateId))
                return 0;

            var node = FindNativeBodyStateDurationNode(stateId);
            if (node == null)
                return 0;

            if (node.DurationMs == NativeBodyStatePermanent)
                return NativeBodyStatePermanent;

            var now = SystemModule.HUtil32.GetTickCount();
            var elapsed = unchecked(now - node.TickRef);
            var remaining = node.DurationMs - elapsed;
            return remaining > 0 ? remaining : 0;
        }

        internal void ProcessNativeBodyStateDurations(int now)
        {
            var elapsed = unchecked(now - m_dwNativeBodyStateTick);
            if (elapsed < NativeBodyStateDurationTickMs)
                return;

            m_dwNativeBodyStateTick = now;

            NativeBodyStateDurationNode prev = null;
            var curr = m_pNativeBodyStateDurationHead;

            while (curr != null)
            {
                var next = curr.Next;

                if (curr.DurationMs != NativeBodyStatePermanent)
                {
                    var nodeElapsed = unchecked(now - curr.TickRef);
                    curr.DurationMs -= nodeElapsed;
                    curr.TickRef = now;

                    if (curr.DurationMs <= 0)
                    {
                        NativeBitsetClear(curr.StateId);
                        if (prev == null)
                            m_pNativeBodyStateDurationHead = next;
                        else
                            prev.Next = next;
                        curr = next;
                        continue;
                    }
                }

                prev = curr;
                curr = next;
            }
        }

        private NativeBodyStateDurationNode FindNativeBodyStateDurationNode(byte stateId)
        {
            var curr = m_pNativeBodyStateDurationHead;
            while (curr != null)
            {
                if (curr.StateId == stateId)
                    return curr;
                curr = curr.Next;
            }
            return null;
        }
    }
}
