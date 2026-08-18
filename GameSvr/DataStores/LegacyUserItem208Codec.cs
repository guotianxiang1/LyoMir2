using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// The 208-byte 战神 item record — the verbatim image of <c>item+0x20 .. item+0xEF</c>
    /// (LOAD <c>sub_74DAE4</c> @0x74DB3A <c>lea edi,[ebx+0x20]</c> / @0x74DB3D
    /// <c>mov ecx,0x34</c> / @0x74DB42 <c>rep movsd</c>; SAVE @0x6B170F..0x6B1717 the
    /// same three instructions in the opposite direction).
    ///
    /// Bytes 0x18..0x55 are NOT unmapped padding.  They are the 眼神 "装备出处"
    /// provenance block, already reverse-engineered byte-for-byte in
    /// <c>GameSvr.Plugins.BigBag.YanshenBigBagRecord</c> from 31 production files /
    /// 1399 records, and independently re-confirmed here against the 30 golden M2
    /// saves (1363 live items): DateDays u16 @0x1C, Minute @0x1E, Hour @0x1F,
    /// map title GBK[12] @0x20, dword @0x2C, source-name length @0x30 with the name
    /// split 7 bytes @0x31 + 7 bytes @0x3C around the always-zero hole @0x38,
    /// map-code length @0x43, char-name length @0x44 + name[14] @0x45,
    /// map-code-length copy @0x53, source kind @0x54, origin marker 0x00/0xFF @0x55.
    /// All nine structural invariants hold with 0 violations on the golden corpus and
    /// every map title / source name / character name decodes as clean GBK.
    ///
    /// Consequence for this codec: the old "everything past the core must be zero"
    /// tail check rejected 1232 of 1363 real records (90.4%), every one of them at
    /// offset 0x1C, which is the provenance date.  Since <see cref="TryEncode"/>
    /// clones <c>NativeRecord</c> verbatim before patching, that check never
    /// protected any byte — it only refused real data.  The fail-closed guard is now
    /// scoped to the two spans that are genuinely zero in all 2762 sampled records.
    /// </summary>
    internal static class LegacyUserItem208Codec
    {
        internal const int RecordSize = 208;
        internal const int HexLength = RecordSize * 2;
        internal const int CoreSize = 24;

        /// <summary>
        /// Native weapon-upgrade flag byte. Set with <c>or byte [esi+0x47],0x80</c>
        /// (@0x6CA0F3, 不破碎) and <c>or byte [esi+0x47],0x40</c> (@0x6CA10D, 必成功);
        /// read at @0x6D7A93 <c>mov al,[ebx+0x47]</c> + <c>and al,0x80</c>; cleared with
        /// <c>mov byte [ebx+0x47],0</c> at @0x6D7AE5 and @0x6D7B07.  The offset is
        /// correct, but in production the same byte is also the trail byte of the 4th
        /// GBK character of the provenance map title, so its low six bits carry player
        /// data and must be preserved rather than asserted to be zero.
        /// </summary>
        internal const int UpgradeFlagsOffset = 0x27;

        /// <summary>
        /// NOT the bind flag.  <c>record[0xB8]</c> is <c>item+0xD8</c>, the native
        /// 赠品 (gift) byte: zeroed by the item factory (@0x7837EE
        /// <c>mov byte [ebx+0xD8],0</c>), set to 1 at @0x67D236 / @0x6C8611 / @0x709498 /
        /// @0x7094A4, and read by all three drop paths (@0x73CD44, @0x740161, @0x73FDD0)
        /// as <c>cmp byte [item+0xD8],0; je</c>.  The real bind/lock word is
        /// <c>word[item+0x34]</c> = <c>btValue[10..11]</c> (<c>sub_784710</c> @0x784710
        /// <c>mov ax,word [eax+0x34]</c>, <c>sub_784718</c> @0x784718
        /// <c>mov word [eax+0x34],dx</c>).  The mapping is deliberately left alone:
        /// every persisted store already writes bind here, so moving it would
        /// re-interpret existing rows.  See staging/m_itemdb_20260813.md for the
        /// migration plan.
        /// </summary>
        internal const int BindOffset = 0xB8;

        internal const byte KnownUpgradeFlags = 0xC0;

        /// <summary>First byte of the span that has no known owner in any sample.</summary>
        internal const int UnownedSpanStart = 0x56;

        /// <summary>
        /// True when <paramref name="offset"/> is inside the tail but has a proven owner,
        /// so the fail-closed guard must leave it alone.
        ///
        /// The only such range past <see cref="UnownedSpanStart"/> is the 眼神 element
        /// block ys1..ys17 at record <c>0x58..0x6B</c> (in-memory item <c>+0x78..+0x8B</c>),
        /// which <see cref="SystemModule.YanshenNativeItemLayout"/> already maps and which
        /// <c>NativeHumanDataCodec.DecodeItem</c> already reads back out through
        /// <c>YanshenNativeItemLayout.Unpack</c>. Plugin dump (base 0x10000000):
        ///   0x10075D48  c7 45 ec 78 00 00 00   mov [ebp-0x14],0x78   ; ys5  -> record 0x58
        ///   0x10075D3F  c7 45 ec 79 00 00 00   ; ys4 -> 0x59
        ///   0x10075D36  c7 45 ec 7a 00 00 00   ; ys3 -> 0x5A
        ///   0x10075D2A  c7 45 ec 7b 00 00 00   ; ys2 -> 0x5B
        ///   0x10075CF9  89 70 7c               mov [eax+0x7c],esi    ; ys1  -> record 0x5C (dword)
        ///   0x10075D51  c7 45 ec 80 00 00 00   ; ys6 -> 0x60 ... ys17 -> 0x6B
        /// Asserting those bytes zero would reject every item that actually carries
        /// 眼神 element values — the same failure mode as the old blanket check, just
        /// waiting on a deployment that uses the feature. The golden corpus cannot see
        /// it: 0x56..0xCF is zero in all 1363 items and all 1399 production big-bag
        /// records, so the collision is latent rather than currently firing.
        /// </summary>
        internal static bool HasKnownOwner(int offset) =>
            offset >= SystemModule.YanshenNativeItemLayout.Ys5Offset
            && offset <= SystemModule.YanshenNativeItemLayout.Ys17Offset;

        internal static bool TryEncode(TUserItem item, out string weaponData, out string error)
        {
            weaponData = string.Empty;
            error = string.Empty;
            if (item == null || item.btValue == null || item.btValue.Length != 14)
            {
                error = "invalid core item record";
                return false;
            }
            if (HasUnmappedExtensionData(item))
            {
                error = "item contains unmapped extended attributes";
                return false;
            }

            byte[] record;
            if (item.NativeRecord == null)
            {
                record = new byte[RecordSize];
            }
            else
            {
                if (item.NativeRecord.Length != RecordSize)
                {
                    error = $"native item record must be {RecordSize} bytes";
                    return false;
                }
                record = (byte[])item.NativeRecord.Clone();
            }
            if (!TryValidateUnownedSpans(record, out error))
            {
                return false;
            }

            // Native only ever ORs bits 0x80/0x40 into this byte (@0x6CA0F3, @0x6CA10D) or
            // clears it whole (@0x6D7AE5, @0x6D7B07); it never rewrites the low six bits,
            // which in production hold the provenance map title. Refuse to do what native
            // cannot rather than silently corrupting the title.
            if ((item.UpgradeFlags & ~KnownUpgradeFlags) != 0
                && (item.UpgradeFlags & ~KnownUpgradeFlags)
                    != (record[UpgradeFlagsOffset] & ~KnownUpgradeFlags))
            {
                error = "item refine flags would rewrite bytes native never writes";
                return false;
            }

            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0, 4), item.MakeIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4, 2), item.wIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6, 2), item.Dura);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8, 2), item.DuraMax);
            item.btValue.CopyTo(record, 10);
            record[UpgradeFlagsOffset] = item.UpgradeFlags;
            record[BindOffset] = item.Bind;
            weaponData = Convert.ToHexString(record);
            return true;
        }

        internal static bool TryDecode(string weaponData, out TUserItem item, out string error)
        {
            item = null;
            error = string.Empty;
            if (!IsNativeHex(weaponData))
            {
                error = $"WeaponData must be {HexLength} uppercase hex characters";
                return false;
            }

            var record = Convert.FromHexString(weaponData);
            if (!TryValidateUnownedSpans(record, out error))
            {
                return false;
            }

            item = new TUserItem
            {
                MakeIndex = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(0, 4)),
                wIndex = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(4, 2)),
                Dura = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(6, 2)),
                DuraMax = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(8, 2)),
                UpgradeFlags = record[UpgradeFlagsOffset],
                Bind = record[BindOffset],
                NativeRecord = (byte[])record.Clone()
            };
            record.AsSpan(10, 14).CopyTo(item.btValue);
            var stdItem = M2Share.UserEngine.GetStdItem(item.wIndex);
            if (stdItem != null)
            {
                NativeSpecialDropItemRollCore.HydrateConstructorState(item,
                    stdItem);
                NativeOutOfBoundsItemClassifier.Apply(item, stdItem);
            }

            return true;
        }

        private static bool IsNativeHex(string value)
        {
            if (value == null || value.Length != HexLength) return false;
            foreach (var ch in value)
            {
                if (!((ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'F'))) return false;
            }
            return true;
        }

        /// <summary>
        /// Fail-closed guard over the bytes with no known owner: 0x56..0x57, 0x6C..0xB7
        /// and 0xB9..0xCF.  All of them are zero in all 1363 golden M2 items and all 1399
        /// production 眼神 extra-bag records.  Three ranges are excluded because they DO
        /// have an owner: 0x18..0x55 is the provenance block (the previous blanket check
        /// refused 1232 of 1363 real records, all at 0x1C), 0x58..0x6B is the ys1..ys17
        /// element block (see <see cref="HasKnownOwner"/>), and 0xB8 is the native gift
        /// byte.
        /// </summary>
        private static bool TryValidateUnownedSpans(byte[] record, out string error)
        {
            error = string.Empty;
            for (var i = UnownedSpanStart; i < record.Length; i++)
            {
                if (i == BindOffset || HasKnownOwner(i)) continue;
                if (record[i] != 0)
                {
                    error = $"unmapped native item data at offset 0x{i:X2}";
                    return false;
                }
            }
            return true;
        }

        private static bool HasUnmappedExtensionData(TUserItem item)
        {
            return item.ys1 != 0 || item.ys2 != 0 || item.ys3 != 0 || item.ys4 != 0 ||
                   item.ys5 != 0 || item.ys6 != 0 || item.ys7 != 0 || item.ys8 != 0 ||
                   item.ys9 != 0 || item.ys10 != 0 || item.ys11 != 0 || item.ys12 != 0 ||
                   item.ys13 != 0 || item.ys14 != 0 || item.ys15 != 0 || item.ys16 != 0 ||
                   item.ys17 != 0 || item.jp1 != 0 || item.jp2 != 0 || item.jp3 != 0 ||
                   item.jp4 != 0 || item.jp5 != 0 || item.jp6 != 0 ||
                   !string.IsNullOrEmpty(item.pname) || !string.IsNullOrEmpty(item.desc1) ||
                   !string.IsNullOrEmpty(item.desc2) || !string.IsNullOrEmpty(item.sourceTime) ||
                   !string.IsNullOrEmpty(item.killerName) || !string.IsNullOrEmpty(item.mapName);
        }
    }
}
