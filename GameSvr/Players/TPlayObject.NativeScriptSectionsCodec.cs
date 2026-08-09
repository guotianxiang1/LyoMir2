using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace GameSvr
{
    public partial class TPlayObject
    {
        /// <summary>
        /// sub_791D54(stateId): Boolean — the per-element persistence gate for
        /// ScriptData type 6, applied on BOTH sides (size 0x6E4B93, emit 0x6E4BDC,
        /// parse 0x6E4333; the hero codec reuses it at 0x68AB3B/0x68AB84/0x68AFA1).
        ///
        /// This is Delphi `not (State in [<set>])`, so the polarity is the OPPOSITE
        /// of how the bitmap reads:
        ///     791D54  cmp al,0x67
        ///     791D56  ja  0x791D62          ; >0x67 skips the bt, leaving CF=0
        ///     791D58  and eax,0x7F
        ///     791D5B  bt  dword [0x791D6C],eax
        ///     791D62  jae 0x791D67          ; CF=0 (bit CLEAR) -> True
        ///     791D64  xor eax,eax / ret     ; CF=1 (bit SET)   -> False
        ///     791D67  mov al,1  / ret
        /// A bit that is SET means the state is NOT persisted. Because `ja` bypasses
        /// the `bt` with CF clear, ids above 0x67 (104/105/106) ARE persisted.
        ///
        /// Bitmap image at 0x791D6C, transcribed verbatim:
        ///   FF FF 0F 90 00 00 FE FF FF 00 C0 03 40 00 00 00
        /// Excluded ids: 0..19, 28, 31, 49..71, 86..89, 102 — i.e. the poisons,
        /// stStone/stFreezeForever, the two mount states and csFatal, exactly the
        /// states you would not want re-applied at login.
        ///
        /// Adjudicated against ORIGINAL DBServer output, not just disassembly: the
        /// 34 golden ScriptData blobs contain 252 type-6 elements whose stateIds are
        /// {20,21,22,23,32,33,34,36,37,40,41,78}. All 252 satisfy this reading; none
        /// satisfies the inverse. (staging/_sd_golden2.py)
        /// </summary>
        private static readonly byte[] NativeBodyStateNonPersistentBitmap =
        {
            0xFF, 0xFF, 0x0F, 0x90, 0x00, 0x00, 0xFE, 0xFF,
            0xFF, 0x00, 0xC0, 0x03, 0x40, 0x00, 0x00, 0x00
        };

        internal static bool IsNativePersistentBodyState(byte stateId)
        {
            // 0x791D54 / 0x791D56: the compare is on the unsigned byte, and the
            // `ja` path reaches `jae` with CF still clear, i.e. True.
            if (stateId > 0x67)
            {
                return true;
            }
            // 0x791D58 `and eax,0x7F`, then `bt dword [0x791D6C],eax`.
            var index = stateId & 0x7F;
            var bitSet = ((NativeBodyStateNonPersistentBitmap[index >> 3] >> (index & 7)) & 1) == 1;
            // 0x791D62 `jae` -> True when the bit is CLEAR.
            return !bitSet;
        }

        /// <summary>
        /// Rebuilds m_NativeScriptData so that sections 2/6/7/8 reflect current
        /// runtime state, leaving every other section (0, 1 and the C#-only 0x79)
        /// byte-identical and in place. Mirrors the ordering of sub_6E4CD8's ladder:
        /// 2 before 6 before 7 before 8, and each of those after 0/1.
        /// Returns false only when the existing blob is malformed, in which case
        /// nothing is modified.
        /// </summary>
        internal bool PersistNativeScriptSections()
        {
            if (!TryParseNativeScriptSections(m_NativeScriptData, out var sections))
            {
                return false;
            }

            SetNativeScriptSection(sections, NativeScriptTypeShenYou,
                BuildNativeShenYouPayload());
            SetNativeScriptSection(sections, NativeScriptTypeBodyState,
                BuildNativeBodyStatePayload());
            SetNativeScriptSection(sections, NativeScriptTypeColdTime,
                BuildNativeColdTimePayload());
            SetNativeScriptSection(sections, NativeScriptTypeFirstDoSome,
                BuildNativeFirstDoSomePayload());

            m_NativeScriptData = EncodeNativeScriptSections(sections);
            return true;
        }

        /// <summary>
        /// Loads sections 2/6/7/8 out of the blob into the runtime mirrors. Absent
        /// or malformed sections leave the mirrors at their native post-ctor values
        /// (THumanKind ctor sub_73BF00 zero-fills all four), matching the parser's
        /// swallow-and-continue behaviour: sub_6E448C installs SEH at 0x6E44AA and
        /// every per-type failure only formats a log line.
        /// </summary>
        internal void RestoreNativeScriptSections()
        {
            m_NativeShenYouBlock = new byte[NativeShenYouBlockSize];
            m_dwNativeFirstDoSome = 0;
            m_NativeColdTimes = new List<NativeColdTimeEntry>();
            m_NativeBodyStates = new List<NativeBodyStateEntry>();

            if (!TryParseNativeScriptSections(m_NativeScriptData, out var sections))
            {
                return;
            }

            foreach (var section in sections)
            {
                switch (section.Type)
                {
                    case NativeScriptTypeShenYou:
                        ApplyNativeShenYouPayload(section.Payload);
                        break;
                    case NativeScriptTypeBodyState:
                        ApplyNativeBodyStatePayload(section.Payload);
                        break;
                    case NativeScriptTypeColdTime:
                        ApplyNativeColdTimePayload(section.Payload);
                        break;
                    case NativeScriptTypeFirstDoSome:
                        ApplyNativeFirstDoSomePayload(section.Payload);
                        break;
                }
            }
        }

        internal readonly struct NativeScriptSection
        {
            public NativeScriptSection(byte type, byte[] payload)
            {
                Type = type;
                Payload = payload;
            }

            public byte Type { get; }
            public byte[] Payload { get; }
        }

        /// <summary>
        /// Walks the outer prefix and the 7-byte section headers exactly as
        /// sub_6E448C does. A null/empty blob parses as "no sections" so that a
        /// character created fresh by C# still gets a correctly framed blob.
        /// </summary>
        internal static bool TryParseNativeScriptSections(byte[] raw,
            out List<NativeScriptSection> sections)
        {
            sections = new List<NativeScriptSection>();
            if (raw == null || raw.Length == 0)
            {
                return true;
            }
            if (raw.Length < sizeof(int))
            {
                return false;
            }
            // 0x6E44B5 reads the prefix as the loop bound; 0x6E4DBC writes it as
            // total-4.
            var declared = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0, sizeof(int)));
            if (declared != raw.Length - sizeof(int))
            {
                return false;
            }
            var offset = sizeof(int);
            while (offset < raw.Length)
            {
                if (raw.Length - offset < NativeScriptSectionHeaderSize
                    || BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(offset, 4))
                    != NativeScriptSectionMagic)
                {
                    return false;
                }
                var length = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(offset + 4, 2));
                var type = raw[offset + 6];
                offset += NativeScriptSectionHeaderSize;
                if (offset + length > raw.Length)
                {
                    return false;
                }
                sections.Add(new NativeScriptSection(type,
                    raw.AsSpan(offset, length).ToArray()));
                offset += length;
            }
            return offset == raw.Length;
        }

        internal static byte[] EncodeNativeScriptSections(
            List<NativeScriptSection> sections)
        {
            var total = 0;
            foreach (var section in sections)
            {
                total += NativeScriptSectionHeaderSize + section.Payload.Length;
            }
            var raw = new byte[sizeof(int) + total];
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0, sizeof(int)), total);
            var offset = sizeof(int);
            foreach (var section in sections)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(offset, 4),
                    NativeScriptSectionMagic);
                BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(offset + 4, 2),
                    (ushort)section.Payload.Length);
                raw[offset + 6] = section.Type;
                offset += NativeScriptSectionHeaderSize;
                section.Payload.CopyTo(raw, offset);
                offset += section.Payload.Length;
            }
            return raw;
        }

        /// <summary>
        /// Replaces the payload of <paramref name="type"/> in place, or inserts it
        /// at the native ladder position when absent. An empty payload removes the
        /// section, matching the `jle` size gates (0x6E4E62 type 6, 0x6E4E94
        /// type 7) that skip a section whose size fn returned 0.
        /// </summary>
        private static void SetNativeScriptSection(List<NativeScriptSection> sections,
            byte type, byte[] payload)
        {
            for (var i = 0; i < sections.Count; i++)
            {
                if (sections[i].Type != type)
                {
                    continue;
                }
                if (payload.Length == 0)
                {
                    sections.RemoveAt(i);
                }
                else
                {
                    sections[i] = new NativeScriptSection(type, payload);
                }
                return;
            }
            if (payload.Length == 0)
            {
                return;
            }
            // Native ladder order is 0, 1, 2, 6, 7, 8 (sub_6E4CD8 two-pass), so
            // insert ahead of the first section that sorts after this one. The
            // C#-only 0x79 sidecar is > 8 and therefore always lands last.
            for (var i = 0; i < sections.Count; i++)
            {
                if (NativeScriptLadderRank(sections[i].Type) > NativeScriptLadderRank(type))
                {
                    sections.Insert(i, new NativeScriptSection(type, payload));
                    return;
                }
            }
            sections.Add(new NativeScriptSection(type, payload));
        }

        private static int NativeScriptLadderRank(byte type)
        {
            return type;
        }
    }
}
