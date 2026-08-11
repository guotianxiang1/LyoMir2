using System;

namespace GameSvr
{
    /// <summary>
    /// STATE-01/02/03/05 structural layer: body state duration tracking via
    /// the native obj+0xDC linked list and obj+0x168 bitset.
    ///
    /// Native evidence: EA 0x77316A (applier), 0x773000 (remove walk),
    /// 0x772974 (bts setter cmp bl,0x6F/ja→no-op), 0x7729A8 (btr clearer).
    ///
    /// Node layout (18 bytes, verified at 0x773150-0x77317C):
    ///   +0x00 [1] flags (updated in modify path 0x77310F)
    ///   +0x01 [1] StateId (byte, 0..0x6F valid per 0x772993)
    ///   +0x02 [4] Value (uint, ability modifier)
    ///   +0x06 [4] TickRef (int, re-stamped at load 0x7731B3)
    ///   +0x0A [4] DurationMs (int, -1=permanent per STATE-05)
    ///   +0x0E [4] pNext (next node pointer, prepend at 0x773176)
    ///
    /// Bitset: obj+0x168, 112 bits (0..0x6F = 0..111), accessed via bt/bts/btr.
    /// C# maps to m_nCharStatus (bits 0..31), m_nCharStatus2 (32..63),
    /// m_nCharStatus3 (64..95), m_nCharStatus4 (96..127). Valid range 0..0x6F.
    ///
    /// Tick: 500ms gate (>=, not >), wall-clock elapsed decrement, mov latch
    /// (not +=). Permanent duration (-1) short-circuits before arithmetic.
    ///
    /// Coexists with legacy m_wStatusTimeArr[12] (not replaced, per task req).
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
