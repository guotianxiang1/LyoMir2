using System;
using System.Buffers.Binary;
using System.Text;

namespace SystemModule
{
    /// <summary>
    /// Maps <see cref="TUserItem"/> yanshen fields onto the native 208-byte persist
    /// blob copied by M2Server <c>0x6B170F lea esi,[eax+0x20] / 0x6B1712 mov ecx,0x34 /
    /// 0x6B1717 rep movsd</c>. Plugin dump base <c>0x10000000</c>.
    ///
    /// The C# ScriptData section type <c>0x79</c> is not a native section (host parser
    /// <c>0x6E4510 cmp eax,8 / 0x6E4513 ja 0x6E4856</c>). These bytes are the real
    /// destination; <c>0x79</c> is kept only as a dual-write overlay during migration.
    /// </summary>
    public static class YanshenNativeItemLayout
    {
        public const int RecordSize = 208;

        // blob offset = in-memory item offset - 0x20
        public const int Jp2Offset = 0x0A; // item+0x2A  10075C8D mov [ebp-0x18],0x2A
        public const int Jp1Offset = 0x0B; // item+0x2B  10075C84 mov [ebp-0x18],0x2B
        public const int Jp6Offset = 0x0C; // item+0x2C  10075CB1 mov [ebp-0x18],0x2C
        public const int Jp5Offset = 0x0D; // item+0x2D  10075CA8 mov [ebp-0x18],0x2D
        public const int Jp4Offset = 0x0E; // item+0x2E  10075C9F mov [ebp-0x18],0x2E
        public const int Jp3Offset = 0x0F; // item+0x2F  10075C96 mov [ebp-0x18],0x2F

        public const int DateDaysOffset = 0x1C; // item+0x3C  100586B2 mov [eax+0x3C],edx
        public const int MinuteOffset = 0x1E;
        public const int HourOffset = 0x1F;

        public const int Desc1Offset = 0x20; // item+0x40  10058626..1005863E four dwords
        // The NATIVE field here is 16 bytes (rec 0x20..0x2F), blind-copied as four
        // dwords by BOTH stampers — script 10058626/1005862E/10058636/1005863E and
        // drop 1008287F/10082885/1008288B/10082891, the last of which stores
        // item+0x4C == rec 0x2C.  (An older comment below claimed "no .text store of
        // that dword"; it is wrong, and 257 of the 1363 golden item records carry a
        // non-zero rec[0x2C..0x2F].)  MapTitleSize stays 12 because that is all the
        // C# projection can reconstruct: the source buffer is a raw heap window, so
        // the bytes past the map name's NUL are adjacent heap content, not text.
        public const int MapTitleSize = 12;
        public const int Dword2COffset = 0x2C;
        public const int StringFieldSize = 16;

        public const int Desc2Dword0Offset = 0x30; // item+0x50  10058652
        public const int Desc2Dword1Offset = 0x34; // item+0x54  1005865A
        public const int Hole38Offset = 0x38;      // skipped    1005865D add edx,4 / mov [eax+0x5C]
        public const int Desc2Dword2Offset = 0x3C; // item+0x5C  10058662
        public const int Desc2Dword3Offset = 0x40; // item+0x60  1005866A

        public const int PNameOffset = 0x44; // item+0x64  1005867E..10058696 four dwords
        public const int SourceKindOffset = 0x54; // item+0x74  100586A9 mov [eax+0x74],2
        public const int OriginMarkerOffset = 0x55; // item+0x75  100586A4 mov [eax+0x75],0xFF
        // item+0x74 is NOT re-derivable. A full capstone sweep of the plugin .text
        // for 1-byte accesses at disp 0x74 finds exactly two real item writers,
        // 100586A9 and 1005A4EF, and BOTH store the constant 2; there is no reader
        // at all. The drop stamper 10082868..100828EA writes item+0x40/+0x44/+0x48/
        // +0x4C/+0x50/+0x54/+0x5C/+0x60/+0x64/+0x68/+0x6C/+0x70/+0x75/+0x3C and
        // leaves +0x74 alone, which is why every one of the 301 monster-drop records
        // in the golden corpus reads 0 there — the factory's zero, never overwritten.
        // Who writes the 1 that 931 of the 1363 golden records carry is UNPROVEN
        // (0x1006FD7F stores a literal 1 to this+0x74 but its `this` is the script
        // tunnel's parser object: it returns the -888 "not enough fields" sentinel
        // and its only caller 0x100779D9 sits inside the 集成函数 dispatcher).
        // So the byte is carried, never computed.

        public const int Ys5Offset = 0x58; // item+0x78  10075D48 mov [ebp-0x14],0x78
        public const int Ys4Offset = 0x59; // item+0x79  10075D3F
        public const int Ys3Offset = 0x5A; // item+0x7A  10075D36
        public const int Ys2Offset = 0x5B; // item+0x7B  10075D2A / 10075D10
        public const int Ys1Offset = 0x5C; // item+0x7C  10075CF9 mov [eax+0x7C],esi
        public const int Ys6Offset = 0x60; // item+0x80  10075D51
        public const int Ys17Offset = 0x6B;

        public const byte SourceKindMonster = 0;
        public const byte SourceKindSelf = 1;
        public const byte SourceKindCustom = 2; // 100586A7 mov dl,2
        public const byte OriginMarkerPresent = 0xFF;
        public const byte OriginMarkerAbsent = 0;

        public static readonly DateTime DelphiDateEpoch =
            new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

        private static readonly Encoding Gbk;

        static YanshenNativeItemLayout()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ReplacementFallback);
        }

        /// <summary>
        /// Tail bytes the Delphi <c>rep movsd</c> already carries, so a validator
        /// must not treat them as unknown. Bind at <c>0xB8</c> is native and is
        /// not included here.
        /// </summary>
        public static bool IsMappedTailOffset(int offset)
        {
            if (offset >= DateDaysOffset && offset <= OriginMarkerOffset) return true;
            if (offset >= Ys5Offset && offset <= Ys17Offset) return true;
            return false;
        }

        public static void Unpack(TUserItem item)
        {
            if (item?.NativeRecord == null || item.NativeRecord.Length != RecordSize)
                return;
            Unpack(item, item.NativeRecord);
        }

        public static void Unpack(TUserItem item, ReadOnlySpan<byte> record)
        {
            if (item == null || record.Length < RecordSize) return;

            item.jp2 = record[Jp2Offset];
            item.jp1 = record[Jp1Offset];
            item.jp6 = record[Jp6Offset];
            item.jp5 = record[Jp5Offset];
            item.jp4 = record[Jp4Offset];
            item.jp3 = record[Jp3Offset];

            item.ys5 = record[Ys5Offset];
            item.ys4 = record[Ys4Offset];
            item.ys3 = record[Ys3Offset];
            item.ys2 = record[Ys2Offset];
            item.ys1 = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(Ys1Offset, 4));
            item.ys6 = record[Ys6Offset];
            item.ys7 = record[Ys6Offset + 1];
            item.ys8 = record[Ys6Offset + 2];
            item.ys9 = record[Ys6Offset + 3];
            item.ys10 = record[Ys6Offset + 4];
            item.ys11 = record[Ys6Offset + 5];
            item.ys12 = record[Ys6Offset + 6];
            item.ys13 = record[Ys6Offset + 7];
            item.ys14 = record[Ys6Offset + 8];
            item.ys15 = record[Ys6Offset + 9];
            item.ys16 = record[Ys6Offset + 10];
            item.ys17 = record[Ys17Offset];

            if (record[OriginMarkerOffset] != OriginMarkerPresent) return;

            item.sourceTime = FormatSourceTime(
                BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(DateDaysOffset, 2)),
                record[MinuteOffset],
                record[HourOffset]);
            item.mapName = ReadGbkZ(record.Slice(Desc1Offset, MapTitleSize));

            var kind = record[SourceKindOffset];
            if (kind == SourceKindCustom)
            {
                item.desc1 = ReadGbkZ(record.Slice(Desc1Offset, StringFieldSize));
                item.desc2 = ReadGbkZ(ReadDesc2(record));
                item.pname = ReadGbkZ(record.Slice(PNameOffset, StringFieldSize));
            }
            else
            {
                item.killerName = ReadStructuredSourceName(record);
                item.pname = ReadStructuredCharName(record);
            }
        }

        public static void Pack(TUserItem item)
        {
            if (item == null) return;
            if (item.NativeRecord == null || item.NativeRecord.Length != RecordSize)
                item.NativeRecord = new byte[RecordSize];
            Pack(item, item.NativeRecord);
        }

        public static void Pack(TUserItem item, Span<byte> destination)
        {
            if (item == null || destination.Length < RecordSize) return;

            destination[Jp2Offset] = item.jp2;
            destination[Jp1Offset] = item.jp1;
            destination[Jp6Offset] = item.jp6;
            destination[Jp5Offset] = item.jp5;
            destination[Jp4Offset] = item.jp4;
            destination[Jp3Offset] = item.jp3;
            if (item.btValue != null && item.btValue.Length == 14)
            {
                item.btValue[0] = item.jp2;
                item.btValue[1] = item.jp1;
                item.btValue[2] = item.jp6;
                item.btValue[3] = item.jp5;
                item.btValue[4] = item.jp4;
                item.btValue[5] = item.jp3;
            }

            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(Ys1Offset, 4), item.ys1);
            destination[Ys2Offset] = item.ys2;
            destination[Ys3Offset] = item.ys3;
            destination[Ys4Offset] = item.ys4;
            destination[Ys5Offset] = item.ys5;
            destination[Ys6Offset] = item.ys6;
            destination[Ys6Offset + 1] = item.ys7;
            destination[Ys6Offset + 2] = item.ys8;
            destination[Ys6Offset + 3] = item.ys9;
            destination[Ys6Offset + 4] = item.ys10;
            destination[Ys6Offset + 5] = item.ys11;
            destination[Ys6Offset + 6] = item.ys12;
            destination[Ys6Offset + 7] = item.ys13;
            destination[Ys6Offset + 8] = item.ys14;
            destination[Ys6Offset + 9] = item.ys15;
            destination[Ys6Offset + 10] = item.ys16;
            destination[Ys17Offset] = item.ys17;

            PackOrigin(item, destination);
        }

        public static void PackAll(params TUserItem[][] containers)
        {
            if (containers == null) return;
            foreach (var container in containers)
            {
                if (container == null) continue;
                foreach (var item in container)
                    if (item != null) Pack(item);
            }
        }

        private static void PackOrigin(TUserItem item, Span<byte> destination)
        {
            var hasDesc = !string.IsNullOrEmpty(item.desc1) || !string.IsNullOrEmpty(item.desc2);
            var hasDrop = !string.IsNullOrEmpty(item.mapName) || !string.IsNullOrEmpty(item.killerName)
                          || !string.IsNullOrEmpty(item.pname) || !string.IsNullOrEmpty(item.sourceTime);
            if (!hasDesc && !hasDrop) return;
            // Neither stamper ever RE-derives this block: it is written once at the
            // moment the item is obtained, and from then on all 208 bytes travel by
            // rep movsd (LOAD 0x74DB3A/0x74DB3D/0x74DB42, SAVE 0x6B170F/0x6B1712/
            // 0x6B1717, both M2Server). Everything this method can write is only a
            // lossy PROJECTION of the block — ReadGbkZ stops at the first NUL while
            // the native map field is a blind 16-byte heap window, and item+0x74 is
            // not modelled at all — so re-deriving replaces good bytes with a worse
            // reconstruction. Stamp only when the caller actually changed something.
            if (OriginUnchanged(item, destination)) return;

            if (TryParseSourceTime(item.sourceTime, out var days, out var minute, out var hour))
            {
                BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(DateDaysOffset, 2), days);
                destination[MinuteOffset] = minute;
                destination[HourOffset] = hour;
            }

            if (hasDesc)
            {
                TryWriteGbk16(destination, Desc1Offset, item.desc1);
                TryWriteDesc2(destination, item.desc2);
                TryWriteGbk16(destination, PNameOffset, item.pname);
                destination[SourceKindOffset] = SourceKindCustom;
            }
            else
            {
                TryWriteGbkFixed(destination.Slice(Desc1Offset, MapTitleSize), item.mapName);
                WriteStructuredSourceName(destination, item.killerName);
                WriteStructuredCharName(destination, item.pname);
                // SourceKindOffset is deliberately NOT written here (see the note on
                // the constant): the native drop stamper does not write item+0x74, so
                // a drop keeps whatever the factory left, which is 0.
            }

            destination[OriginMarkerOffset] = OriginMarkerPresent;
        }

        /// <summary>
        /// True when <paramref name="destination"/> already decodes to exactly the
        /// origin values <paramref name="item"/> is carrying, i.e. there is nothing
        /// to stamp. The comparison mirrors <see cref="Unpack(TUserItem, ReadOnlySpan{byte})"/>
        /// field for field, including which fields that method leaves untouched on
        /// each branch, so "unchanged" here means the same thing as "this is what
        /// Unpack produced".
        /// </summary>
        private static bool OriginUnchanged(TUserItem item, ReadOnlySpan<byte> destination)
        {
            if (destination[OriginMarkerOffset] != OriginMarkerPresent) return false;
            if (!SameText(item.sourceTime, FormatSourceTime(
                    BinaryPrimitives.ReadUInt16LittleEndian(destination.Slice(DateDaysOffset, 2)),
                    destination[MinuteOffset],
                    destination[HourOffset]))) return false;
            if (!SameText(item.mapName, ReadGbkZ(destination.Slice(Desc1Offset, MapTitleSize))))
                return false;

            if (destination[SourceKindOffset] == SourceKindCustom)
            {
                return SameText(item.desc1, ReadGbkZ(destination.Slice(Desc1Offset, StringFieldSize)))
                       && SameText(item.desc2, ReadGbkZ(ReadDesc2(destination)))
                       && SameText(item.pname, ReadGbkZ(destination.Slice(PNameOffset, StringFieldSize)));
            }

            // A drop-stamped record carries no description. If one is set now, the
            // caller turned this into a custom-description item and it must be
            // re-stamped through the SourceKindCustom branch.
            return string.IsNullOrEmpty(item.desc1) && string.IsNullOrEmpty(item.desc2)
                   && SameText(item.killerName, ReadStructuredSourceName(destination))
                   && SameText(item.pname, ReadStructuredCharName(destination));
        }

        private static bool SameText(string left, string right)
            => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);

        private static byte[] ReadDesc2(ReadOnlySpan<byte> record)
        {
            var bytes = new byte[StringFieldSize];
            record.Slice(Desc2Dword0Offset, 4).CopyTo(bytes.AsSpan(0, 4));
            record.Slice(Desc2Dword1Offset, 4).CopyTo(bytes.AsSpan(4, 4));
            record.Slice(Desc2Dword2Offset, 4).CopyTo(bytes.AsSpan(8, 4));
            record.Slice(Desc2Dword3Offset, 4).CopyTo(bytes.AsSpan(12, 4));
            return bytes;
        }

        private static void TryWriteDesc2(Span<byte> destination, string value)
        {
            if (!TryEncodeGbk16(value, out var bytes)) return;
            bytes.AsSpan(0, 4).CopyTo(destination.Slice(Desc2Dword0Offset, 4));
            bytes.AsSpan(4, 4).CopyTo(destination.Slice(Desc2Dword1Offset, 4));
            bytes.AsSpan(8, 4).CopyTo(destination.Slice(Desc2Dword2Offset, 4));
            bytes.AsSpan(12, 4).CopyTo(destination.Slice(Desc2Dword3Offset, 4));
        }

        private static string ReadStructuredSourceName(ReadOnlySpan<byte> record)
        {
            var length = record[Desc2Dword0Offset];
            if (length == 0 || length > 14) return string.Empty;
            var bytes = new byte[length];
            var head = Math.Min(length, (byte)7);
            record.Slice(Desc2Dword0Offset + 1, head).CopyTo(bytes.AsSpan(0, head));
            if (length > 7)
                record.Slice(Desc2Dword2Offset, length - 7).CopyTo(bytes.AsSpan(7, length - 7));
            return ReadGbkZ(bytes);
        }

        private static void WriteStructuredSourceName(Span<byte> destination, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            byte[] bytes;
            try { bytes = Gbk.GetBytes(value); }
            catch (EncoderFallbackException) { return; }
            if (bytes.Length > 14) return;
            destination[Desc2Dword0Offset] = (byte)bytes.Length;
            var head = Math.Min(bytes.Length, 7);
            bytes.AsSpan(0, head).CopyTo(destination.Slice(Desc2Dword0Offset + 1, head));
            if (bytes.Length > 7)
                bytes.AsSpan(7, bytes.Length - 7)
                    .CopyTo(destination.Slice(Desc2Dword2Offset, bytes.Length - 7));
        }

        private static string ReadStructuredCharName(ReadOnlySpan<byte> record)
        {
            var length = record[PNameOffset];
            if (length == 0 || length > 14) return string.Empty;
            return ReadGbkZ(record.Slice(PNameOffset + 1, length));
        }

        private static void WriteStructuredCharName(Span<byte> destination, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            byte[] bytes;
            try { bytes = Gbk.GetBytes(value); }
            catch (EncoderFallbackException) { return; }
            if (bytes.Length > 14) return;
            destination[PNameOffset] = (byte)bytes.Length;
            bytes.CopyTo(destination.Slice(PNameOffset + 1, bytes.Length));
        }

        private static bool TryWriteGbk16(Span<byte> destination, int offset, string value)
        {
            if (!TryEncodeGbk16(value, out var bytes)) return false;
            bytes.CopyTo(destination.Slice(offset, StringFieldSize));
            return true;
        }

        private static void TryWriteGbkFixed(Span<byte> destination, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            byte[] bytes;
            try { bytes = Gbk.GetBytes(value); }
            catch (EncoderFallbackException) { return; }
            if (bytes.Length > destination.Length) return;
            destination.Clear();
            bytes.CopyTo(destination);
        }

        /// <summary>
        /// Plugin skips the 16-byte copy when <c>std::string.size() &gt; 0x10</c>
        /// (<c>100584F5 cmp [ebp-0x34],0x10 / 100584F9 ja</c> → flag 0x64).
        /// </summary>
        private static bool TryEncodeGbk16(string value, out byte[] bytes)
        {
            bytes = new byte[StringFieldSize];
            if (string.IsNullOrEmpty(value)) return false;
            byte[] encoded;
            try { encoded = Gbk.GetBytes(value); }
            catch (EncoderFallbackException) { return false; }
            if (encoded.Length > StringFieldSize) return false;
            encoded.CopyTo(bytes, 0);
            return true;
        }

        private static string ReadGbkZ(ReadOnlySpan<byte> bytes)
        {
            var length = bytes.Length;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == 0)
                {
                    length = i;
                    break;
                }
            }
            if (length == 0) return string.Empty;
            return Gbk.GetString(bytes.Slice(0, length).ToArray());
        }

        private static string FormatSourceTime(ushort days, byte minute, byte hour)
        {
            if (days == 0 && minute == 0 && hour == 0) return string.Empty;
            DateTime date;
            try { date = DelphiDateEpoch.AddDays(days); }
            catch (ArgumentOutOfRangeException) { return string.Empty; }
            if (hour > 23 || minute > 59) return date.ToString("yyyy-MM-dd");
            date = date.AddHours(hour).AddMinutes(minute);
            if (hour == 0 && minute == 0) return date.ToString("yyyy-MM-dd");
            return date.ToString("yyyy-MM-dd HH:mm");
        }

        private static bool TryParseSourceTime(string value, out ushort days, out byte minute, out byte hour)
        {
            days = 0;
            minute = 0;
            hour = 0;
            if (string.IsNullOrEmpty(value)) return false;
            if (!DateTime.TryParse(value, out var parsed)) return false;
            var delta = (parsed.Date - DelphiDateEpoch).Days;
            if (delta < 0 || delta > ushort.MaxValue) return false;
            days = (ushort)delta;
            minute = (byte)parsed.Minute;
            hour = (byte)parsed.Hour;
            return true;
        }
    }
}
