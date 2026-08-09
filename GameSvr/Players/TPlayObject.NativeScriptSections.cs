using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace GameSvr
{
    /// <summary>
    /// ScriptData sections 2 / 6 / 7 / 8 — the four native section kinds C# never
    /// originated. Types 0 (act) and 1 (task) already round-trip through
    /// DBSvr/Core/NativeHumanDataCodec.cs and are NOT touched here.
    ///
    /// The blob lives at rec+0xF0A8 and is built/parsed entirely on the GAME side:
    ///   builder TPlayer.BuildScriptData = sub_6E4CD8, sole caller 0x6B65DC
    ///   parser  TPlayer.LoadScriptData  = sub_6E448C, sole caller 0x6547D1
    /// DBServer only stores the column, so nothing here belongs in DBSvr.
    ///
    /// Wire framing (verified byte-for-byte at 0x6E4E39..0x6E4E47 and re-verified
    /// against 34 real blobs written by the ORIGINAL Delphi DBServer):
    ///   +0x00  dword  payloadLength = totalAllocated - 4   (0x6E4DBC)
    ///   then, per section:
    ///   +0x00  4  magic  0xABCDEFAA
    ///   +0x04  2  payload length, UInt16 LE, EXCLUDES this 7-byte header
    ///   +0x06  1  type tag
    ///   +0x07  n  payload
    /// The section NAMES (act/task/shenYou/bodyState/coldTime/FirstDoSome) are
    /// parser-side error labels only and never appear on the wire.
    ///
    /// Native emit order is fixed by the two-pass ladder in sub_6E4CD8:
    /// 0, 1, 2, 6, 7, 8. All 34 goldens show exactly [0, 1, 2, 6, 8] — type 7 is
    /// absent because its list is empty, which matches the size-gate at 0x6E4E94.
    ///
    /// Types 3/4/5 route to the unknown-type error sink 0x6E4856 and must NEVER be
    /// emitted (cmp eax,8 / ja at 0x6E4510 bounds the jump table at 0x6E4520).
    /// </summary>
    public partial class TPlayObject
    {
        internal const uint NativeScriptSectionMagic = 0xABCDEFAA;
        internal const int NativeScriptSectionHeaderSize = 7;

        internal const byte NativeScriptTypeShenYou = 2;
        internal const byte NativeScriptTypeBodyState = 6;
        internal const byte NativeScriptTypeColdTime = 7;
        internal const byte NativeScriptTypeFirstDoSome = 8;

        /// <summary>obj+0x5A4, 24 bytes copied verbatim both ways (sub_6E4B54
        /// emit / sub_6E42D8 parse, both `mov ecx,0x18`). The parse leg demands
        /// EXACTLY 24 (0x6E42DC `cmp edx,0x18`), and the size fn sub_6E4B4C is the
        /// bare constant 0x18, so native emits this section unconditionally.</summary>
        internal const int NativeShenYouBlockSize = 0x18;

        /// <summary>obj+0x1938, a single dword bitset, indices 0..31 (the setter
        /// sub_6F6CB8 and tester sub_6F6CE4 both bound at `cmp cl,0x1F`). Size fn
        /// sub_6E4CB4 is the bare constant 4, so this is also unconditional; the
        /// parse leg sub_6E4464 demands EXACTLY 4 (0x6E4467 `cmp edx,4`).</summary>
        internal const int NativeFirstDoSomeSize = 4;

        /// <summary>Element width for type 6, from `mov ecx,0xA` at 0x6E4C07
        /// (emit) and the `add edi,0xA` stride at 0x6E4395 (parse).</summary>
        internal const int NativeBodyStateElementSize = 10;

        /// <summary>Element width for type 7, from `mov ecx,0xC` at 0x6E4C9C
        /// (emit) and 0x6E43E7 (parse); the destructor frees them as 12-byte
        /// blocks at 0x73C0E7.</summary>
        internal const int NativeColdTimeElementSize = 12;

        /// <summary>Inner magic that selects the modern 12-byte element format.
        /// `mov dword [dest],0xFAFA` at 0x6E4C5A; the parser branches on it at
        /// 0x6E43C6 and silently falls back to an 8-byte legacy element when it is
        /// missing — returning True with NO log line, so omitting it corrupts
        /// silently. Always write it.</summary>
        internal const uint NativeColdTimeInnerMagic = 0x0000FAFA;

        /// <summary>obj+0x5A4 shenYou block, held as raw bytes because native
        /// moves the window verbatim and never marshals fields.</summary>
        public byte[] m_NativeShenYouBlock;

        /// <summary>obj+0x1938 one-shot bitset.</summary>
        public uint m_dwNativeFirstDoSome;

        // obj+0x504 TList of {key, remaining, total} cooldowns.
        // m_NativeColdTimes and NativeColdTimeEntry now live on TBaseObject in
        // TBaseObject.NativeColdTime.cs, because native keeps ONE list at
        // obj+0x504 shared by the codec and the runtime arm/query/tick layer.
        // Declaring a second one here would recreate the dual-source-of-truth
        // drift this port has had to fix elsewhere.

        /// <summary>obj+0xDC singly-linked list of persistent body states.
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
