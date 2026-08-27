using System;

namespace GameSvr
{
    /// <summary>
    /// The 神佑 named-ability block — builder sub_746D6C and consumer sub_75F548.
    ///
    /// ── Where it lives
    /// The block is 10 entries of {word code; word value} at container+0x375, i.e.
    /// 0x28 bytes, which is exactly the FillChar sub_746D6C opens with (0x746D8F
    /// `add eax,0x375` / 0x746D96 `mov edx,0x28`). "container" is *(actor+0x4C0),
    /// the equipment container that also holds the two ability blocks at +0x48 and
    /// +0x1F8 (sub_75F4F8 clears 0x1B0 and 0x36 bytes there).
    ///
    /// ── Builder sub_746D6C, per slot i in 0..9
    ///   0x746DC1  id = word[self + i*2 + 0x5A8]
    ///   0x746DCC  id == 0            -> leave the entry zeroed
    ///   0x746DD8  entry = [[0x7D6014]].lookup(id); nil -> leave the entry zeroed
    ///   0x746DE6  s = ShortString at entry+0xC          (the configured "name value")
    ///   0x746E00  head, rest = SplitFirst(s, ' ')       (sub_4C6AEC, `mov cl,0x20`)
    ///   0x746E16  s = Trim(rest)                        (sub_40C140)
    ///   0x746E2F  word[block + i*4 + 0x000] = NameLookup(head)   (sub_78FB6C)
    ///   0x746E44  word[block + i*4 + 0x002] = StrToIntDef(s, 10) (sub_40CA18, default 10)
    /// Only the low word of the StrToIntDef result is kept (`mov word [..], ax`).
    ///
    /// The builder runs at the two soul-wash commits unconditionally (0x747420,
    /// 0x74750E) and on the two login/refresh paths behind `cmp byte[obj+0x5BC],0`
    /// / `jbe` (0x687F14, 0x6B207B) — so a character with no slots never rebuilds.
    ///
    /// ── Consumer sub_75F548, per slot i in 0..9
    ///   0x75F554  code = word[block + i*4]; `test ax,ax` / `jbe` skips code 0
    ///             (test always clears CF, so `jbe` here is exactly `je`)
    ///   0x75F561  value = zero-extended word[block + i*4 + 2]
    ///   0x75F56E  sub_75F588 -> sub_78E830(base@eax, added@edx, code@ecx, value@stack)
    ///   0x75F573  applied++
    ///   0x75F574  `cmp edi,[[0x7D5AEC]]` / `je` — the cap test runs after EVERY slot,
    ///             including the skipped ones, and it tests equality rather than >=.
    /// That last detail matters at the edges: a cap of 0 with an empty first slot
    /// stops before applying anything, while a cap of 0 with a filled first slot
    /// never hits equality again and applies all ten.
    /// </summary>
    public static class NativeShenYouAbilityBlock
    {
        /// <summary>Entries in the block; the slot window at [+0x5A8] is the same width.</summary>
        public const int SlotCount = 10;

        /// <summary>Bytes per entry: word code + word value.</summary>
        public const int EntrySize = 4;

        /// <summary>0x746D96 `mov edx,0x28`.</summary>
        public const int BlockSize = SlotCount * EntrySize;

        /// <summary>Container displacement of the block (0x746D8F `add eax,0x375`).</summary>
        public const int ContainerOffset = 0x375;

        /// <summary>0x746E3C `mov edx,0xA` — the StrToIntDef fallback is 10, not 0.</summary>
        public const int ValueDefault = 10;

        /// <summary>0x746DFB `mov cl,0x20` — the name and the value are space separated.</summary>
        private const char Divider = ' ';

        /// <summary>
        /// sub_746D6C. <paramref name="slotIds"/> is the 10-word window at [+0x5A8];
        /// the returned block is always <see cref="BlockSize"/> bytes, zero-filled
        /// first, so unresolved slots stay at code 0 and are skipped by the consumer.
        /// </summary>
        public static byte[] Build(ReadOnlySpan<ushort> slotIds,
            NativeShenYouAttributeConfig config)
        {
            var block = new byte[BlockSize];
            if (config == null)
            {
                return block;
            }

            for (var i = 0; i < SlotCount && i < slotIds.Length; i++)
            {
                var id = slotIds[i];
                if (id == 0 || !config.TryGet(id, out var entry))
                {
                    continue;
                }

                var head = SplitFirst(entry.Name ?? string.Empty, Divider, out var rest);
                var code = NativeAbilityNameTable.Lookup(head);
                var value = NativeShenYouAttributeConfig.StrToIntDef(
                    DelphiTrim(rest), ValueDefault);

                var at = i * EntrySize;
                block[at] = (byte)code;
                block[at + 1] = (byte)(code >> 8);
                block[at + 2] = (byte)value;
                block[at + 3] = (byte)(value >> 8);
            }
            return block;
        }

        /// <summary>
        /// sub_75F548. <paramref name="container"/> is the container-shaped byte image
        /// whose base ability block starts at <see cref="NativeAbilityApply.BaseBlockOffset"/>
        /// and whose added block starts at <see cref="NativeAbilityApply.AddedBlockOffset"/>.
        /// </summary>
        public static void Apply(Span<byte> container, ReadOnlySpan<byte> block, int slotCap)
        {
            if (block.Length < BlockSize)
            {
                return;
            }

            var baseBlock = container.Slice(NativeAbilityApply.BaseBlockOffset,
                NativeAbilityApply.BaseBlockSize);
            var addedBlock = container.Slice(NativeAbilityApply.AddedBlockOffset,
                NativeAbilityApply.AddedBlockSize);

            var applied = 0;
            for (var i = 0; i < SlotCount; i++)
            {
                var at = i * EntrySize;
                var code = block[at] | (block[at + 1] << 8);
                if (code != 0)
                {
                    var value = block[at + 2] | (block[at + 3] << 8);
                    NativeAbilityApply.Apply(baseBlock, addedBlock, code, value);
                    applied++;
                }

                // 0x75F574: reached from BOTH the applied and the skipped path.
                if (applied == slotCap)
                {
                    return;
                }
            }
        }

        /// <summary>True when no entry carries a code, i.e. the block is a no-op.</summary>
        public static bool IsEmpty(ReadOnlySpan<byte> block)
        {
            if (block.Length < BlockSize)
            {
                return true;
            }
            for (var i = 0; i < SlotCount; i++)
            {
                var at = i * EntrySize;
                if ((block[at] | (block[at + 1] << 8)) != 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// sub_4C6AEC — the text before the first divider, with the text after it left
        /// in <paramref name="remainder"/>. No divider means the whole string and an
        /// empty remainder.
        /// </summary>
        private static string SplitFirst(string source, char divider, out string remainder)
        {
            source ??= string.Empty;
            var at = source.IndexOf(divider);
            if (at < 0)
            {
                remainder = string.Empty;
                return source;
            }
            remainder = source.Substring(at + 1);
            return source.Substring(0, at);
        }

        /// <summary>
        /// sub_40C140 — Delphi Trim strips every character &lt;= ' ' from both ends,
        /// which is a narrower rule than <c>string.Trim()</c>'s Unicode whitespace set.
        /// </summary>
        private static string DelphiTrim(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            var start = 0;
            var end = text.Length - 1;
            while (start <= end && text[start] <= ' ')
            {
                start++;
            }
            while (end >= start && text[end] <= ' ')
            {
                end--;
            }
            return start > end ? string.Empty : text.Substring(start, end - start + 1);
        }
    }
}
