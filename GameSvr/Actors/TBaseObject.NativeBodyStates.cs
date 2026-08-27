using System.Collections.Generic;

namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>obj+0xDC singly-linked list of persistent body states.
        /// 0xDC = 220 falls in TCreature's own band [0x00C, 0x450), so the list
        /// belongs to every actor, not to the player - it sat on TPlayObject only
        /// because the codec that populates it lives there.
        /// Persistence-only mirror: the live state layer (m_wStatusTimeArr /
        /// m_nCharStatus*) is a synthetic overlay whose slot-to-bit mapping
        /// intentionally differs, so it is deliberately NOT rewired here.</summary>
        public List<NativeBodyStateEntry> m_NativeBodyStates = new List<NativeBodyStateEntry>();

        public struct NativeBodyStateEntry
        {
            /// <summary>node+0x01 -> wire+0x00.</summary>
            public byte StateId;

            /// <summary>node+0x02 -> wire+0x02.</summary>
            public uint Value;

            /// <summary>node+0x0A -> wire+0x06. node+0x06 is a live tick that the
            /// loader re-stamps from GetTickCount (0x6E4359) and is deliberately
            /// NOT on the wire.</summary>
            public uint Duration;
        }
    }
}
