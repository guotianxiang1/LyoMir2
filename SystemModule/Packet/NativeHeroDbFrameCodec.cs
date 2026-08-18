using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace SystemModule
{
    /// <summary>
    /// Codec for the native Delphi hero DB frames used between M2 and DBServer.
    /// The fixed record and dynamic sections remain binary so unmapped fields survive unchanged.
    /// </summary>
    public static class NativeHeroDbFrameCodec
    {
        public const uint FrameMagic = 0x33AABB77;
        public const ushort FrameVersion = 1;
        public const int FrameHeaderSize = 0x0C;
        public const int MessageHeaderSize = 0x48;
        public const int HeroRecordOffset = FrameHeaderSize + MessageHeaderSize;
        public const int HeroRecordSize = 0x49D4;
        public const int HeroFrameBaseSize = HeroRecordOffset + HeroRecordSize;

        public const ushort LoadCommand = 0x0160;
        public const ushort SaveCommand = 0x0161;
        public const ushort CreateCommand = 0x0162;
        public const ushort DeleteCommand = 0x0163;
        public const ushort RenameCommand = 0x0164;
        public const ushort ConsignedListCommand = 0x0165;
        public const ushort RestoreConsignedCommand = 0x0166;
        public const ushort BuildThreeSlotCommand = 0x0167;
        public const ushort DetachCommand = 0x0194;
        public const ushort LoadResponseCommand = 0x0051;
        public const ushort CreateResponseCommand = 0x0053;
        public const ushort DeleteResponseCommand = 0x0059;
        public const ushort RenameResponseCommand = 0x005A;
        public const ushort ConsignedListResponseCommand = 0x005D;
        public const ushort RestoreConsignedResponseCommand = 0x005E;
        public const ushort BuildThreeSlotResponseCommand = 0x0070;
        public const int ConsignedListEntrySize = 22;

        public const uint DynamicSectionMagic = 0xABCDEFAA;
        public const int DynamicHeaderSize = 7;
        public const int MaximumDynamicDataSize = 4 + 5 * (DynamicHeaderSize + ushort.MaxValue);

        // !! 与原生相反，且【不要】单独翻转 —— 见下方证据与迁移要求 (§1.4)。
        //
        // 0x49D4 记录体的两个名字槽，原生 M2Server 的编码器与解码器成对确认：
        //   编码 sub_689034（esi = 英雄, [ebp-4] = 记录基址, ebx = 记录+8）
        //     689045  8D 58 08              lea ebx,[eax+8]          ; 记录 +0x08
        //     689048  8B C3                 mov eax,ebx
        //     68904A  8D 96 06 01 00 00     lea edx,[hero+0x106]     ; 英雄自己的 m_sCharName
        //     689050  B1 0F                 mov cl,0x0F
        //     689052  E8 8D A9 D7 FF        call 0x4039E4            ; ShortString 拷贝, maxlen 15
        //     689057  8D 43 10              lea eax,[ebx+0x10]       ; 记录 +0x18
        //     68905A  8D 96 90 06 00 00     lea edx,[hero+0x690]     ; 主人名
        //     689062  E8 7D A9 D7 FF        call 0x4039E4
        //   解码 sub_6888FC（esi = 记录基址, ebx = esi+8）
        //     688940  8B 45 FC / 05 06 01 00 00  eax = hero+0x106
        //     688948  8B D3 / B1 0E / E8 ..      edx = 记录+0x00, maxlen 14 -> 写英雄自己的名字
        //     688951  ... eax = hero+0x690
        //     688959  8D 53 10 / B1 0F / E8 ..   edx = 记录+0x10 -> 写主人名
        //   记录基址就是 frame+0x54（编码器 0x6888B0 `lea edx,[ebx+0x54]`，
        //   ebx = frame 起点，0x54 == FrameHeaderSize 0x0C + MessageHeaderSize 0x48）。
        //
        // 原生（M2 字节亲验 sub_689034 / sub_6888FC）：记录 +0x08 = HeroName、+0x18 = MasterName。
        // 存量 C# 库若 NameLayout=0/1 仍按错位布局入库 —— 读路径须经
        // <see cref="ApplyStoredNameLayout"/> / <see cref="RequiresLegacyNameSwap"/> 门控，
        // 禁止无标记全表对调（见 docs/dbsvr_hero_name_layout_migration_20260814.sql）。
        public const int HeroNameOffset = 0x0008;
        public const int MasterNameOffset = 0x0018;
        public const int NameSlotByteLength = 16;

        /// <summary>hero_data.NameLayout：未知/待人工复核（禁止自动 swap）。</summary>
        public const byte NameLayoutUnknown = 0;
        /// <summary>错位布局（C# 旧库）；迁移脚本 swap 后升为 2。</summary>
        public const byte NameLayoutLegacySwapped = 1;
        /// <summary>原生正确布局；Save 成功后应写入此值。</summary>
        public const byte NameLayoutNativeCorrect = 2;
        public const int RaceOffset = 0x0028;
        public const int SexOffset = 0x0029;
        public const int JobOffset = 0x002A;
        public const int LevelOffset = 0x002C;
        public const int ExpOffset = 0x0030;
        public const int IndexExpOffset = 0x0030;
        public const int GoldOffset = 0x0034;
        public const int HpLowOffset = 0x0038;
        public const int MpLowOffset = 0x003A;
        /// <summary>
        /// 英雄模式字节 <c>[hero+0x6A1]</c>（0 攻击 / 1 跟随 / 2 休息）的存档槽。
        /// 编码 sub_689034 <c>0x68910A mov al,[esi+0x6A1] / 0x689110 mov [ebx+0x9C],al</c>，
        /// 解码 sub_6888FC <c>0x688A9C mov al,[ebx+0x9C] / 0x688AA5 mov [edx+0x6A1],al</c>，
        /// 两处的 <c>ebx</c> 都是记录基址 + 8，故记录偏移 = 0x9C + 8 = 0xA4。
        /// </summary>
        public const int HeroModeOffset = 0x00A4;
        public const int NativeUnionStateOffset = 0x00AD;
        public const int NativeUnionEnergyOffset = 0x00AE;
        public const int ForceExpOffset = 0x00B4;
        public const int ForceLvOffset = 0x00B8;
        public const int HeroTypeOffset = 0x00BC;
        public const int NativeCommonInformationOption1Offset = 0x00BE;
        public const int NativeCommonInformationOption3Offset = 0x00BF;
        public const int HeroRankOffset = 0x00EA;
        public const int IndexSfLevelOffset = 0x012E;
        public const int HpHighOffset = 0x00FC;
        public const int MpHighOffset = 0x00FE;
        public const int CurrentYOffset = 0x0100;
        public const int CurrentXOffset = 0x0104;
        public const int NativeCommonInformationOption2Offset = 0x0108;
        public const int EquippedItemsOffset = 0x016C;
        public const int NativeUnionChargeTierOffset = 0x0754;
        public const int BagItemsOffset = 0x191A;
        public const int NormalMagicOffset = 0x3DAC;
        public const int SpecialMagicOffset = 0x4900;
        public const int IndexForceLvOffset = 0x46B4;
        public const int IndexForceExpOffset = 0x46BC;
        public const int ItemRecordSize = 0xD0;
        public const int MagicRecordSize = 0x28;
        public const int EquippedItemCount = 16;
        public const int BagItemCount = 40;
        public const int NormalMagicCount = 55;
        public const int SpecialMagicCount = 3;
        /// <summary>
        /// Embedded native TSlaveInfo record inside a fixed hero record.
        /// </summary>
        public const int NativeSlaveRecordOffset = 0x4694;
        public const int NativeSlaveRecordSize = 0x20;

        // Native encoder sub_68ACA4 emits only {2,6,7} (0x68AD4F / 0x68AD78 / 0x68ADA3
        // `mov byte [eax+6], 2/6/7`). Decoder jump table 0x68B0E5: 2→0x68B1A9, 6→0x68B215,
        // 7→0x68B281; types 3/4/5 and >7 share 0x68B2EA (log then skip via 0x68B349).
        private static readonly byte[] DynamicSectionTypes = { 2, 6, 7 };
        private static readonly Encoding Gbk;

        static NativeHeroDbFrameCodec()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        public static bool TryCreateRecord(byte[] data, out NativeHeroRecord record, out string error)
        {
            record = null;
            error = string.Empty;
            if (data == null || data.Length != HeroRecordSize)
            {
                error = $"native hero record length must be {HeroRecordSize}";
                return false;
            }

            try
            {
                ReadShortString(data, MasterNameOffset, 15);
                ReadShortString(data, HeroNameOffset, 15);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero record name: " + ex.Message;
                return false;
            }

            record = new NativeHeroRecord((byte[])data.Clone());
            return true;
        }

        public static bool TryDecodeDynamicData(byte[] data,
            out NativeHeroDynamicData dynamicData, out string error)
        {
            dynamicData = null;
            error = string.Empty;
            if (data == null || data.Length == 0)
            {
                dynamicData = new NativeHeroDynamicData(Array.Empty<NativeHeroDynamicSection>());
                return true;
            }
            if (data.Length < 4 || data.Length > MaximumDynamicDataSize)
            {
                error = "native hero dynData length is invalid";
                return false;
            }

            var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
            if (declaredLength != data.Length - 4)
            {
                error = $"native hero dynData length mismatch: {declaredLength} != {data.Length - 4}";
                return false;
            }

            var sections = new List<NativeHeroDynamicSection>(3);
            var offset = 4;
            // Native decoder 0x68B0D5 `cmp eax,7 / ja 0x68B2EA` then
            // `jmp dword [eax*4+0x68B0E5]` — dispatch by type, no order test.
            // A blob that is 7 then 2 is therefore legal on the read path.
            // The encoder below still emits only {2,6,7} in that order
            // (0x68AD4F / 0x68AD78 / 0x68ADA3).
            while (offset < data.Length)
            {
                // Truncated header / bad magic / short payload: native leaves the
                // already-parsed sections in place and exits the loop.
                // Bad magic 0x68B0B9 `jne 0x68B396` logs then falls to 0x68B3F3.
                // Short payload 0x68B0C9 `jl 0x68B354` logs then `jmp 0x68B3F3`.
                // C# used to `return false` here; TryDecodeLoadResponse then
                // replaced DynamicData with an empty list and dropped 2/6/7.
                if (data.Length - offset < DynamicHeaderSize)
                    break;
                if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4)) != DynamicSectionMagic)
                    break;

                var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 4, 2));
                var type = data[offset + 6];
                var nextOffset = offset + DynamicHeaderSize + payloadLength;
                if (nextOffset > data.Length)
                    break;
                if (type is not (2 or 6 or 7))
                {
                    offset = nextOffset;
                    continue;
                }
                if (payloadLength == 0)
                {
                    error = $"empty native hero dynData section type {type}";
                    return false;
                }
                sections.Add(new NativeHeroDynamicSection(type,
                    data.AsSpan(offset + DynamicHeaderSize, payloadLength).ToArray()));
                offset = nextOffset;
            }

            dynamicData = new NativeHeroDynamicData(sections.ToArray());
            return true;
        }

        public static bool TryEncodeDynamicData(NativeHeroDynamicData dynamicData,
            out byte[] data, out string error)
        {
            data = null;
            error = string.Empty;
            var sections = dynamicData?.Sections ?? Array.Empty<NativeHeroDynamicSection>();
            if (sections.Count == 0)
            {
                data = Array.Empty<byte>();
                return true;
            }

            var size = 4;
            var expectedTypeIndex = 0;
            foreach (var section in sections)
            {
                if (section?.Payload == null || section.Payload.Length == 0
                    || section.Payload.Length > ushort.MaxValue)
                {
                    error = "native hero dynData section payload length is invalid";
                    return false;
                }
                while (expectedTypeIndex < DynamicSectionTypes.Length
                       && DynamicSectionTypes[expectedTypeIndex] != section.Type)
                    expectedTypeIndex++;
                if (expectedTypeIndex >= DynamicSectionTypes.Length)
                {
                    error = $"unknown or out-of-order native hero dynData section type {section.Type}";
                    return false;
                }
                expectedTypeIndex++;
                size = checked(size + DynamicHeaderSize + section.Payload.Length);
            }
            if (size > MaximumDynamicDataSize)
            {
                error = "native hero dynData exceeds the native section limit";
                return false;
            }

            data = new byte[size];
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), (uint)(size - 4));
            var offset = 4;
            foreach (var section in sections)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), DynamicSectionMagic);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 4, 2),
                    (ushort)section.Payload.Length);
                data[offset + 6] = section.Type;
                section.Payload.CopyTo(data, offset + DynamicHeaderSize);
                offset += DynamicHeaderSize + section.Payload.Length;
            }
            return true;
        }

        public static bool TryEncodeLoadRequest(NativeHeroLoadRequest request,
            out byte[] frame, out string error)
        {
            frame = null;
            error = string.Empty;
            if (request == null)
            {
                error = "native hero load request is null";
                return false;
            }

            frame = CreateFrame(MessageHeaderSize);
            var message = frame.AsSpan(FrameHeaderSize, MessageHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(message, LoadCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(message.Slice(2), request.HeroKind);
            BinaryPrimitives.WriteInt32LittleEndian(message.Slice(4), request.HeroSlot);
            if (!TryWriteShortString(message, 16, 20, request.Account, out error)
                || !TryWriteShortString(message, 37, 15, request.MasterName, out error))
            {
                frame = null;
                return false;
            }
            return true;
        }

        public static bool TryDecodeLoadRequest(byte[] frame,
            out NativeHeroLoadRequest request, out string error)
        {
            request = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != LoadCommand)
            {
                error = "native hero frame is not a load request";
                return false;
            }
            // Delphi builds this 0x48-byte message on the stack and only initializes
            // the named fields. Bytes 8..15 and 54..71 therefore carry unspecified data.
            if (payload[53] != 0)
            {
                error = "native hero load request string terminator is nonzero";
                return false;
            }
            try
            {
                request = new NativeHeroLoadRequest
                {
                    HeroKind = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2)),
                    HeroSlot = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4)),
                    Account = ReadShortString(payload, 16, 20),
                    MasterName = ReadShortString(payload, 37, 15)
                };
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero load request string: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Decodes the native M2 hero-detach notification. The original receiver
        /// compares HeroKind to exactly one and reads only the low byte of Mode.
        /// </summary>
        public static bool TryDecodeDetachRequest(byte[] frame,
            out NativeHeroDetachRequest request, out string error)
        {
            request = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload,
                    out error)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != DetachCommand)
            {
                error = "native hero frame is not a detach request";
                return false;
            }
            if (payload[53] != 0)
            {
                error = "native hero detach request string terminator is nonzero";
                return false;
            }
            try
            {
                request = new NativeHeroDetachRequest
                {
                    HeroKind = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2)),
                    Mode = payload[4],
                    Account = ReadShortString(payload, 16, 20),
                    MasterName = ReadShortString(payload, 37, 15)
                };
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero detach request string: " + ex.Message;
                return false;
            }
        }

        public static bool TryEncodeCreateRequest(NativeHeroCreateRequest request,
            out byte[] frame, out string error)
        {
            frame = null;
            if (!ValidateCreateRequest(request, out error)) return false;

            frame = CreateFrame(MessageHeaderSize);
            var message = frame.AsSpan(FrameHeaderSize, MessageHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(message, CreateCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(message.Slice(2), request.HeroType);
            BinaryPrimitives.WriteInt32LittleEndian(message.Slice(4), request.Code);
            if (!TryWriteShortString(message, 16, 20, request.Account, out error)
                || !TryWriteShortString(message, 37, 15, request.MasterName, out error)
                || !TryWriteShortString(message, 53, 15, request.HeroName, out error))
            {
                frame = null;
                return false;
            }
            return true;
        }

        public static bool TryDecodeCreateRequest(byte[] frame,
            out NativeHeroCreateRequest request, out string error)
        {
            request = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error,
                    allowTrailing: true)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != CreateCommand)
            {
                error = "native hero frame is not a create request";
                return false;
            }

            try
            {
                request = new NativeHeroCreateRequest
                {
                    HeroType = payload[2],
                    Code = payload[4],
                    Account = string.Empty,
                    MasterName = ReadShortString(payload, 37, 15),
                    HeroName = ReadShortString(payload, 53, 15)
                };
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero create request string: " + ex.Message;
                request = null;
                return false;
            }
            return true;
        }

        public static bool TryEncodeCreateResponse(NativeHeroCreateResponse response,
            out byte[] frame, out string error)
        {
            frame = null;
            if (!ValidateCreateResponse(response, out error)) return false;

            frame = CreateFrame(MessageHeaderSize);
            var message = frame.AsSpan(FrameHeaderSize, MessageHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(message, CreateResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(message.Slice(2), response.HeroType);
            BinaryPrimitives.WriteInt32LittleEndian(message.Slice(4), response.Result);
            if (!TryWriteShortString(message, 37, 15, response.MasterName, out error)
                || !TryWriteShortString(message, 53, 15, response.HeroName, out error))
            {
                frame = null;
                return false;
            }
            return true;
        }

        public static bool TryDecodeCreateResponse(byte[] frame,
            out NativeHeroCreateResponse response, out string error)
        {
            response = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != CreateResponseCommand)
            {
                error = "native hero frame is not a create response";
                return false;
            }

            try
            {
                response = new NativeHeroCreateResponse
                {
                    HeroType = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2)),
                    Result = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4)),
                    MasterName = ReadShortString(payload, 37, 15),
                    HeroName = ReadShortString(payload, 53, 15)
                };
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero create response string: " + ex.Message;
                response = null;
                return false;
            }
            if (ValidateCreateResponse(response, out error)) return true;
            response = null;
            return false;
        }

        public static bool TryEncodeDeleteRequest(NativeHeroDeleteRequest request,
            out byte[] frame, out string error)
        {
            frame = null;
            if (!ValidateDeleteRequest(request, out error)) return false;

            frame = CreateFrame(MessageHeaderSize);
            var message = frame.AsSpan(FrameHeaderSize, MessageHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(message, DeleteCommand);
            if (!TryWriteShortString(message, 16, 20, request.Account, out error)
                || !TryWriteShortString(message, 37, 15, request.MasterName, out error)
                || !TryWriteShortString(message, 53, 15, request.HeroName, out error))
            {
                frame = null;
                return false;
            }
            return true;
        }

        public static bool TryDecodeDeleteRequest(byte[] frame,
            out NativeHeroDeleteRequest request, out string error)
        {
            request = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != DeleteCommand
                || BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2)) != 0
                || BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4)) != 0)
            {
                error = "native hero frame is not a delete request";
                return false;
            }
            try
            {
                request = new NativeHeroDeleteRequest
                {
                    Account = ReadShortString(payload, 16, 20),
                    MasterName = ReadShortString(payload, 37, 15),
                    HeroName = ReadShortString(payload, 53, 15)
                };
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero delete request string: " + ex.Message;
                return false;
            }
            if (ValidateDeleteRequest(request, out error)) return true;
            request = null;
            return false;
        }

        public static bool TryEncodeDeleteResponse(NativeHeroDeleteResponse response,
            out byte[] frame, out string error)
        {
            frame = null;
            if (!ValidateDeleteResponse(response, out error)) return false;

            frame = CreateFrame(MessageHeaderSize);
            var message = frame.AsSpan(FrameHeaderSize, MessageHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(message, DeleteResponseCommand);
            BinaryPrimitives.WriteInt32LittleEndian(message.Slice(4), response.Result);
            if (!TryWriteShortString(message, 16, 20, response.Account, out error)
                || !TryWriteShortString(message, 37, 15, response.MasterName, out error)
                || !TryWriteShortString(message, 53, 15, response.HeroName, out error))
            {
                frame = null;
                return false;
            }
            return true;
        }

        public static bool TryDecodeDeleteResponse(byte[] frame,
            out NativeHeroDeleteResponse response, out string error)
        {
            response = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != DeleteResponseCommand)
            {
                error = "native hero frame is not a delete response";
                return false;
            }
            try
            {
                response = new NativeHeroDeleteResponse
                {
                    Result = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4)),
                    Account = ReadShortString(payload, 16, 20),
                    MasterName = ReadShortString(payload, 37, 15),
                    HeroName = ReadShortString(payload, 53, 15)
                };
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero delete response string: " + ex.Message;
                return false;
            }
            if (ValidateDeleteResponse(response, out error)) return true;
            response = null;
            return false;
        }

        public static bool TryEncodeRenameRequest(NativeHeroRenameRequest request,
            out byte[] frame, out string error)
        {
            frame = null;
            if (request == null || request.SelectionMode > 1)
            {
                error = "native hero rename selection mode must be 0 or 1";
                return false;
            }
            frame = CreateFrame(MessageHeaderSize);
            var message = frame.AsSpan(FrameHeaderSize, MessageHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(message, RenameCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(message.Slice(2), request.SelectionMode);
            BinaryPrimitives.WriteInt32LittleEndian(message.Slice(4), request.Code);
            if (TryWriteShortString(message, 16, 20, request.OldHeroName, out error)
                && TryWriteShortString(message, 37, 15, request.MasterName, out error)
                && TryWriteShortString(message, 53, 15, request.NewHeroName, out error))
                return true;
            frame = null;
            return false;
        }

        public static bool TryDecodeRenameRequest(byte[] frame,
            out NativeHeroRenameRequest request, out string error)
        {
            request = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != RenameCommand)
            {
                error = "native hero frame is not a rename request";
                return false;
            }
            var selectionMode = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2));
            if (selectionMode > 1)
            {
                error = "native hero rename selection mode must be 0 or 1";
                return false;
            }
            try
            {
                request = new NativeHeroRenameRequest
                {
                    SelectionMode = selectionMode,
                    Code = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4)),
                    OldHeroName = ReadShortString(payload, 16, 20),
                    MasterName = ReadShortString(payload, 37, 15),
                    NewHeroName = ReadShortString(payload, 53, 15)
                };
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero rename request string: " + ex.Message;
                return false;
            }
        }

        public static bool TryEncodeRenameResponse(NativeHeroRenameResponse response,
            out byte[] frame, out string error)
        {
            frame = null;
            if (response == null || response.Result is < 0 or > 5)
            {
                error = "native hero rename result must be in 0..5";
                return false;
            }
            frame = CreateFrame(MessageHeaderSize);
            var message = frame.AsSpan(FrameHeaderSize, MessageHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(message, RenameResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(message.Slice(2), response.Result);
            BinaryPrimitives.WriteInt32LittleEndian(message.Slice(4), response.Code);
            if (TryWriteShortString(message, 37, 15, response.MasterName, out error)
                && TryWriteShortString(message, 53, 15, response.NewHeroName, out error))
                return true;
            frame = null;
            return false;
        }

        public static bool TryDecodeRenameResponse(byte[] frame,
            out NativeHeroRenameResponse response, out string error)
        {
            response = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != RenameResponseCommand)
            {
                error = "native hero frame is not a rename response";
                return false;
            }
            var result = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2));
            if (result > 5)
            {
                error = "native hero rename result must be in 0..5";
                return false;
            }
            try
            {
                response = new NativeHeroRenameResponse
                {
                    Result = result,
                    Code = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4)),
                    MasterName = ReadShortString(payload, 37, 15),
                    NewHeroName = ReadShortString(payload, 53, 15)
                };
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero rename response string: " + ex.Message;
                return false;
            }
        }

        public static bool TryEncodeConsignedListRequest(NativeHeroConsignedListRequest request,
            out byte[] frame, out string error)
        {
            frame = null;
            if (request == null)
            {
                error = "native hero consigned-list request is null";
                return false;
            }
            frame = CreateFrame(MessageHeaderSize);
            var message = frame.AsSpan(FrameHeaderSize, MessageHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(message, ConsignedListCommand);
            if (TryWriteShortString(message, 37, 15, request.MasterName, out error)) return true;
            frame = null;
            return false;
        }

        public static bool TryDecodeConsignedListRequest(byte[] frame,
            out NativeHeroConsignedListRequest request, out string error)
        {
            request = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != ConsignedListCommand)
            {
                error = "native hero frame is not a consigned-list request";
                return false;
            }
            try
            {
                request = new NativeHeroConsignedListRequest
                    { MasterName = ReadShortString(payload, 37, 15) };
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero consigned-list request string: " + ex.Message;
                return false;
            }
        }

        public static bool TryEncodeConsignedListResponse(NativeHeroConsignedListResponse response,
            out byte[] frame, out string error)
        {
            frame = null;
            error = string.Empty;
            var entries = response?.Entries ?? Array.Empty<NativeHeroConsignedListEntry>();
            if (response == null || entries.Count > 3)
            {
                error = "native hero consigned-list response count must be in 0..3";
                return false;
            }
            frame = CreateFrame(checked(MessageHeaderSize + entries.Count * ConsignedListEntrySize));
            var payload = frame.AsSpan(FrameHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(payload, ConsignedListResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(2), (ushort)entries.Count);
            if (!TryWriteShortString(payload, 37, 15, response.MasterName, out error))
            {
                frame = null;
                return false;
            }
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.HeroType > byte.MaxValue || entry.Job > byte.MaxValue
                    || entry.Level > ushort.MaxValue || entry.Sex > byte.MaxValue)
                {
                    error = $"native hero consigned-list entry {i} is invalid";
                    frame = null;
                    return false;
                }
                var target = payload.Slice(MessageHeaderSize + i * ConsignedListEntrySize,
                    ConsignedListEntrySize);
                if (!TryWriteShortString(target, 0, 15, entry.HeroName, out error))
                {
                    frame = null;
                    return false;
                }
                target[16] = (byte)entry.HeroType;
                target[17] = (byte)entry.Job;
                BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(18), (ushort)entry.Level);
                target[20] = (byte)entry.Sex;
            }
            return true;
        }

        public static bool TryDecodeConsignedListResponse(byte[] frame,
            out NativeHeroConsignedListResponse response, out string error)
        {
            response = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error,
                    allowTrailing: true)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != ConsignedListResponseCommand)
            {
                error = "native hero frame is not a consigned-list response";
                return false;
            }
            var count = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2));
            if (count > 3)
            {
                error = "native hero consigned-list response count must be in 0..3";
                return false;
            }
            if (payload.Length != MessageHeaderSize + count * ConsignedListEntrySize)
            {
                error = "native hero consigned-list response is not an exact 22-byte entry list";
                return false;
            }
            try
            {
                var entries = new NativeHeroConsignedListEntry[count];
                for (var i = 0; i < entries.Length; i++)
                {
                    var source = payload.Slice(MessageHeaderSize + i * ConsignedListEntrySize,
                        ConsignedListEntrySize);
                    entries[i] = new NativeHeroConsignedListEntry
                    {
                        HeroName = ReadShortString(source, 0, 15),
                        HeroType = source[16],
                        Job = source[17],
                        Level = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(18)),
                        Sex = source[20]
                    };
                }
                response = new NativeHeroConsignedListResponse
                {
                    MasterName = ReadShortString(payload, 37, 15),
                    Entries = entries
                };
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero consigned-list response string: " + ex.Message;
                return false;
            }
        }

        public static bool TryEncodeRestoreConsignedRequest(NativeHeroRestoreConsignedRequest request,
            out byte[] frame, out string error)
        {
            frame = null;
            if (request == null)
            {
                error = "native hero restore-consigned request is null";
                return false;
            }
            frame = CreateFrame(MessageHeaderSize);
            var payload = frame.AsSpan(FrameHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(payload, RestoreConsignedCommand);
            if (TryWriteShortString(payload, 37, 15, request.MasterName, out error)
                && TryWriteShortString(payload, 53, 15, request.HeroName, out error))
                return true;
            frame = null;
            return false;
        }

        public static bool TryDecodeRestoreConsignedRequest(byte[] frame,
            out NativeHeroRestoreConsignedRequest request, out string error)
        {
            request = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != RestoreConsignedCommand)
            {
                error = "native hero frame is not a restore-consigned request";
                return false;
            }
            try
            {
                request = new NativeHeroRestoreConsignedRequest
                {
                    MasterName = ReadShortString(payload, 37, 15),
                    HeroName = ReadShortString(payload, 53, 15)
                };
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero restore-consigned request string: " + ex.Message;
                return false;
            }
        }

        public static bool TryEncodeRestoreConsignedResponse(NativeHeroRestoreConsignedResponse response,
            out byte[] frame, out string error)
        {
            frame = null;
            if (response == null || response.Result is < 0 or > 2
                || response.HeroType is < 0 or > byte.MaxValue)
            {
                error = "native hero restore-consigned response is invalid";
                return false;
            }
            frame = CreateFrame(MessageHeaderSize);
            var payload = frame.AsSpan(FrameHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(payload, RestoreConsignedResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(2), (ushort)response.Result);
            BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(4), response.HeroType);
            if (TryWriteShortString(payload, 37, 15, response.MasterName, out error)
                && TryWriteShortString(payload, 53, 15, response.HeroName, out error))
                return true;
            frame = null;
            return false;
        }

        public static bool TryDecodeRestoreConsignedResponse(byte[] frame,
            out NativeHeroRestoreConsignedResponse response, out string error)
        {
            response = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != RestoreConsignedResponseCommand)
            {
                error = "native hero frame is not a restore-consigned response";
                return false;
            }
            var result = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2));
            var heroType = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4));
            if (result > 2 || heroType is < 0 or > byte.MaxValue)
            {
                error = "native hero restore-consigned response is invalid";
                return false;
            }
            try
            {
                response = new NativeHeroRestoreConsignedResponse
                {
                    Result = result,
                    HeroType = heroType,
                    MasterName = ReadShortString(payload, 37, 15),
                    HeroName = ReadShortString(payload, 53, 15)
                };
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero restore-consigned response string: " + ex.Message;
                return false;
            }
        }

        public static bool TryEncodeBuildThreeSlotRequest(NativeHeroBuildThreeSlotRequest request,
            out byte[] frame, out string error)
        {
            frame = null;
            if (request == null)
            {
                error = "native hero three-slot request is null";
                return false;
            }
            frame = CreateFrame(MessageHeaderSize);
            var payload = frame.AsSpan(FrameHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(payload, BuildThreeSlotCommand);
            if (TryWriteShortString(payload, 37, 15, request.MasterName, out error)) return true;
            frame = null;
            return false;
        }

        public static bool TryDecodeBuildThreeSlotRequest(byte[] frame,
            out NativeHeroBuildThreeSlotRequest request, out string error)
        {
            request = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != BuildThreeSlotCommand)
            {
                error = "native hero frame is not a three-slot request";
                return false;
            }
            try
            {
                request = new NativeHeroBuildThreeSlotRequest
                    { MasterName = ReadShortString(payload, 37, 15) };
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero three-slot request string: " + ex.Message;
                return false;
            }
        }

        public static bool TryEncodeBuildThreeSlotResponse(NativeHeroBuildThreeSlotResponse response,
            out byte[] frame, out string error)
        {
            frame = null;
            if (response == null || response.Result > 6)
            {
                error = "native hero three-slot result must be in 0..6";
                return false;
            }
            frame = CreateFrame(MessageHeaderSize);
            var payload = frame.AsSpan(FrameHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(payload, BuildThreeSlotResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(2), response.Result);
            if (TryWriteShortString(payload, 16, 20, response.MasterName, out error)
                && TryWriteShortString(payload, 37, 15, response.HeroName, out error))
                return true;
            frame = null;
            return false;
        }

        public static bool TryDecodeBuildThreeSlotResponse(byte[] frame,
            out NativeHeroBuildThreeSlotResponse response, out string error)
        {
            response = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error)) return false;
            var result = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2));
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != BuildThreeSlotResponseCommand
                || result > 6
                || BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4)) != 0)
            {
                error = "native hero frame is not a three-slot response";
                return false;
            }
            try
            {
                response = new NativeHeroBuildThreeSlotResponse
                {
                    Result = result,
                    MasterName = ReadShortString(payload, 16, 20),
                    HeroName = ReadShortString(payload, 37, 15)
                };
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero three-slot response string: " + ex.Message;
                return false;
            }
        }

        public static bool TryRenameRecord(byte[] source, string newHeroName,
            out byte[] renamed, out string error)
        {
            renamed = null;
            if (!TryCreateRecord(source, out _, out error)) return false;
            renamed = (byte[])source.Clone();
            if (TryWriteShortString(renamed, HeroNameOffset, 15, newHeroName, out error))
                return true;
            renamed = null;
            return false;
        }

        public static bool TryCreateInitialRecord(NativeHeroCreateRequest request,
            out NativeHeroRecord record, out string error)
        {
            record = null;
            if (!ValidateCreateRequest(request, out error)) return false;

            var data = new byte[HeroRecordSize];
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2), HeroRecordSize);
            if (!TryWriteShortString(data, MasterNameOffset, 15, request.MasterName, out error)
                || !TryWriteShortString(data, HeroNameOffset, 15, request.HeroName, out error))
                return false;
            var zeroBasedCode = request.Code - 1;
            data[RaceOffset] = 1;
            data[SexOffset] = (byte)(zeroBasedCode / 3);
            data[JobOffset] = (byte)(zeroBasedCode % 3);
            data[HeroTypeOffset] = (byte)request.HeroType;
            return TryCreateRecord(data, out record, out error);
        }

        public static bool TryEncodeSaveRequest(NativeHeroSaveRequest request,
            out byte[] frame, out string error)
        {
            frame = null;
            error = string.Empty;
            if (request?.Record == null)
            {
                error = "native hero save request record is null";
                return false;
            }
            if (!TryEncodeDynamicData(request.DynamicData, out var dynData, out error)) return false;

            frame = CreateFrame(MessageHeaderSize + HeroRecordSize + dynData.Length);
            var message = frame.AsSpan(FrameHeaderSize, MessageHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(message, SaveCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(message.Slice(2), request.SaveMode);
            BinaryPrimitives.WriteInt32LittleEndian(message.Slice(8), request.Param1);
            BinaryPrimitives.WriteInt32LittleEndian(message.Slice(12), request.Param2);
            if (!TryWriteShortString(message, 37, 15, request.Record.MasterName, out error)
                || !TryWriteShortString(message, 53, 15, request.Record.HeroName, out error))
            {
                frame = null;
                return false;
            }
            request.Record.CopyTo(frame.AsSpan(HeroRecordOffset, HeroRecordSize));
            dynData.CopyTo(frame, HeroFrameBaseSize);
            return true;
        }

        public static bool TryDecodeSaveRequest(byte[] frame,
            out NativeHeroSaveRequest request, out string error)
        {
            request = null;
            if (!TryGetPayload(frame, MessageHeaderSize + HeroRecordSize,
                    out var payload, out error, allowTrailing: true)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != SaveCommand)
            {
                error = "native hero frame is not a save request";
                return false;
            }
            string masterName;
            string heroName;
            try
            {
                masterName = ReadShortString(payload, 37, 15);
                heroName = ReadShortString(payload, 53, 15);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero save request string: " + ex.Message;
                return false;
            }

            if (!TryCreateRecord(payload.Slice(MessageHeaderSize, HeroRecordSize).ToArray(),
                    out var record, out error)) return false;
            var rawDynamicData = payload.Slice(
                MessageHeaderSize + HeroRecordSize).ToArray();
            if (!TryDecodeDynamicData(rawDynamicData, out var dynamicData, out _))
                dynamicData = new NativeHeroDynamicData(
                    Array.Empty<NativeHeroDynamicSection>());

            request = new NativeHeroSaveRequest
            {
                SaveMode = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2)),
                Param1 = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8)),
                Param2 = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(12)),
                MasterName = masterName,
                HeroName = heroName,
                Record = record,
                DynamicData = dynamicData,
                RawDynamicData = rawDynamicData
            };
            return true;
        }

        public static bool TryEncodeLoadResponse(NativeHeroLoadResponse response,
            out byte[] frame, out string error)
        {
            frame = null;
            error = string.Empty;
            if (response == null)
            {
                error = "native hero load response is null";
                return false;
            }
            if (response.Status != 1)
            {
                frame = CreateFrame(MessageHeaderSize);
                var failure = frame.AsSpan(FrameHeaderSize, MessageHeaderSize);
                BinaryPrimitives.WriteUInt16LittleEndian(failure, LoadResponseCommand);
                BinaryPrimitives.WriteUInt16LittleEndian(failure.Slice(2), response.Status);
                return TryWriteShortString(failure, 16, 20, response.MasterName, out error);
            }
            if (response.Record == null)
            {
                error = "successful native hero load response has no record";
                return false;
            }
            byte[] dynData;
            if (response.RawDynamicData != null)
                dynData = (byte[])response.RawDynamicData.Clone();
            else if (!TryEncodeDynamicData(response.DynamicData, out dynData, out error))
                return false;

            frame = CreateFrame(MessageHeaderSize + HeroRecordSize + dynData.Length);
            var message = frame.AsSpan(FrameHeaderSize, MessageHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(message, LoadResponseCommand);
            BinaryPrimitives.WriteUInt16LittleEndian(message.Slice(2), 1);
            BinaryPrimitives.WriteInt32LittleEndian(message.Slice(4), dynData.Length);
            if (!TryWriteShortString(message, 16, 20, response.Record.HeroName, out error)
                || !TryWriteShortString(message, 37, 15, response.Record.MasterName, out error))
            {
                frame = null;
                return false;
            }
            response.Record.CopyTo(frame.AsSpan(HeroRecordOffset, HeroRecordSize));
            dynData.CopyTo(frame, HeroFrameBaseSize);
            return true;
        }

        public static bool TryDecodeLoadResponse(byte[] frame,
            out NativeHeroLoadResponse response, out string error)
        {
            response = null;
            if (!TryGetPayload(frame, MessageHeaderSize, out var payload, out error,
                    allowTrailing: true)) return false;
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != LoadResponseCommand)
            {
                error = "native hero frame is not a load response";
                return false;
            }
            if (!RangeIsZero(payload, 8, 8) || !RangeIsZero(payload, 53, 19))
            {
                error = "native hero load response reserved bytes are nonzero";
                return false;
            }

            string heroName;
            string masterName;
            try
            {
                heroName = ReadShortString(payload, 16, 20);
                masterName = ReadShortString(payload, 37, 15);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
            {
                error = "invalid native hero load response string: " + ex.Message;
                return false;
            }

            var status = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(2));
            var dynLength = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4));
            if (status != 1)
            {
                if (payload.Length != MessageHeaderSize || dynLength != 0 || masterName.Length != 0)
                {
                    error = "invalid failed native hero load response body";
                    return false;
                }
                response = new NativeHeroLoadResponse { Status = status, MasterName = heroName };
                return true;
            }
            if (dynLength < 0 || dynLength > MaximumDynamicDataSize
                || payload.Length != MessageHeaderSize + HeroRecordSize + dynLength)
            {
                error = "native hero load response dynData length mismatch";
                return false;
            }
            if (!TryCreateRecord(payload.Slice(MessageHeaderSize, HeroRecordSize).ToArray(),
                    out var record, out error)) return false;
            if (!string.Equals(heroName, record.HeroName, StringComparison.Ordinal)
                || !string.Equals(masterName, record.MasterName, StringComparison.Ordinal))
            {
                error = "native hero load metadata does not match the fixed record";
                return false;
            }
            var rawDynamicData = payload.Slice(
                MessageHeaderSize + HeroRecordSize, dynLength).ToArray();
            if (!TryDecodeDynamicData(rawDynamicData, out var dynamicData, out _))
                dynamicData = new NativeHeroDynamicData(
                    Array.Empty<NativeHeroDynamicSection>());

            response = new NativeHeroLoadResponse
            {
                Status = status,
                HeroName = heroName,
                MasterName = masterName,
                Record = record,
                DynamicData = dynamicData,
                RawDynamicData = rawDynamicData
            };
            return true;
        }

        private static byte[] CreateFrame(int payloadLength)
        {
            var frame = new byte[checked(FrameHeaderSize + payloadLength)];
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), FrameMagic);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), FrameVersion);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(8, 4), payloadLength);
            return frame;
        }

        private static bool ValidateCreateRequest(
            NativeHeroCreateRequest request, out string error)
        {
            error = string.Empty;
            if (request == null)
            {
                error = "native hero create request is null";
                return false;
            }
            if (request.HeroType is < 1 or > 2)
            {
                error = "native hero create HeroType must be 1 or 2";
                return false;
            }
            if (request.Code is < 1 or > 6)
            {
                error = "native hero create code must be in 1..6";
                return false;
            }
            return true;
        }

        private static bool ValidateCreateResponse(
            NativeHeroCreateResponse response, out string error)
        {
            error = string.Empty;
            if (response == null)
            {
                error = "native hero create response is null";
                return false;
            }
            if (response.HeroType > byte.MaxValue)
            {
                error = "native hero create response HeroType must fit one byte";
                return false;
            }
            if (response.Result == 0 || response.Result < -6 || response.Result > 6)
            {
                error = "native hero create result must be -6..-1 or 1..6";
                return false;
            }
            return true;
        }

        private static bool ValidateDeleteRequest(
            NativeHeroDeleteRequest request, out string error)
        {
            error = string.Empty;
            if (request != null) return true;
            error = "native hero delete request is null";
            return false;
        }

        private static bool ValidateDeleteResponse(
            NativeHeroDeleteResponse response, out string error)
        {
            error = string.Empty;
            if (response == null)
            {
                error = "native hero delete response is null";
                return false;
            }
            if (response.Result is >= 0 and <= 3) return true;
            error = "native hero delete result must be in 0..3";
            return false;
        }

        private static bool TryGetPayload(byte[] frame, int minimumPayloadLength,
            out ReadOnlySpan<byte> payload, out string error, bool allowTrailing = false)
        {
            payload = default;
            error = string.Empty;
            if (frame == null || frame.Length < FrameHeaderSize)
            {
                error = "native hero frame is truncated";
                return false;
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4)) != FrameMagic)
            {
                error = "native hero frame magic mismatch";
                return false;
            }
            if (BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4, 2)) != FrameVersion)
            {
                error = "native hero frame version is invalid";
                return false;
            }
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(8, 4));
            if (payloadLength < minimumPayloadLength || frame.Length != FrameHeaderSize + payloadLength)
            {
                error = "native hero frame payload length mismatch";
                return false;
            }
            if (!allowTrailing && payloadLength != minimumPayloadLength)
            {
                error = "native hero frame has unexpected trailing data";
                return false;
            }
            if (payloadLength > MessageHeaderSize + HeroRecordSize + MaximumDynamicDataSize)
            {
                error = "native hero frame exceeds the native maximum length";
                return false;
            }
            payload = frame.AsSpan(FrameHeaderSize, payloadLength);
            return true;
        }

        private static bool TryWriteShortString(Span<byte> destination, int offset,
            int maximumLength, string value, out string error)
        {
            error = string.Empty;
            byte[] bytes;
            try
            {
                bytes = Gbk.GetBytes(value ?? string.Empty);
            }
            catch (EncoderFallbackException ex)
            {
                error = "native hero string is not GBK: " + ex.Message;
                return false;
            }
            if (bytes.Length > maximumLength)
            {
                error = $"native hero string exceeds {maximumLength} GBK bytes";
                return false;
            }
            destination.Slice(offset, maximumLength + 1).Clear();
            destination[offset] = (byte)bytes.Length;
            bytes.CopyTo(destination.Slice(offset + 1));
            return true;
        }

        private static string ReadShortString(ReadOnlySpan<byte> data, int offset, int maximumLength)
        {
            var length = data[offset];
            if (length > maximumLength)
                throw new ArgumentException($"short string length {length} exceeds {maximumLength} at 0x{offset:X}");
            return Gbk.GetString(data.Slice(offset + 1, length).ToArray());
        }

        private static bool RangeIsZero(ReadOnlySpan<byte> data, int offset, int length)
        {
            for (var i = offset; i < offset + length; i++)
                if (data[i] != 0) return false;
            return true;
        }

        internal static string ReadRecordString(byte[] data, int offset, int maximumLength)
            => ReadShortString(data, offset, maximumLength);

        public static bool RequiresLegacyNameSwap(byte nameLayout) =>
            nameLayout == NameLayoutUnknown || nameLayout == NameLayoutLegacySwapped;

        public static bool IsNameLayoutLoadRejected(byte nameLayout) =>
            nameLayout == NameLayoutLegacySwapped;

        /// <summary>
        /// 对单条 0x49D4 记录就地交换 +0x08/+0x18 两个 ShortString 槽（各 16 字节）。
        /// </summary>
        public static void SwapRecordNameSlots(Span<byte> record)
        {
            if (record.Length < HeroRecordSize)
                throw new ArgumentException(
                    $"native hero record length must be {HeroRecordSize}",
                    nameof(record));
            Span<byte> temp = stackalloc byte[NameSlotByteLength];
            record.Slice(HeroNameOffset, NameSlotByteLength).CopyTo(temp);
            record.Slice(MasterNameOffset, NameSlotByteLength).CopyTo(
                record.Slice(HeroNameOffset, NameSlotByteLength));
            temp.CopyTo(record.Slice(MasterNameOffset, NameSlotByteLength));
        }

        /// <summary>
        /// 三槽包按 stride 0x49D4 独立处理；layout=2 无操作，layout=1 由调用方拒绝加载。
        /// </summary>
        public static void ApplyStoredNameLayout(Span<byte> dataBlob, byte nameLayout)
        {
            if (!RequiresLegacyNameSwap(nameLayout) || dataBlob.IsEmpty)
                return;
            if (dataBlob.Length != HeroRecordSize
                && dataBlob.Length != HeroRecordSize * 3)
                return;
            for (var offset = 0; offset < dataBlob.Length;
                 offset += HeroRecordSize)
                SwapRecordNameSlots(dataBlob.Slice(offset, HeroRecordSize));
        }
    }

    public sealed class NativeHeroRecord
    {
        private readonly byte[] _data;

        internal NativeHeroRecord(byte[] data) => _data = data;

        public string MasterName => NativeHeroDbFrameCodec.ReadRecordString(
            _data, NativeHeroDbFrameCodec.MasterNameOffset, 15);
        public string HeroName => NativeHeroDbFrameCodec.ReadRecordString(
            _data, NativeHeroDbFrameCodec.HeroNameOffset, 15);
        public byte Race => _data[NativeHeroDbFrameCodec.RaceOffset];
        public byte Sex => _data[NativeHeroDbFrameCodec.SexOffset];
        public byte Job => _data[NativeHeroDbFrameCodec.JobOffset];
        public ushort Level => BinaryPrimitives.ReadUInt16LittleEndian(
            _data.AsSpan(NativeHeroDbFrameCodec.LevelOffset, 2));
        public int Gold => BinaryPrimitives.ReadInt32LittleEndian(
            _data.AsSpan(NativeHeroDbFrameCodec.GoldOffset, 4));
        public uint Exp => BinaryPrimitives.ReadUInt32LittleEndian(
            _data.AsSpan(NativeHeroDbFrameCodec.ExpOffset, 4));
        public uint IndexExp => BinaryPrimitives.ReadUInt32LittleEndian(
            _data.AsSpan(NativeHeroDbFrameCodec.IndexExpOffset, 4));
        public uint Hp => ReadSplitUInt32(NativeHeroDbFrameCodec.HpLowOffset,
            NativeHeroDbFrameCodec.HpHighOffset);
        public uint Mp => ReadSplitUInt32(NativeHeroDbFrameCodec.MpLowOffset,
            NativeHeroDbFrameCodec.MpHighOffset);
        public int ForceExp => BinaryPrimitives.ReadInt32LittleEndian(
            _data.AsSpan(NativeHeroDbFrameCodec.ForceExpOffset, 4));
        public int ForceLv => BinaryPrimitives.ReadInt32LittleEndian(
            _data.AsSpan(NativeHeroDbFrameCodec.ForceLvOffset, 4));
        public ushort IndexForceLv => BinaryPrimitives.ReadUInt16LittleEndian(
            _data.AsSpan(NativeHeroDbFrameCodec.IndexForceLvOffset, 2));
        public uint IndexForceExp => BinaryPrimitives.ReadUInt32LittleEndian(
            _data.AsSpan(NativeHeroDbFrameCodec.IndexForceExpOffset, 4));
        public ushort IndexSfLevel => BinaryPrimitives.ReadUInt16LittleEndian(
            _data.AsSpan(NativeHeroDbFrameCodec.IndexSfLevelOffset, 2));
        public byte HeroType => _data[NativeHeroDbFrameCodec.HeroTypeOffset];
        public byte HeroRank => _data[NativeHeroDbFrameCodec.HeroRankOffset];
        public int CurrentX => BinaryPrimitives.ReadInt32LittleEndian(
            _data.AsSpan(NativeHeroDbFrameCodec.CurrentXOffset, 4));
        public int CurrentY => BinaryPrimitives.ReadInt32LittleEndian(
            _data.AsSpan(NativeHeroDbFrameCodec.CurrentYOffset, 4));
        public bool NativeCommonInformationOption1 =>
            _data[NativeHeroDbFrameCodec.NativeCommonInformationOption1Offset] != 0;
        public int NativeCommonInformationOption2 => BinaryPrimitives.ReadInt32LittleEndian(
            _data.AsSpan(NativeHeroDbFrameCodec.NativeCommonInformationOption2Offset, 4));
        public bool NativeCommonInformationOption3 =>
            _data[NativeHeroDbFrameCodec.NativeCommonInformationOption3Offset] != 0;

        public byte[] ToArray() => (byte[])_data.Clone();

        internal void CopyTo(Span<byte> destination) => _data.CopyTo(destination);

        private uint ReadSplitUInt32(int lowOffset, int highOffset)
            => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(lowOffset, 2))
               | ((uint)BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(highOffset, 2)) << 16);
    }

    public sealed class NativeHeroDynamicSection
    {
        public NativeHeroDynamicSection(byte type, byte[] payload)
        {
            Type = type;
            Payload = payload == null ? null : (byte[])payload.Clone();
        }

        public byte Type { get; }
        public byte[] Payload { get; }
    }

    public sealed class NativeHeroDynamicData
    {
        public NativeHeroDynamicData(IReadOnlyList<NativeHeroDynamicSection> sections)
            => Sections = sections ?? Array.Empty<NativeHeroDynamicSection>();

        public IReadOnlyList<NativeHeroDynamicSection> Sections { get; }
    }

    public sealed class NativeHeroLoadRequest
    {
        public ushort HeroKind { get; set; }
        public int HeroSlot { get; set; }
        public string Account { get; set; }
        public string MasterName { get; set; }
    }

    public sealed class NativeHeroDetachRequest
    {
        public ushort HeroKind { get; set; }
        public byte Mode { get; set; }
        public string Account { get; set; }
        public string MasterName { get; set; }
    }

    public sealed class NativeHeroSaveRequest
    {
        public ushort SaveMode { get; set; }
        public int Param1 { get; set; }
        public int Param2 { get; set; }
        public string MasterName { get; set; }
        public string HeroName { get; set; }
        public NativeHeroRecord Record { get; set; }
        public NativeHeroDynamicData DynamicData { get; set; }
        public byte[] RawDynamicData { get; set; }
    }

    public sealed class NativeHeroCreateRequest
    {
        public ushort HeroType { get; set; }
        public int Code { get; set; }
        public string Account { get; set; }
        public string MasterName { get; set; }
        public string HeroName { get; set; }
    }

    public sealed class NativeHeroCreateResponse
    {
        public ushort HeroType { get; set; }
        public int Result { get; set; }
        public string MasterName { get; set; }
        public string HeroName { get; set; }
    }

    public sealed class NativeHeroDeleteRequest
    {
        public string Account { get; set; }
        public string MasterName { get; set; }
        public string HeroName { get; set; }
    }

    public sealed class NativeHeroDeleteResponse
    {
        public int Result { get; set; }
        public string Account { get; set; }
        public string MasterName { get; set; }
        public string HeroName { get; set; }
    }

    public sealed class NativeHeroRenameRequest
    {
        public ushort SelectionMode { get; set; }
        public int Code { get; set; }
        public string OldHeroName { get; set; }
        public string MasterName { get; set; }
        public string NewHeroName { get; set; }
    }

    public sealed class NativeHeroRenameResponse
    {
        public ushort Result { get; set; }
        public int Code { get; set; }
        public string MasterName { get; set; }
        public string NewHeroName { get; set; }
    }

    public sealed class NativeHeroConsignedListRequest
    {
        public string MasterName { get; set; }
    }

    public sealed class NativeHeroConsignedListEntry
    {
        public string HeroName { get; set; }
        public int HeroType { get; set; }
        public int Job { get; set; }
        public int Level { get; set; }
        public int Sex { get; set; }
    }

    public sealed class NativeHeroConsignedListResponse
    {
        public string MasterName { get; set; }
        public IReadOnlyList<NativeHeroConsignedListEntry> Entries { get; set; }
    }

    public sealed class NativeHeroRestoreConsignedRequest
    {
        public string MasterName { get; set; }
        public string HeroName { get; set; }
    }

    public sealed class NativeHeroRestoreConsignedResponse
    {
        public int Result { get; set; }
        public int HeroType { get; set; }
        public string MasterName { get; set; }
        public string HeroName { get; set; }
    }

    public sealed class NativeHeroBuildThreeSlotRequest
    {
        public string MasterName { get; set; }
    }

    public sealed class NativeHeroBuildThreeSlotResponse
    {
        public ushort Result { get; set; }
        public string MasterName { get; set; }
        public string HeroName { get; set; }
    }

    public sealed class NativeHeroLoadResponse
    {
        public ushort Status { get; set; }
        public string HeroName { get; set; }
        public string MasterName { get; set; }
        public NativeHeroRecord Record { get; set; }
        public NativeHeroDynamicData DynamicData { get; set; }
        public byte[] RawDynamicData { get; set; }
    }
}
