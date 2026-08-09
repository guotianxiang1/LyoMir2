using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace GameSvr
{
    public partial class TPlayObject
    {
        // ---------------------------------------------------------------- type 2
        // shenYou, obj+0x5A4, 24 bytes verbatim.
        //   size  sub_6E4B4C  `mov eax,0x18 / ret`   -> unconditional
        //   emit  sub_6E4B54  Move(Self+0x5A4, dest, 0x18)
        //   parse sub_6E42D8  cmp edx,0x18 / je      -> any other length REJECTED
        // Because the size fn is a bare constant, native emits this section on
        // every save; all 34 goldens carry it, all 24 bytes zero.
        // Field meanings inside the window are not needed: it is a verbatim
        // passthrough on both sides.

        private byte[] BuildNativeShenYouPayload()
        {
            var payload = new byte[NativeShenYouBlockSize];
            if (m_NativeShenYouBlock != null)
            {
                var n = Math.Min(m_NativeShenYouBlock.Length, NativeShenYouBlockSize);
                Array.Copy(m_NativeShenYouBlock, 0, payload, 0, n);
            }
            return payload;
        }

        private void ApplyNativeShenYouPayload(byte[] payload)
        {
            // 0x6E42DC: exactly 24 or the whole section is discarded and
            // obj+0x5A4 keeps its ctor zeros.
            if (payload == null || payload.Length != NativeShenYouBlockSize)
            {
                return;
            }
            m_NativeShenYouBlock = (byte[])payload.Clone();
        }

        // ---------------------------------------------------------------- type 8
        // FirstDoSome, obj+0x1938, one dword bitset (indices 0..31).
        //   size  sub_6E4CB4  `mov eax,4 / ret`      -> unconditional
        //   emit  sub_6E4CBC  Move(Self+0x1938, dest, 4)
        //   parse sub_6E4464  cmp edx,4 / jne        -> any other length REJECTED

        private byte[] BuildNativeFirstDoSomePayload()
        {
            var payload = new byte[NativeFirstDoSomeSize];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, m_dwNativeFirstDoSome);
            return payload;
        }

        private void ApplyNativeFirstDoSomePayload(byte[] payload)
        {
            if (payload == null || payload.Length != NativeFirstDoSomeSize)
            {
                return;
            }
            m_dwNativeFirstDoSome = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        }

        /// <summary>
        /// sub_6F6CE4: `cmp cl,0x1F / ja -> False`, then bt obj+0x1938.
        /// </summary>
        internal bool HasNativeFirstDoSome(int index)
        {
            if (index < 0 || index > 0x1F)
            {
                return false;
            }
            return ((m_dwNativeFirstDoSome >> index) & 1) == 1;
        }

        /// <summary>
        /// sub_6F6CB8: tests first and returns early when already set (idempotent
        /// fire-once), bounds at 0x1F, then `bts obj+0x1938`.
        /// </summary>
        internal bool SetNativeFirstDoSome(int index)
        {
            if (index < 0 || index > 0x1F || HasNativeFirstDoSome(index))
            {
                return false;
            }
            m_dwNativeFirstDoSome |= 1u << index;
            return true;
        }

        // ---------------------------------------------------------------- type 7
        // coldTime, obj+0x504 TList of 12-byte {key, remaining, total}.
        //   size  sub_6E4C28  Count*12, then +4 when Count>0 (0x6E4C44)
        //   emit  sub_6E4C4C  dword 0xFAFA, then 12 bytes per element
        //   parse sub_6E43B8  branches on the 0xFAFA inner magic
        // The legacy branch (no magic) reads 8-byte elements and zero-fills Total,
        // and it returns True with NO log line -- so a missing inner magic corrupts
        // silently. Always write it.

        private byte[] BuildNativeColdTimePayload()
        {
            var entries = m_NativeColdTimes;
            if (entries == null || entries.Count == 0)
            {
                // 0x6E4C40 `test eax,eax / jle` returns 0, and the emit gate at
                // 0x6E4E94 then skips the section entirely.
                return Array.Empty<byte>();
            }
            var payload = new byte[sizeof(uint)
                                   + entries.Count * NativeColdTimeElementSize];
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, sizeof(uint)),
                NativeColdTimeInnerMagic);
            var offset = sizeof(uint);
            foreach (var entry in entries)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(offset, sizeof(uint)), entry.Key);
                BinaryPrimitives.WriteInt32LittleEndian(
                    payload.AsSpan(offset + 4, sizeof(int)), entry.Remaining);
                BinaryPrimitives.WriteInt32LittleEndian(
                    payload.AsSpan(offset + 8, sizeof(int)), entry.Total);
                offset += NativeColdTimeElementSize;
            }
            return payload;
        }

        private void ApplyNativeColdTimePayload(byte[] payload)
        {
            m_NativeColdTimes = new List<NativeColdTimeEntry>();
            if (payload == null || payload.Length < sizeof(uint))
            {
                return;
            }
            // 0x6E43C6: the inner magic selects the element width.
            var modern = BinaryPrimitives.ReadUInt32LittleEndian(
                payload.AsSpan(0, sizeof(uint))) == NativeColdTimeInnerMagic;
            if (modern)
            {
                // 0x6E43CE `add esi,4` skips the magic, then 12-byte elements while
                // esi < declaredLength.
                var offset = sizeof(uint);
                while (offset + NativeColdTimeElementSize <= payload.Length)
                {
                    m_NativeColdTimes.Add(new NativeColdTimeEntry
                    {
                        Key = BinaryPrimitives.ReadUInt32LittleEndian(
                            payload.AsSpan(offset, sizeof(uint))),
                        Remaining = BinaryPrimitives.ReadInt32LittleEndian(
                            payload.AsSpan(offset + 4, sizeof(int))),
                        Total = BinaryPrimitives.ReadInt32LittleEndian(
                            payload.AsSpan(offset + 8, sizeof(int)))
                    });
                    offset += NativeColdTimeElementSize;
                }
                return;
            }
            // Legacy leg 0x6E4410: 8-byte elements, node zero-filled first
            // (0x6E442A) so Total stays 0. Read-only -- never emitted.
            var legacyOffset = 0;
            while (legacyOffset + 8 <= payload.Length)
            {
                m_NativeColdTimes.Add(new NativeColdTimeEntry
                {
                    Key = BinaryPrimitives.ReadUInt32LittleEndian(
                        payload.AsSpan(legacyOffset, sizeof(uint))),
                    Remaining = BinaryPrimitives.ReadInt32LittleEndian(
                        payload.AsSpan(legacyOffset + 4, sizeof(int))),
                    Total = 0
                });
                legacyOffset += 8;
            }
        }

        // ---------------------------------------------------------------- type 6
        // bodyState, obj+0xDC singly-linked list, 10 bytes per persistent element.
        //   size  sub_6E4B70  10 per node passing sub_791D54
        //   emit  sub_6E4BB4  {stateId, 0x00 pad, value dword, duration dword}
        //   parse sub_6E4304  stride 10; id >= 107 aborts the WHOLE section
        //                     (0x6E432D `sub al,0x6B` / `jae`), while a merely
        //                     non-persistent id is silently skipped (0x6E433A).
        // Native walks head-first on emit and PREPENDS on parse, so one round trip
        // reverses list order and a second restores it. That is native behaviour;
        // reproduced rather than fixed.

        private byte[] BuildNativeBodyStatePayload()
        {
            var entries = m_NativeBodyStates;
            if (entries == null || entries.Count == 0)
            {
                return Array.Empty<byte>();
            }
            var kept = new List<NativeBodyStateEntry>(entries.Count);
            foreach (var entry in entries)
            {
                // 0x6E4BDC / 0x6E4BE3: non-persistent nodes contribute nothing.
                if (IsNativePersistentBodyState(entry.StateId))
                {
                    kept.Add(entry);
                }
            }
            if (kept.Count == 0)
            {
                // Size fn returned 0, so the 0x6E4E62 gate drops the section.
                return Array.Empty<byte>();
            }
            var payload = new byte[kept.Count * NativeBodyStateElementSize];
            var offset = 0;
            foreach (var entry in kept)
            {
                payload[offset] = entry.StateId;
                // 0x6E4BED writes an explicit zero pad byte.
                payload[offset + 1] = 0;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(offset + 2, sizeof(uint)), entry.Value);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(offset + 6, sizeof(uint)), entry.Duration);
                offset += NativeBodyStateElementSize;
            }
            return payload;
        }

        private void ApplyNativeBodyStatePayload(byte[] payload)
        {
            m_NativeBodyStates = new List<NativeBodyStateEntry>();
            if (payload == null)
            {
                return;
            }
            var parsed = new List<NativeBodyStateEntry>();
            var offset = 0;
            while (offset < payload.Length)
            {
                if (offset + NativeBodyStateElementSize > payload.Length)
                {
                    // Native would over-read up to 9 bytes past the section here
                    // (the loop only tests edi < declaredLength). Reading past our
                    // own payload array is not reproducible in C# and the garbage
                    // id would almost certainly be >= 107 and abort the section, so
                    // stop instead.
                    break;
                }
                var stateId = payload[offset];
                // 0x6E432D `sub al,0x6B` / 0x6E432F `jae` -> id >= 107 rejects the
                // ENTIRE section and nothing is applied.
                if (stateId >= 107)
                {
                    m_NativeBodyStates = new List<NativeBodyStateEntry>();
                    return;
                }
                if (IsNativePersistentBodyState(stateId))
                {
                    // 0x6E4370-0x6E437F prepends, reversing wire order.
                    parsed.Insert(0, new NativeBodyStateEntry
                    {
                        StateId = stateId,
                        Value = BinaryPrimitives.ReadUInt32LittleEndian(
                            payload.AsSpan(offset + 2, sizeof(uint))),
                        Duration = BinaryPrimitives.ReadUInt32LittleEndian(
                            payload.AsSpan(offset + 6, sizeof(uint)))
                    });
                }
                offset += NativeBodyStateElementSize;
            }
            m_NativeBodyStates = parsed;
        }
    }
}
