using ProtoBuf;
using System;
using System.IO;
using System.Text;

namespace SystemModule
{
    /// <summary>
    /// TUserItem - Per-item instance data for a player's inventory/equipment/storage item.
    ///
    /// Binary compatibility: This revision extends the binary packet format with yanshen plugin
    /// fields. Old binary packets (without yanshen data) are NOT compatible with this format.
    ///
    /// Migration approach:
    ///   - Protobuf (database save/load): New fields use ProtoMember numbers 6-35. Old saved data
    ///     will deserialize with default values (0/null) for new fields. Full backward compatible.
    ///   - JSON (sell-off items): System.Text.Json will skip missing properties on old data,
    ///     defaulting new fields to 0/null. Full backward compatible.
    ///   - Binary ReadPacket/WritePacket (network): Format is extended. Old clients sending
    ///     short packets will cause ReadPacket to fail. Both client and server must update
    ///     simultaneously. For gradual rollout, use a packet version flag before reading new fields.
    /// </summary>
    [ProtoContract]
    public class TUserItem : Packets
    {
        // ======================== Core Fields (original) ========================

        /// <summary>Unique item serial number (server-assigned)</summary>
        [ProtoMember(1)]
        public int MakeIndex;

        /// <summary>
        /// Session-local identifier exposed to the game client. The original M2 stores this at
        /// item+0x18 and assigns it from the owning player's counter; it is deliberately excluded
        /// from every persisted and binary item record.
        /// </summary>
        [ProtoIgnore]
        public int ClientItemID;

        // Runtime flag written by native type-2000 item events. It permits
        // forced map-exit drops even when the standard definition is protected.
        [ProtoIgnore]
        public bool NativeMapDropAllowed;

        /// <summary>
        /// 战神 <c>item+0xD8</c> — the "gift" (赠品/赠送) runtime byte.  The item factory
        /// zeroes it (<c>sub_7837D8</c> @0x7837EE <c>mov byte [ebx+0xD8],0</c>) and only
        /// specific grant paths set it to 1 (@0x6C8611, @0x709498, @0x7094A4, @0x67D236).
        /// It is read by the DESTROY branch of all three drop paths — manual drop
        /// <c>sub_73CC98</c> @0x73CD44, death bag-drop <c>sub_740078</c> @0x740161 and
        /// death equip-drop <c>sub_73FC70</c> @0x73FDD0 — each doing
        /// <c>cmp byte [item+0xD8],0; je</c>: a gift item is FREED with a chat notice
        /// instead of being placed on the map, which is what stops a gift from being
        /// laundered to another character via the floor.
        ///
        /// CORRECTION: the old claim that native never persists this byte is wrong.
        /// <c>item+0xD8</c> is INSIDE the saved window — the record is
        /// <c>item+0x20 .. item+0xEF</c> (0xD0 = 208 bytes, LOAD @0x74DB3A/0x74DB3D/0x74DB42,
        /// SAVE @0x6B170F/0x6B1712/0x6B1717) — so the gift byte lands at record offset
        /// 0xB8 and survives every save/load. What actually happens today is that the
        /// persisted value is decoded into <see cref="Bind"/> (see its remark) while this
        /// runtime flag stays false, so a gift item stops being a gift after one relog.
        /// The only reader is <c>NativeItemDropDestroy</c>; nothing in production ever
        /// sets this field.
        /// </summary>
        /// <summary>
        /// 战神 <c>item+0xFC</c> — runtime out-of-bounds class flag written by
        /// <c>sub_74DAE4</c> @0x74DC58/0x74DCB5/0x74DCF0/… (<c>mov [ebx+0xFC],al</c>).
        /// Outside the 208-byte persist window; excluded from every codec.
        /// </summary>
        [ProtoIgnore]
        public byte NativeClassFc;

        /// <summary>native 赠品(gift) byte; runtime-only, excluded from codec (protocol unchanged).</summary>
        [ProtoIgnore]
        public byte NativeGiftItem;

        /// <summary>
        /// 战神 <c>item+0x100 .. item+0x103</c> — a shared runtime dword. The first
        /// three bytes are also the inlay/jewel attribute triple.
        /// <c>sub_78C5EC</c> copies the last three bytes of the 9-byte row at
        /// <c>[0x7DCBDC]</c> into them (@0x78C643 <c>88 86 00 01 00 00</c>
        /// <c>mov [esi+0x100],al</c>, @0x78C64C <c>mov [esi+0x101],al</c>, @0x78C655
        /// <c>mov [esi+0x102],al</c>). For StdMode 96, constructor
        /// <c>sub_78BCD8</c> copies dword <c>[std+0x4C]</c> to
        /// <c>[item+0x100]</c>, and <c>sub_78BCBC</c> reads it as a signed dword:
        /// <c>mov eax,0x64; call Random; cmp eax,[ebx+0x100]; setl al</c>.
        ///
        /// They are runtime-only: the persisted record is <c>item+0x20 .. item+0xEF</c>
        /// (LOAD <c>sub_74DAE4</c> @0x74DB3A <c>lea edi,[ebx+0x20]</c> / @0x74DB3D
        /// <c>mov ecx,0x34</c> / @0x74DB42 <c>rep movsd</c>; SAVE @0x6B170F..0x6B1717
        /// copies the same 0xD0-byte window in reverse), so these four bytes sit
        /// beyond its end and must never be folded into <see cref="NativeRecord"/>.
        /// </summary>
        [ProtoIgnore]
        public byte NativeItemPlus100;

        /// <inheritdoc cref="NativeItemPlus100"/>
        [ProtoIgnore]
        public byte NativeItemPlus101;

        /// <inheritdoc cref="NativeItemPlus100"/>
        [ProtoIgnore]
        public byte NativeItemPlus102;

        /// <summary>
        /// 战神 <c>item+0x103</c> — high byte of the transient word at
        /// <c>item+0x102</c> and of the special-drop signed threshold dword at
        /// <c>item+0x100</c>. <c>sub_75EE04</c> clears +0x102/+0x103 together at
        /// 0x75EE12 before rebuilding the equipped-item aggregate. It is outside
        /// the 208-byte persisted item record and is not part of any wire codec.
        /// </summary>
        [ProtoIgnore]
        public byte NativeItemPlus103;

        /// <summary>
        /// 战神 <c>item+0x104</c> — runtime equipment-class bitmap rebuilt for
        /// equipped items with positive durability by <c>sub_75EE04</c>. It is
        /// outside the 208-byte persisted item record.
        /// </summary>
        [ProtoIgnore]
        public byte NativeClass104;

        /// <summary>Item definition index (lookup into StdItemList)</summary>
        [ProtoMember(2)]
        public ushort wIndex;

        /// <summary>Current durability</summary>
        [ProtoMember(3)]
        public ushort Dura;

        /// <summary>Maximum durability</summary>
        [ProtoMember(4)]
        public ushort DuraMax;

        /// <summary>
        /// Upgrade/bonus values (14 bytes).
        /// Layout varies by item type (see GoodItem.GetItemAddValue):
        ///   Weapon: [0]=DC+, [1]=MC+, [2]=SC+, [3]=AC+, [4]=MAC+, [5]=AC2+,
        ///           [6]=MAC2+, [7]=Source, [8]=flag, [9]=native refine status,
        ///           [10]=reserved, [13]=customName
        ///   Armor:  [0]=AC+, [1]=MAC+, [2]=DC+, [3]=MC+, [4]=SC+
        ///   Acc:    [0]=AC+, [1]=MAC+, [2]=DC+, [3]=MC+, [4]=SC+, [5]=Need, [6]=NeedLevel
        /// </summary>
        [ProtoMember(5, OverwriteList = true)]
        public byte[] btValue;

        // ======================== Yanshen Plugin: Element Values (ys1-ys17) ========================

        /// <summary>Element value 1 (max 2.1 billion)</summary>
        [ProtoMember(6)]
        public int ys1;

        /// <summary>Element value 2 (max 255)</summary>
        [ProtoMember(7)]
        public byte ys2;

        /// <summary>Element value 3 (max 255)</summary>
        [ProtoMember(8)]
        public byte ys3;

        /// <summary>Element value 4 (max 255)</summary>
        [ProtoMember(9)]
        public byte ys4;

        /// <summary>Element value 5 (max 255)</summary>
        [ProtoMember(10)]
        public byte ys5;

        /// <summary>Element value 6 (max 255)</summary>
        [ProtoMember(11)]
        public byte ys6;

        /// <summary>Element value 7 (max 255)</summary>
        [ProtoMember(12)]
        public byte ys7;

        /// <summary>Element value 8 (max 255)</summary>
        [ProtoMember(13)]
        public byte ys8;

        /// <summary>Element value 9 (max 255)</summary>
        [ProtoMember(14)]
        public byte ys9;

        /// <summary>Element value 10 (max 255)</summary>
        [ProtoMember(15)]
        public byte ys10;

        /// <summary>Element value 11 (max 255)</summary>
        [ProtoMember(16)]
        public byte ys11;

        /// <summary>Element value 12 (max 255)</summary>
        [ProtoMember(17)]
        public byte ys12;

        /// <summary>Element value 13 (max 255)</summary>
        [ProtoMember(18)]
        public byte ys13;

        /// <summary>Element value 14 (max 255)</summary>
        [ProtoMember(19)]
        public byte ys14;

        /// <summary>Element value 15 (max 255)</summary>
        [ProtoMember(20)]
        public byte ys15;

        /// <summary>Element value 16 (max 255)</summary>
        [ProtoMember(21)]
        public byte ys16;

        /// <summary>Element value 17 (max 255)</summary>
        [ProtoMember(22)]
        public byte ys17;

        // ======================== Yanshen Plugin: Extreme/JP Values (jp1-jp6) ========================

        /// <summary>Extreme/jp value 1 (max 255)</summary>
        [ProtoMember(23)]
        public byte jp1;

        /// <summary>Extreme/jp value 2 (max 255)</summary>
        [ProtoMember(24)]
        public byte jp2;

        /// <summary>Extreme/jp value 3 (max 255)</summary>
        [ProtoMember(25)]
        public byte jp3;

        /// <summary>Extreme/jp value 4 (max 255)</summary>
        [ProtoMember(26)]
        public byte jp4;

        /// <summary>Extreme/jp value 5 (max 255)</summary>
        [ProtoMember(27)]
        public byte jp5;

        /// <summary>Extreme/jp value 6 (max 255)</summary>
        [ProtoMember(28)]
        public byte jp6;

        // ======================== Yanshen Plugin: Description / Source Info ========================

        /// <summary>Item source player name (up to 8 Chinese chars / 16 ASCII chars)</summary>
        [ProtoMember(29)]
        public string pname;

        /// <summary>Item description line 1 (up to 8 Chinese chars / 16 ASCII chars)</summary>
        [ProtoMember(30)]
        public string desc1;

        /// <summary>Item description line 2 (up to 8 Chinese chars / 16 ASCII chars)</summary>
        [ProtoMember(31)]
        public string desc2;

        // ======================== Yanshen Plugin: Bind Flag ========================

        /// <summary>
        /// Bind flag: 0 = unbound, greater than 0 = bound.
        ///
        /// MISNAMED, DELIBERATELY NOT MOVED. Every persisted codec maps this field to
        /// record offset <c>0xB8</c> (<c>NativeHumanDataCodec</c>, <c>NativeHeroRuntimeCodec</c>,
        /// <c>NativeMerchantGoodsCodec</c>, <c>NativeMailAttachmentCodec</c>,
        /// <c>LegacyUserItem208Codec</c>), and record <c>0xB8</c> is <c>item+0xD8</c> — the
        /// native 赠品 (gift) byte, not a bind flag. See <see cref="NativeGiftItem"/> for the
        /// VAs.
        ///
        /// The genuine native bind/lock word is <c>word[item+0x34]</c> = record <c>0x14..0x15</c>
        /// = <c>btValue[10..11]</c>: <c>sub_784710</c> @0x784710 <c>66 8b 40 34</c>
        /// (<c>mov ax,word [eax+0x34]</c>) and <c>sub_784718</c> @0x784718 <c>66 89 50 34</c>
        /// (<c>mov word [eax+0x34],dx</c>). It is written by the item factory for the
        /// bind-on-create class (<c>sub_783788</c> @0x7837C4 <c>test byte [std+3],8</c> ->
        /// @0x7837CA <c>mov dx,1</c> -> <c>call sub_784718</c>) and by the acquisition
        /// stamper <c>sub_7842F8</c>. That word is already modelled correctly as
        /// <c>btValue[10..11]</c> (see <c>NativeItemAcquisitionStamp</c>) and round-trips
        /// through every codec, so nothing is lost today.
        ///
        /// Renaming the offset would re-interpret every already-written row and every
        /// existing .Sav in both directions at once. The migration plan (read the gift
        /// byte into <see cref="NativeGiftItem"/>, fold bind into <c>btValue[10..11]</c>,
        /// backfill in one pass) is in staging/m_itemdb_20260813.md. Do not change the
        /// offset without it.
        /// </summary>
        [ProtoMember(32)]
        public byte Bind;

        // ======================== Yanshen Plugin: Description Tags ========================

        /// <summary>Source timestamp (when the item was obtained)</summary>
        [ProtoMember(33)]
        public string sourceTime;

        /// <summary>Name of the killer / creator</summary>
        [ProtoMember(34)]
        public string killerName;

        /// <summary>Map name where the item was obtained</summary>
        [ProtoMember(35)]
        public string mapName;

        /// <summary>
        /// Native weapon-upgrade flags at record offset <c>0x27</c> = <c>item+0x47</c>.
        /// Bit <c>0x80</c> = 不破碎 (<c>or byte [esi+0x47],0x80</c> @0x6CA0F3), bit
        /// <c>0x40</c> = 必成功 (<c>or byte [esi+0x47],0x40</c> @0x6CA10D). Read at
        /// @0x6D7A93 <c>mov al,[ebx+0x47]</c> / <c>and al,0x80</c> / <c>cmp al,0x80</c>,
        /// cleared with <c>mov byte [ebx+0x47],0</c> at @0x6D7AE5 and @0x6D7B07.
        /// Server state; intentionally not part of the mobile item packet.
        ///
        /// The offset is right, but the byte is SHARED. Record <c>0x20..0x2B</c> holds the
        /// 眼神 provenance map title as 12 GBK bytes, so <c>0x26..0x27</c> is the 4th
        /// character and <c>0x27</c> is its trail byte. Trail bytes always have bit 0x80
        /// set, so in production every item picked up on a map whose name is four or more
        /// characters reads back as 不破碎 for free (golden corpus: 287 of 1363 items).
        /// That collision is native's, not ours — but it means the low six bits are player
        /// data: only ever OR bits in or clear the whole byte, never assign a value.
        /// </summary>
        [ProtoMember(36)]
        public byte UpgradeFlags;

        /// <summary>
        /// Original 208-byte Gs1 item record. The server patches mapped fields in this copy and
        /// preserves every unknown extension byte instead of inventing a layout for it.
        /// </summary>
        [ProtoMember(37, OverwriteList = true)]
        public byte[] NativeRecord;

        // ======================== Constants ========================

        /// <summary>Fixed byte length per string field in binary packets (16 bytes = 8 Chinese or 16 ASCII in GBK)</summary>
        private const int STRING_FIELD_LEN = 16;

        // ======================== Constructors ========================

        public TUserItem()
        {
            btValue = new byte[14];
            pname = string.Empty;
            desc1 = string.Empty;
            desc2 = string.Empty;
            sourceTime = string.Empty;
            killerName = string.Empty;
            mapName = string.Empty;
        }

        internal bool IsTransportPlaceholder()
        {
            return MakeIndex == 0 && wIndex == 0 && Dura == 0 && DuraMax == 0
                && UpgradeFlags == 0 && ys1 == 0 && ys2 == 0 && ys3 == 0
                && ys4 == 0 && ys5 == 0 && ys6 == 0 && ys7 == 0 && ys8 == 0
                && ys9 == 0 && ys10 == 0 && ys11 == 0 && ys12 == 0
                && ys13 == 0 && ys14 == 0 && ys15 == 0 && ys16 == 0 && ys17 == 0
                && jp1 == 0 && jp2 == 0 && jp3 == 0 && jp4 == 0 && jp5 == 0 && jp6 == 0
                && Bind == 0 && IsEmptyBytes(btValue) && IsEmptyBytes(NativeRecord)
                && string.IsNullOrEmpty(pname) && string.IsNullOrEmpty(desc1)
                && string.IsNullOrEmpty(desc2) && string.IsNullOrEmpty(sourceTime)
                && string.IsNullOrEmpty(killerName) && string.IsNullOrEmpty(mapName);
        }

        private static bool IsEmptyBytes(byte[] values)
        {
            if (values == null) return true;
            for (var i = 0; i < values.Length; i++)
                if (values[i] != 0) return false;
            return true;
        }

        /// <summary>
        /// Copy constructor. Performs a deep copy of btValue and all yanshen fields.
        /// </summary>
        public TUserItem(TUserItem userItem)
        {
            if (userItem == null)
                throw new ArgumentNullException(nameof(userItem));

            this.MakeIndex = userItem.MakeIndex;
            this.ClientItemID = userItem.ClientItemID;
            this.NativeMapDropAllowed = userItem.NativeMapDropAllowed;
            this.NativeGiftItem = userItem.NativeGiftItem;
            this.NativeClassFc = userItem.NativeClassFc;
            this.NativeItemPlus100 = userItem.NativeItemPlus100;
            this.NativeItemPlus101 = userItem.NativeItemPlus101;
            this.NativeItemPlus102 = userItem.NativeItemPlus102;
            this.NativeItemPlus103 = userItem.NativeItemPlus103;
            this.NativeClass104 = userItem.NativeClass104;
            this.wIndex = userItem.wIndex;
            this.Dura = userItem.Dura;
            this.DuraMax = userItem.DuraMax;

            // Deep copy btValue to avoid shared-mutation bugs
            if (userItem.btValue != null)
            {
                this.btValue = new byte[userItem.btValue.Length];
                Array.Copy(userItem.btValue, this.btValue, userItem.btValue.Length);
            }
            else
            {
                this.btValue = new byte[14];
            }

            // Element values
            this.ys1 = userItem.ys1;
            this.ys2 = userItem.ys2;
            this.ys3 = userItem.ys3;
            this.ys4 = userItem.ys4;
            this.ys5 = userItem.ys5;
            this.ys6 = userItem.ys6;
            this.ys7 = userItem.ys7;
            this.ys8 = userItem.ys8;
            this.ys9 = userItem.ys9;
            this.ys10 = userItem.ys10;
            this.ys11 = userItem.ys11;
            this.ys12 = userItem.ys12;
            this.ys13 = userItem.ys13;
            this.ys14 = userItem.ys14;
            this.ys15 = userItem.ys15;
            this.ys16 = userItem.ys16;
            this.ys17 = userItem.ys17;

            // Extreme/jp values
            this.jp1 = userItem.jp1;
            this.jp2 = userItem.jp2;
            this.jp3 = userItem.jp3;
            this.jp4 = userItem.jp4;
            this.jp5 = userItem.jp5;
            this.jp6 = userItem.jp6;

            // Description strings
            this.pname = userItem.pname ?? string.Empty;
            this.desc1 = userItem.desc1 ?? string.Empty;
            this.desc2 = userItem.desc2 ?? string.Empty;

            // Bind flag
            this.Bind = userItem.Bind;

            // Description tags
            this.sourceTime = userItem.sourceTime ?? string.Empty;
            this.killerName = userItem.killerName ?? string.Empty;
            this.mapName = userItem.mapName ?? string.Empty;
            this.UpgradeFlags = userItem.UpgradeFlags;
            this.NativeRecord = userItem.NativeRecord == null
                ? null
                : (byte[])userItem.NativeRecord.Clone();
        }

        // ======================== Binary Serialization (network packets) ========================

        /// <summary>
        /// Reads a TUserItem from a binary stream (network packet format).
        /// Format: MakeIndex(4) + wIndex(2) + Dura(2) + DuraMax(2) + btValue(14)
        ///         + ys1(4) + ys2-ys17(16) + jp1-jp6(6) + Bind(1)
        ///         + pname(16) + desc1(16) + desc2(16) + sourceTime(16) + killerName(16) + mapName(16)
        /// Total: 24 (core) + 123 (yanshen) = 147 bytes
        /// </summary>
        protected override void ReadPacket(BinaryReader reader)
        {
            this.MakeIndex = reader.ReadInt32();
            this.wIndex = reader.ReadUInt16();
            this.Dura = reader.ReadUInt16();
            this.DuraMax = reader.ReadUInt16();
            this.btValue = reader.ReadBytes(14);

            // Yanshen plugin fields
            this.ys1 = reader.ReadInt32();
            this.ys2 = reader.ReadByte();
            this.ys3 = reader.ReadByte();
            this.ys4 = reader.ReadByte();
            this.ys5 = reader.ReadByte();
            this.ys6 = reader.ReadByte();
            this.ys7 = reader.ReadByte();
            this.ys8 = reader.ReadByte();
            this.ys9 = reader.ReadByte();
            this.ys10 = reader.ReadByte();
            this.ys11 = reader.ReadByte();
            this.ys12 = reader.ReadByte();
            this.ys13 = reader.ReadByte();
            this.ys14 = reader.ReadByte();
            this.ys15 = reader.ReadByte();
            this.ys16 = reader.ReadByte();
            this.ys17 = reader.ReadByte();

            this.jp1 = reader.ReadByte();
            this.jp2 = reader.ReadByte();
            this.jp3 = reader.ReadByte();
            this.jp4 = reader.ReadByte();
            this.jp5 = reader.ReadByte();
            this.jp6 = reader.ReadByte();

            this.Bind = reader.ReadByte();

            this.pname = ReadFixedString(reader, STRING_FIELD_LEN);
            this.desc1 = ReadFixedString(reader, STRING_FIELD_LEN);
            this.desc2 = ReadFixedString(reader, STRING_FIELD_LEN);
            this.sourceTime = ReadFixedString(reader, STRING_FIELD_LEN);
            this.killerName = ReadFixedString(reader, STRING_FIELD_LEN);
            this.mapName = ReadFixedString(reader, STRING_FIELD_LEN);
        }

        /// <summary>
        /// Writes a TUserItem to a binary stream (network packet format).
        /// See ReadPacket for format layout.
        /// </summary>
        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(MakeIndex);
            writer.Write(wIndex);
            writer.Write(Dura);
            writer.Write(DuraMax);
            writer.Write(btValue ?? new byte[14]);

            // Yanshen plugin fields
            writer.Write(ys1);
            writer.Write(ys2);
            writer.Write(ys3);
            writer.Write(ys4);
            writer.Write(ys5);
            writer.Write(ys6);
            writer.Write(ys7);
            writer.Write(ys8);
            writer.Write(ys9);
            writer.Write(ys10);
            writer.Write(ys11);
            writer.Write(ys12);
            writer.Write(ys13);
            writer.Write(ys14);
            writer.Write(ys15);
            writer.Write(ys16);
            writer.Write(ys17);

            writer.Write(jp1);
            writer.Write(jp2);
            writer.Write(jp3);
            writer.Write(jp4);
            writer.Write(jp5);
            writer.Write(jp6);

            writer.Write(Bind);

            WriteFixedString(writer, pname, STRING_FIELD_LEN);
            WriteFixedString(writer, desc1, STRING_FIELD_LEN);
            WriteFixedString(writer, desc2, STRING_FIELD_LEN);
            WriteFixedString(writer, sourceTime, STRING_FIELD_LEN);
            WriteFixedString(writer, killerName, STRING_FIELD_LEN);
            WriteFixedString(writer, mapName, STRING_FIELD_LEN);
        }

        // ======================== Helpers ========================

        /// <summary>
        /// Reads a fixed-length GBK-encoded string from the reader.
        /// Reads exactly byteLen bytes, decodes with GBK, and trims trailing nulls.
        /// </summary>
        private static string ReadFixedString(BinaryReader reader, int byteLen)
        {
            byte[] raw = reader.ReadBytes(byteLen);
            // Find the null terminator (or end of buffer)
            int strEnd = 0;
            while (strEnd < raw.Length && raw[strEnd] != 0)
                strEnd++;
            if (strEnd == 0)
                return string.Empty;
            return HUtil32.GbkEncoding.GetString(raw, 0, strEnd);
        }

        /// <summary>
        /// Writes a fixed-length GBK-encoded string to the writer.
        /// Encodes with GBK, writes exactly byteLen bytes (zero-padded if string is shorter,
        /// truncated if longer).
        /// </summary>
        private static void WriteFixedString(BinaryWriter writer, string str, int byteLen)
        {
            byte[] buffer = new byte[byteLen];
            if (!string.IsNullOrEmpty(str))
            {
                byte[] encoded = HUtil32.GbkEncoding.GetBytes(str);
                int copyLen = Math.Min(encoded.Length, byteLen);
                Array.Copy(encoded, 0, buffer, 0, copyLen);
            }
            writer.Write(buffer);
        }
    }
}
