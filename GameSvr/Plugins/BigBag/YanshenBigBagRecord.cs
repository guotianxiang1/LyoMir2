using System;
using System.Buffers.Binary;
using System.Text;

namespace GameSvr.Plugins.BigBag
{
    /// <summary>
    /// One 208-byte entry of a <c>Gs1\MyJson\bags\角色名.bin</c> extra-bag file:
    /// the first 24 bytes of a <see cref="SystemModule.TUserItem"/> followed by
    /// the "装备出处" (item provenance) metadata the 眼神 plugin records for it —
    /// when it was obtained, on which map, from which monster (or from the owner
    /// himself), and who first picked it up.
    ///
    /// The layout was recovered from 31 production files / 1399 records; parsing each
    /// record into the fields below and rebuilding it from those fields alone
    /// reproduces every record byte for byte, so no byte of the 208 is unaccounted for.
    ///
    /// Three details are easy to get wrong and each one silently breaks that property:
    /// <list type="bullet">
    /// <item>The source name is <b>not</b> 14 contiguous bytes. It is 7 bytes at
    /// <c>0x31</c>, then a 4-byte hole at <c>0x38</c> that the byte sequence skips
    /// over, then 7 more bytes at <c>0x3C</c>.</item>
    /// <item><see cref="MapCodeLengthCopy"/> at <c>0x53</c> repeats
    /// <see cref="MapCodeLength"/> at <c>0x43</c> only while the character-name block
    /// is populated; with an empty character name it is 0 even though <c>0x43</c>
    /// stays non-zero (757 of 1399 records). See <see cref="DeriveMapCodeLengthCopy"/>.</item>
    /// <item><see cref="Reserved18"/>, <see cref="Hole38"/> and <see cref="Tail"/> are
    /// carried through verbatim rather than re-zeroed. They are zero in almost every
    /// sample, but three records carry <c>0x01</c> at offset <c>0xB8</c> inside the
    /// tail and dropping it would no longer be byte-equivalent.</item>
    /// </list>
    /// </summary>
    public sealed class YanshenBigBagRecord
    {
        public const int RecordSize = 208;

        public const int MakeIndexOffset = 0x00;
        public const int WIndexOffset = 0x04;
        public const int DuraOffset = 0x06;
        public const int DuraMaxOffset = 0x08;
        public const int BtValueOffset = 0x0A;
        public const int BtValueSize = 14;
        public const int Reserved18Offset = 0x18;
        public const int Reserved18Size = 4;
        public const int DateDaysOffset = 0x1C;
        public const int MinuteOffset = 0x1E;
        public const int HourOffset = 0x1F;
        public const int MapTitleOffset = 0x20;
        public const int MapTitleSize = 12;
        public const int Dword2COffset = 0x2C;
        public const int SourceNameLengthOffset = 0x30;
        public const int SourceNameHeadOffset = 0x31;
        public const int SourceNameHeadSize = 7;
        public const int Hole38Offset = 0x38;
        public const int Hole38Size = 4;
        public const int SourceNameTailOffset = 0x3C;
        public const int SourceNameTailSize = 7;
        public const int SourceNameSize = SourceNameHeadSize + SourceNameTailSize;
        public const int MapCodeLengthOffset = 0x43;
        public const int CharNameLengthOffset = 0x44;
        public const int CharNameOffset = 0x45;
        public const int CharNameSize = 14;
        public const int MapCodeLengthCopyOffset = 0x53;
        public const int SourceKindOffset = 0x54;
        public const int OriginMarkerOffset = 0x55;
        public const int TailOffset = 0x56;
        public const int TailSize = 122;

        /// <summary><see cref="SourceKind"/> value for an item dropped by a monster.</summary>
        public const byte SourceKindMonster = 0x00;

        /// <summary><see cref="SourceKind"/> value for an item sourced from the owner himself.</summary>
        public const byte SourceKindSelf = 0x01;

        /// <summary><see cref="OriginMarker"/> value on a record that carries provenance.</summary>
        public const byte OriginMarkerPresent = 0xFF;

        /// <summary><see cref="OriginMarker"/> value on a record with no provenance.</summary>
        public const byte OriginMarkerAbsent = 0x00;

        /// <summary>
        /// Day 0 of a Delphi <c>TDate</c>. <see cref="DateDays"/> counts days from here,
        /// it is not a Unix timestamp.
        /// </summary>
        public static readonly DateTime DelphiDateEpoch = new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

        private static readonly Encoding Gbk;

        static YanshenBigBagRecord()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // Writing a name that is not representable in GBK is a caller bug and must be
            // reported; reading one is player data and must never throw.
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ReplacementFallback);
        }

        /// <summary>
        /// <c>0x00</c> — item instance serial. Stored on disk as an unsigned 32-bit
        /// value and held here as <see cref="int"/> to match
        /// <see cref="SystemModule.TUserItem.MakeIndex"/>; the conversion is
        /// bit-preserving in both directions.
        /// </summary>
        public int MakeIndex;

        /// <summary><c>0x04</c> — StdItem index.</summary>
        public ushort WIndex;

        /// <summary><c>0x06</c> — current durability.</summary>
        public ushort Dura;

        /// <summary><c>0x08</c> — maximum durability.</summary>
        public ushort DuraMax;

        /// <summary><c>0x0A</c> — the item's 14 <c>btValue</c> bytes.</summary>
        public byte[] BtValue = new byte[BtValueSize];

        /// <summary>
        /// <c>0x18</c> — four bytes, zero in all 1399 sampled records. Unpacked
        /// <c>.text</c> has no write to record+0x18 in the bags range; new records
        /// leave it 0. Carried through so an unexpected non-zero would survive.
        /// </summary>
        public byte[] Reserved18 = new byte[Reserved18Size];

        /// <summary>
        /// <c>0x1C</c> — acquisition date as days since <see cref="DelphiDateEpoch"/>.
        /// Dump has 0 hits for <c>COleDateTime</c> / <c>SystemTimeToVariantTime</c> /
        /// 25569 / 109205, so the epoch is the Delphi TDate / OLE DATE convention,
        /// not a conversion site. Zero means the record carries no date.
        /// </summary>
        public ushort DateDays;

        /// <summary><c>0x1E</c> — acquisition minute (0..59 in every sample).</summary>
        public byte Minute;

        /// <summary><c>0x1F</c> — acquisition hour (0..23 in every sample).</summary>
        public byte Hour;

        /// <summary>
        /// <c>0x20</c> — 12 raw bytes of the map's display name (mapinfo column 2),
        /// GBK, NUL-padded. Held raw so padding survives a round trip; use
        /// <see cref="MapTitle"/> for the decoded text.
        /// </summary>
        public byte[] MapTitleBytes = new byte[MapTitleSize];

        /// <summary>
        /// <c>0x2C</c> — unidentified 32-bit value. Sampled values are 16-byte
        /// aligned and constant per (file, map), 0 when the record has no map.
        /// Unpacked <c>.text</c> has no write-site; new records leave it 0 and
        /// loaded records must keep the original bits.
        /// </summary>
        public uint Dword2C;

        /// <summary><c>0x30</c> — declared byte length of the source name (0..14).</summary>
        public byte SourceNameLength;

        /// <summary>
        /// The source name's 14 raw bytes in logical order: <c>0x31</c>..<c>0x37</c>
        /// followed by <c>0x3C</c>..<c>0x42</c>, with the <c>0x38</c> hole skipped.
        /// Either a monster name or, when <see cref="SourceKind"/> is
        /// <see cref="SourceKindSelf"/>, the owner's own character name.
        /// </summary>
        public byte[] SourceNameBytes = new byte[SourceNameSize];

        /// <summary>
        /// <c>0x38</c> — four-byte hole in the source name. Zero in all 1399
        /// samples; unpacked <c>.text</c> has no write to record+0x38. New records
        /// leave it 0. Carried through verbatim.
        /// </summary>
        public byte[] Hole38 = new byte[Hole38Size];

        /// <summary>
        /// <c>0x43</c> — GBK byte length of <c>Envirnoment.sMapName</c> (mapinfo
        /// column 1, e.g. <c>D717~1</c>), not of the display name at <c>0x20</c>.
        /// 21/21 production maps match. The code string itself is never stored.
        /// </summary>
        public byte MapCodeLength;

        /// <summary><c>0x44</c> — declared byte length of the character name (0..14).</summary>
        public byte CharNameLength;

        /// <summary>
        /// <c>0x45</c> — 14 raw bytes naming the character who <b>first</b> obtained the
        /// item, which is not necessarily the file's owner: two sampled records name a
        /// different character because the item changed hands.
        /// </summary>
        public byte[] CharNameBytes = new byte[CharNameSize];

        /// <summary>
        /// <c>0x53</c> — second copy of <see cref="MapCodeLength"/>, written only while
        /// the character-name block is populated. See <see cref="DeriveMapCodeLengthCopy"/>.
        /// </summary>
        public byte MapCodeLengthCopy;

        /// <summary>
        /// <c>0x54</c> — <see cref="SourceKindMonster"/> or <see cref="SourceKindSelf"/>.
        /// </summary>
        public byte SourceKind;

        /// <summary>
        /// <c>0x55</c> — <see cref="OriginMarkerPresent"/> or <see cref="OriginMarkerAbsent"/>.
        /// </summary>
        public byte OriginMarker;

        /// <summary>
        /// <c>0x56</c> — 122 trailing bytes, zero except for three sampled records that
        /// carry <c>0x01</c> at record offset <c>0xB8</c>. Carried through verbatim.
        /// </summary>
        public byte[] Tail = new byte[TailSize];

        /// <summary>
        /// Map display name decoded from <see cref="MapTitleBytes"/>, up to the first NUL.
        /// </summary>
        public string MapTitle => DecodeGbk(MapTitleBytes, MeasureNulTerminated(MapTitleBytes));

        /// <summary>Source name decoded from the declared length.</summary>
        public string SourceName => DecodeGbk(SourceNameBytes, ClampLength(SourceNameLength, SourceNameBytes));

        /// <summary>First owner's character name decoded from the declared length.</summary>
        public string CharName => DecodeGbk(CharNameBytes, ClampLength(CharNameLength, CharNameBytes));

        /// <summary>
        /// Acquisition instant, or <c>null</c> when <see cref="DateDays"/> is 0. Seconds
        /// are not stored by the format.
        /// </summary>
        public DateTime? AcquiredAt => DateDays == 0
            ? (DateTime?)null
            : (DateTime?)DelphiDateEpoch.AddDays(DateDays).AddHours(Hour).AddMinutes(Minute);

        /// <summary>
        /// The value <c>0x53</c> must hold: <paramref name="mapCodeLength"/> while the
        /// character-name block is populated, 0 otherwise. Across the samples this holds
        /// without exception — 642 records have both non-zero and equal, and the 757
        /// with an empty character name all have <c>0x53 == 0</c> while <c>0x43</c> is
        /// still 1, 3, 4, 5 or 7 in 657 of them. Copying <c>0x43</c> unconditionally
        /// would corrupt those 657.
        /// </summary>
        public static byte DeriveMapCodeLengthCopy(byte mapCodeLength, byte charNameLength)
            => charNameLength > 0 ? mapCodeLength : (byte)0;

        /// <summary>
        /// Recompute <see cref="MapCodeLengthCopy"/> from the current
        /// <see cref="MapCodeLength"/> and <see cref="CharNameLength"/>. A writer that
        /// builds a new record should call this last; a record parsed from disk must not
        /// need it.
        /// </summary>
        public void ApplyMapCodeLengthCopyRule()
            => MapCodeLengthCopy = DeriveMapCodeLengthCopy(MapCodeLength, CharNameLength);

        /// <summary>Whether <see cref="MapCodeLengthCopy"/> already satisfies the rule.</summary>
        public bool MatchesMapCodeLengthCopyRule()
            => MapCodeLengthCopy == DeriveMapCodeLengthCopy(MapCodeLength, CharNameLength);

        public bool TrySetMapTitle(string value, out string error)
            => TryWriteFixedBlock(value, MapTitleBytes, MapTitleSize, "map title", out _, out error);

        public bool TrySetSourceName(string value, out string error)
        {
            if (!TryWriteFixedBlock(value, SourceNameBytes, SourceNameSize, "source name", out var length, out error))
                return false;
            SourceNameLength = (byte)length;
            return true;
        }

        public bool TrySetCharName(string value, out string error)
        {
            if (!TryWriteFixedBlock(value, CharNameBytes, CharNameSize, "character name", out var length, out error))
                return false;
            CharNameLength = (byte)length;
            return true;
        }

        /// <summary>
        /// Store the GBK byte length of mapinfo column 1 (<c>Envirnoment.sMapName</c>).
        /// Passing the display name (<c>sMapDesc</c>) produces the wrong <c>0x43</c>
        /// (沙巴克藏宝阁 is 7 as <c>F002~01</c>, 12 as the title).
        /// </summary>
        public bool TrySetMapCodeLengthFromMapCode(string mapCode, out string error)
        {
            byte[] bytes;
            try
            {
                bytes = Gbk.GetBytes(mapCode ?? string.Empty);
            }
            catch (EncoderFallbackException ex)
            {
                error = $"extra-bag map code is not representable in GBK: {ex.Message}";
                return false;
            }

            if (bytes.Length > byte.MaxValue)
            {
                error = $"extra-bag map code is {bytes.Length} GBK bytes, length field is one byte";
                return false;
            }

            MapCodeLength = (byte)bytes.Length;
            ApplyMapCodeLengthCopyRule();
            error = null;
            return true;
        }

        /// <summary>
        /// Set the acquisition instant. Seconds are discarded because the format has no
        /// field for them.
        /// </summary>
        public bool TrySetAcquiredAt(DateTime value, out string error)
        {
            var days = (value.Date - DelphiDateEpoch).Days;
            if (days < 0 || days > ushort.MaxValue)
            {
                error = $"{value:yyyy-MM-dd} is outside the Delphi TDate range this field can hold";
                return false;
            }

            DateDays = (ushort)days;
            Hour = (byte)value.Hour;
            Minute = (byte)value.Minute;
            error = null;
            return true;
        }

        /// <summary>Clear the acquisition instant, as the 100 sampled records with no provenance have it.</summary>
        public void ClearAcquiredAt()
        {
            DateDays = 0;
            Hour = 0;
            Minute = 0;
        }

        public static bool TryParse(ReadOnlySpan<byte> raw, out YanshenBigBagRecord parsed, out string error)
        {
            parsed = null;
            if (raw.Length != RecordSize)
            {
                error = $"extra-bag record must be exactly {RecordSize} bytes, got {raw.Length}";
                return false;
            }

            var result = new YanshenBigBagRecord
            {
                MakeIndex = BinaryPrimitives.ReadInt32LittleEndian(raw.Slice(MakeIndexOffset, sizeof(int))),
                WIndex = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(WIndexOffset, sizeof(ushort))),
                Dura = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(DuraOffset, sizeof(ushort))),
                DuraMax = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(DuraMaxOffset, sizeof(ushort))),
                DateDays = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(DateDaysOffset, sizeof(ushort))),
                Minute = raw[MinuteOffset],
                Hour = raw[HourOffset],
                Dword2C = BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(Dword2COffset, sizeof(uint))),
                SourceNameLength = raw[SourceNameLengthOffset],
                MapCodeLength = raw[MapCodeLengthOffset],
                CharNameLength = raw[CharNameLengthOffset],
                MapCodeLengthCopy = raw[MapCodeLengthCopyOffset],
                SourceKind = raw[SourceKindOffset],
                OriginMarker = raw[OriginMarkerOffset],
            };

            raw.Slice(BtValueOffset, BtValueSize).CopyTo(result.BtValue);
            raw.Slice(Reserved18Offset, Reserved18Size).CopyTo(result.Reserved18);
            raw.Slice(MapTitleOffset, MapTitleSize).CopyTo(result.MapTitleBytes);
            raw.Slice(Hole38Offset, Hole38Size).CopyTo(result.Hole38);
            raw.Slice(CharNameOffset, CharNameSize).CopyTo(result.CharNameBytes);
            raw.Slice(TailOffset, TailSize).CopyTo(result.Tail);

            // The source name straddles the 0x38 hole: 7 bytes, skip 4, 7 more.
            raw.Slice(SourceNameHeadOffset, SourceNameHeadSize)
                .CopyTo(result.SourceNameBytes.AsSpan(0, SourceNameHeadSize));
            raw.Slice(SourceNameTailOffset, SourceNameTailSize)
                .CopyTo(result.SourceNameBytes.AsSpan(SourceNameHeadSize, SourceNameTailSize));

            parsed = result;
            error = null;
            return true;
        }

        public bool TryWrite(Span<byte> destination, out string error)
        {
            if (destination.Length < RecordSize)
            {
                error = $"extra-bag record needs {RecordSize} bytes of space, got {destination.Length}";
                return false;
            }

            if (!TryValidateBlock(BtValue, BtValueSize, nameof(BtValue), out error)
                || !TryValidateBlock(Reserved18, Reserved18Size, nameof(Reserved18), out error)
                || !TryValidateBlock(MapTitleBytes, MapTitleSize, nameof(MapTitleBytes), out error)
                || !TryValidateBlock(SourceNameBytes, SourceNameSize, nameof(SourceNameBytes), out error)
                || !TryValidateBlock(Hole38, Hole38Size, nameof(Hole38), out error)
                || !TryValidateBlock(CharNameBytes, CharNameSize, nameof(CharNameBytes), out error)
                || !TryValidateBlock(Tail, TailSize, nameof(Tail), out error))
                return false;

            var target = destination.Slice(0, RecordSize);
            BinaryPrimitives.WriteInt32LittleEndian(target.Slice(MakeIndexOffset, sizeof(int)), MakeIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(WIndexOffset, sizeof(ushort)), WIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(DuraOffset, sizeof(ushort)), Dura);
            BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(DuraMaxOffset, sizeof(ushort)), DuraMax);
            BtValue.CopyTo(target.Slice(BtValueOffset, BtValueSize));
            Reserved18.CopyTo(target.Slice(Reserved18Offset, Reserved18Size));
            BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(DateDaysOffset, sizeof(ushort)), DateDays);
            target[MinuteOffset] = Minute;
            target[HourOffset] = Hour;
            MapTitleBytes.CopyTo(target.Slice(MapTitleOffset, MapTitleSize));
            BinaryPrimitives.WriteUInt32LittleEndian(target.Slice(Dword2COffset, sizeof(uint)), Dword2C);
            target[SourceNameLengthOffset] = SourceNameLength;

            SourceNameBytes.AsSpan(0, SourceNameHeadSize)
                .CopyTo(target.Slice(SourceNameHeadOffset, SourceNameHeadSize));
            Hole38.CopyTo(target.Slice(Hole38Offset, Hole38Size));
            SourceNameBytes.AsSpan(SourceNameHeadSize, SourceNameTailSize)
                .CopyTo(target.Slice(SourceNameTailOffset, SourceNameTailSize));

            target[MapCodeLengthOffset] = MapCodeLength;
            target[CharNameLengthOffset] = CharNameLength;
            CharNameBytes.CopyTo(target.Slice(CharNameOffset, CharNameSize));
            target[MapCodeLengthCopyOffset] = MapCodeLengthCopy;
            target[SourceKindOffset] = SourceKind;
            target[OriginMarkerOffset] = OriginMarker;
            Tail.CopyTo(target.Slice(TailOffset, TailSize));

            error = null;
            return true;
        }

        public byte[] ToBytes()
        {
            var buffer = new byte[RecordSize];
            if (!TryWrite(buffer, out var error))
                throw new InvalidOperationException(error);
            return buffer;
        }

        /// <summary>Deep copy, so a caller can stage edits without touching the loaded record.</summary>
        public YanshenBigBagRecord Clone()
        {
            var copy = (YanshenBigBagRecord)MemberwiseClone();
            copy.BtValue = (byte[])BtValue.Clone();
            copy.Reserved18 = (byte[])Reserved18.Clone();
            copy.MapTitleBytes = (byte[])MapTitleBytes.Clone();
            copy.SourceNameBytes = (byte[])SourceNameBytes.Clone();
            copy.Hole38 = (byte[])Hole38.Clone();
            copy.CharNameBytes = (byte[])CharNameBytes.Clone();
            copy.Tail = (byte[])Tail.Clone();
            return copy;
        }

        private static bool TryWriteFixedBlock(string value, byte[] block, int size, string what,
            out int length, out string error)
        {
            length = 0;
            if (!TryValidateBlock(block, size, what, out error))
                return false;

            byte[] bytes;
            try
            {
                bytes = Gbk.GetBytes(value ?? string.Empty);
            }
            catch (EncoderFallbackException ex)
            {
                error = $"extra-bag {what} is not representable in GBK: {ex.Message}";
                return false;
            }

            if (bytes.Length > size)
            {
                error = $"extra-bag {what} is {bytes.Length} GBK bytes, the field holds {size}";
                return false;
            }

            Array.Clear(block, 0, size);
            bytes.CopyTo(block, 0);
            length = bytes.Length;
            error = null;
            return true;
        }

        private static bool TryValidateBlock(byte[] block, int size, string name, out string error)
        {
            if (block == null || block.Length != size)
            {
                error = $"extra-bag record field {name} must be a {size}-byte array";
                return false;
            }

            error = null;
            return true;
        }

        private static int MeasureNulTerminated(byte[] block)
        {
            if (block == null) return 0;
            var end = Array.IndexOf(block, (byte)0);
            return end < 0 ? block.Length : end;
        }

        private static int ClampLength(byte declared, byte[] block)
        {
            if (block == null) return 0;
            return declared > block.Length ? block.Length : declared;
        }

        private static string DecodeGbk(byte[] block, int length)
            => block == null || length <= 0 ? string.Empty : Gbk.GetString(block, 0, length);
    }
}
