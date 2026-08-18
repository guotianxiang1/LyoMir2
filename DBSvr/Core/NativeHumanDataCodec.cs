using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using SystemModule;

namespace DBSvr.Core
{
    public static class NativeHumanDataCodec
    {
        public const int DataRecordSize = 0xEEF8;
        public const ushort DataSizeMarker = 0xEF00;

        // The persisted blob is NOT reproduced verbatim on the wire.  The native load
        // reply sub_5986CC blits all 0xEF00 bytes (0x598810 rep movsd, ecx=0x3BC0) and
        // then overwrites exactly one word inside them:
        //   0x598819 mov edx,[ebp-8]            ; the account name
        //   0x59881C call 0x5AD608               ; [mgr+0x14] lookup, 0 when absent
        //   0x598824 mov word ptr [rec+0x53C],ax
        // rec is the blob start (frame+0x54), so this is record-proper 0x53C-8 = 0x534.
        //
        // acct+0x1C is now PROVEN to be nothing but a write-through cache OF THIS VERY
        // FIELD, so the record stays the source of truth and carrying it verbatim is
        // right.  There is exactly one writer, sub_5AD648 @0x5AD676
        // (`66 89 50 1c  mov word [eax+0x1c],dx`), it has exactly one caller, and that
        // caller is the SAVE handler sub_59C060 (type-1 0x0150, dispatcher arm
        // 0x598BF7) feeding it the value it just read back out of the record:
        //   0x59C0CF  mov eax,[ebp-0x10] / add eax,8   ; the record body
        //   0x59C0DB  edx = record+0x20 -> call 0x404E5C ; the SS20 account name
        //   0x59C0F3  66 8b 89 34 05 00 00  mov cx,word [record+0x534]
        //   0x59C0FA  call 0x5AD648                     ; cache[account].w1C = cx
        // and the LOAD side reads it straight back through sub_5AD608 (0x5AD62E
        // `mov ax,[eax+0x1c]`, 0 when the account is not cached) into rec+0x53C.
        //
        // The previous note named 0x426436 as a second writer.  It is not one:
        // 0x426436 sits in the Delphi RTL helper sub_4263C0, which walks a dynarray
        // (`mov eax,[eax-4]`, `mov eax,[eax+edx*4]`) and masks a flags word
        // (0x426424 `not edx` / 0x426426 `and dx,[eax+0x1c]`).  Different structure,
        // same offset — a coincidence, not a writer of the account record.
        //
        // Still do not synthesise a value.  The one case where a pure carry differs
        // from native is a load whose account is absent from the cache: native stamps
        // 0 there, a carry keeps the stored word.  It is unobservable on the live data
        // — record[0x534] is 0 in 30/30 golden records — but whether the cache is
        // preloaded at startup or only filled by saves is not established.
        public const int AccountScopedWordOffset = 0x0534;

        public const int ItemRecordSize = 208;
        public const int NativeEquippedItemCount = Grobal2.HUMAN_EQUIPPED_ITEM_COUNT;
        public const int EquippedItemCount = NativeEquippedItemCount;
        public const int BagItemCount = 48;
        public const int StorageItemCount = 192;
        public const int MagicRecordSize = 40;
        public const int MagicCount = 55;
        // rec[0xDE] <-> obj+0xBA5 (LOAD 0x6B01AF->0x6B01B8, SAVE 0x6B1234->0x6B123A).
        // This is where 战神 persists the byte the C# DTO calls btAllowGroup, so the
        // binding stays at 0xDE.  A 2026-08-07 attempt to move it to 0x00D7 was WRONG
        // and has been reverted: rec[0xD7] <-> obj+0xBA4 is the 天地合一 toggle, proven
        // by the GM command that XORs it (sub_622820 @0x623993) printing
        // "设置 [允许天地合一]" / "设置 [拒绝天地合一]" (0x62B8A0 / 0x62B8BC) and by its
        // only gameplay consumer @0x72750C guarding "无法对 %s 使用天地合一"
        // (0x7275A4 / 0x7275B4).  rec[0xD7] therefore stays clone-carried.
        // The RUNTIME group-mode flag m_boAllowGroup lives at obj+0xBA1 (the -4 leg of
        // sub_6C33CC @0x6C33D8) and is NEVER persisted: 0xBA1 has 13 refs, none inside
        // LOAD sub_6AFD7C or SAVE sub_6B0FF0, and login resets it to 0 (0x6B548F).
        public const int AllowGroupFlagOffset = 0x00DE;
        public const int AllowMarryFlagOffset = 0x00D8;
        public const int MarriedFlagOffset = 0x00DB;
        public const int AllowMasterFlagOffset = 0x00D9;
        public const int MasterFlagOffset = 0x00DA;
        public const int StudentFlagOffset = 0x00DC;
        public const int StudentOrderOffset = 0x00DF;
        public const int StudentCountOffset = 0x00E0;
        // NOTE on obj+0xBA5 (== rec[0xDE], see AllowGroupFlagOffset above): besides the
        // codec, two marriage helpers also write it — sub_6C66C4 @0x6C6706 sets it to 1
        // and sub_6C67D4 @0x6C67F5 clears it alongside the spouse-name slot obj+0xC68.
        // So the byte is shared/overloaded at runtime; the codec's job is only to
        // round-trip rec[0xDE] <-> obj+0xBA5 verbatim, which it does.
        // These are the ShortString[15] SLOTS INSIDE the opaque social block
        // (NativeSocialBlockOffset 0x650): +0x00 spouse, +0x10 master, +0x30..
        // +0x70 students — confirmed by the block RE (writes at 0x6C5608/0x6CA9E2
        // spouse, 0x6C58A0 master, array sub_6C59F8 @0x6C5A0C students).  So the
        // offsets themselves are real, BUT TryDecode/TryEncode must NOT touch them
        // as standalone ShortStrings: 战神 only ever block-copies the whole region
        // (it never re-reads these slots on the record itself), and reading 0x680
        // as a length crashed on 30/30 real records.  They remain here because the
        // offline 0x0152 relation paths (NativeHumanLogicalCache, GameSocService)
        // still poke the blob at 0x660/0x650 directly; migrating those to operate
        // on NativeSocialBlob is tracked as a separate, evidence-backed change.
        // Note StudentNameBaseOffset 0x680 == block+0x30 (block base 0x650), so the
        // old "5×0x10 from 0x680" span was right by luck; only the 0x670 companion
        // slot (block+0x20) was unaccounted for, which is where the crash lived.
        public const int DearNameOffset = 0x0650;
        public const int DearNameCapacity = 15;
        public const int MasterNameOffset = 0x0660;
        public const int MasterNameCapacity = 15;
        public const int StudentNameBaseOffset = 0x0680;
        public const int StudentNameStride = 0x10;
        public const int StudentNameCapacity = 15;
        public const int StudentSlotCount = 5;
        public const int CompanionNameOffset = 0x0670;
        public const int CompanionNameCapacity = 15;

        // 2026-08-07: reading ShortStrings at 0x650/0x660/0x680 made TryDecode
        // reject 30/30 real records ("short string length 58 exceeds 15 at
        // 0x680") — every character failed to load.  raw[0x680] is not a length
        // byte at all: it is CONTENT of a ':'/'$' string kept in this region, whose
        // ':' filler (0x3A = 58) lands there.  Encoding was as damaging: the 0x680
        // write cleared 0x680..0x690 and truncated that string.
        //
        // 战神 does NOT parse names out of this region.  It treats the whole social
        // state (spouse / master / companion / 5 students) as one OPAQUE block and
        // block-copies it both ways.  The block base is the INFLATED-record offset
        // 0x650 — pinned by two independent anchors:
        //   * The record base handed to the ShortString helper is arg+8: LOAD
        //     0x6AFDBC `mov eax,[ebp-8]; add eax,8` then CharName<-[+0x106]/
        //     CurMap<-[+0x115] land at inflated 0x00/0x10 (matches golden 30/30).
        //   * The block copy uses the RAW arg (not arg+8): LOAD 0x6B096C
        //     `lea esi,[eax+0x658]` with eax=[ebp-8]=arg, and SAVE 0x6B1687
        //     `mov eax,[ebp-4]; lea edi,[eax+0x658]`.  arg+0x658 == inflated 0x650.
        //   So obj+0xc48 == inflated 0x650, and 0x20 dwords = 128 bytes span
        //   inflated 0x650..0x6CF — which ends EXACTLY at MagicBase 0x6D0 (no
        //   overlap).  MagicBase 0x6D0 is independently proven: 0x6D0 + 55*40 ==
        //   0xF68 == EquippedItemBase, and golden idx10 has a real magic record
        //   (wMagIdx=2, btLevel=3) at 0x6D0.
        // Within the block the RE (staging/social_block_grammar_20260807.md) maps
        // 8 ShortString[15] slots (cl=0xF each): +0x00 spouse, +0x10 master,
        // +0x20 companion, +0x30..+0x70 students[0..4].  Verified against all 30
        // golden records: married rec[0xDB]=0 and student-count rec[0xE0]=0 in
        // 30/30, block slots empty in 30/30; the only non-zero content is an
        // EXTERNALLY-written ':'/'$' string in the companion slot (its producer/
        // parser is NOT in M2Server.exe — no ':'(0x3A)/'$'(0x24) compares exist in
        // CODE — so M2Server only passes it through, and we must too).
        //
        // The block is carried VERBATIM as an opaque DTO member.  We do NOT parse
        // sDearName/sMasterName/sStudentNames from it here yet: the C# consumers
        // read them from the THumInfoData scalar fields, and wiring those from the
        // in-block ShortStrings is a separate, evidence-backed change.  Carrying
        // the block whole is what makes the round-trip byte-exact meanwhile.
        public const int NativeSocialBlockOffset = 0x0650;
        public const int NativeSocialBlockLength = MagicBase - NativeSocialBlockOffset; // 0x80 = 128
        public const int ExchangeBookPersonalRareCountersOffset = 0x0180;
        public const int ExchangeBookPersonalRareCounterCount = 8;

        private const int MagicBase = 0x06D0;
        private const int EquippedItemBase = 0x0F68;
        private const int BagItemBase = 0x2BF6;
        private const int StorageBase = 0x52F6;
        private const int StorageSpaceCountOffset = 0x050E;
        private const int ShengWanOffset = 0x00EC;
        private const int LingFuOffset = 0x00F0;         // obj+0xBD8; bidirectional LOAD/SAVE
        private const int UsedLingFuOffset = 0x00F4;     // obj+0xBDC MyUsedLfNum; bidirectional LOAD/SAVE
        // rec[0xF8] = sub_714334(obj+0x1824) SAVE-only computed snapshot — not a storage field; clone-carry preserves it
        private const int NickLinFuOffset = 0x01CC;      // obj+0x70C NickLinFu; SAVE-only snapshot (native LOAD skips it)
        private const int GoldActNextLevelOffset = 0x01D8;
        private const int FirstUsedGiftStageOffset = 0x01D9;
        private const int ActivePointOffset = 0x0608;
        private const int HeroIntimacyOffset = 0x01E0;
        private const int HeroExperienceAccumulatorOffset = 0x04C8;
        private const int HeroExperienceAccumulatorSize = 24;
        private const int ForceLvOffset = 0x04E0;
        private const int ForceExpOffset = 0x04E4;
        private const int FightPointsOffset = 0x04E8;
        private const int SfLevelOffset = 0x04EC;
        private const int SecHeroPracticeRewardModeOffset = 0x04F0;
        private const int SecHeroPracticeCostTierOffset = 0x04F1;
        private const int SecHeroPracticeLevelOffset = 0x04F2;
        private const int UpgradeFlagsOffset = 0x27;
        private const int BindOffset = 0xB8;
        private const byte KnownUpgradeFlags = 0xC0;
        private const uint ScriptSectionMagic = 0xABCDEFAA;
        public const byte YanshenScriptSectionType = 0x79;

        private static readonly Encoding Gbk = Encoding.GetEncoding(936,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        public static bool LooksLikeNativeDataBlob(byte[] blob)
        {
            if (blob == null || blob.Length < 16) return false;
            var crc = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(0, 4));
            if (crc == 0)
            {
                var size = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(4, 4));
                return size == DataRecordSize
                       || size == DataSizeMarker && blob.Length >= DataSizeMarker;
            }
            return BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(4, 2)) == DataSizeMarker
                   && blob[8] == 0x78;
        }

        public static bool TryDecode(byte[] dataBlob, byte[] scriptBlob,
            out THumDataInfo result, out string error)
        {
            result = null;
            error = string.Empty;
            if (!TryUnwrap(dataBlob, DataRecordSize, DataSizeMarker, out var raw,
                    out var dataCrc, out _, out error))
                return false;
            // 2026-08-04: the "record version" gate that used to live here has been
            // DELETED, not relaxed.  战神 has no version field: rec+0x3E is 发型 (hair).
            // Save  sub_6B0FF0: 0x6B109A `mov al,[ebx+0x70]` / 0x6B109D `mov [esi+0x3E],al`
            // Load  sub_6AFD7C: 0x6AFFBD `mov al,[eax+0x3E]` / 0x6AFFC3 `mov [edx+0x70],al`
            // and the two neighbours are the already-correct Sex/Job pair, proving the
            // Hair/Sex/Job triple is consecutive on BOTH sides:
            //   0x6B10A0 [ebx+0x71] -> [esi+0x3F]   (Sex, C# reads raw[0x3F])
            //   0x6B10A6 [ebx+0x72] -> [esi+0x40]   (Job, C# reads raw[0x40])
            // Confirmed against 30 real records from the live mir3.user_data of
            // D:/光头卧龙 (written by the original Delphi DBServer): after inflating
            // the blob, index.sex matched rec[0x3F] in 30/30 and index.job matched
            // rec[0x40] in 30/30, while rec[0x3B] was 0 in all 30 — i.e. native never
            // writes 0x3B.  Those characters all happen to have hair == 1, so the old
            // gate let THEM through; any character with hair != 1 would have been
            // refused with "unsupported native human record version".  See
            // AuditTools/GoldenSaveFrameCheck and staging/golden_saves_gtwl/.

            var info = new THumDataInfo
            {
                NativeData = (byte[])raw.Clone(),
                NativeDataCrc = dataCrc
            };
            info.Data.Initialization();
            var data = info.Data;
            try
            {
                data.sCharName = ReadShortString(raw, 0x0000, 15);
                data.sCurMap = ReadShortString(raw, 0x0010, 15);
                data.sAccount = ReadShortString(raw, 0x0020, 20);
                // Spouse and master names ARE these two block slots (proven writes
                // at 0x6C5608/0x6CA9E2 spouse -> obj+0xc48, 0x6C58A0 master ->
                // obj+0xc58, with obj+0xc48 == raw 0x650).  They are read with a
                // TOLERANT reader: native never re-parses these slots off the
                // record (it block-copies), so a slot holding something that is not
                // a valid 15-byte ShortString — e.g. the external ':'/'$' string
                // that really occupies the companion slot — must NOT fail the load.
                // The authoritative bytes always survive in NativeSocialBlob.
                data.sDearName = ReadBlockName(raw, DearNameOffset);
                data.sMasterName = ReadBlockName(raw, MasterNameOffset);
                data.boAllowMarry = raw[AllowMarryFlagOffset] != 0;
                data.boMarried = raw[MarriedFlagOffset] != 0;
                data.boAllowMaster = raw[AllowMasterFlagOffset] != 0;
                data.boMaster = raw[MasterFlagOffset] != 0;
                data.boStudent = raw[StudentFlagOffset] != 0;
                data.btStudentOrder = raw[StudentOrderOffset];
                data.btStudentCount = raw[StudentCountOffset];
                // Student names are the last five ShortString[15] slots of the block
                // (block+0x30..+0x70 == raw 0x680 + i*0x10, stride proven by the
                // apprentice array write 0x6C58D5 / empty-slot scan sub_6C59F8).  Read
                // with the SAME tolerant reader as spouse/master: in 30/30 real
                // records the external ':'/'$' companion string overflows its 0x670
                // slot into 0x680 (students[0]) and its ':' byte (0x3A = 58) sat in a
                // length position — that is exactly what crashed the old code.  Since
                // 战神 block-copies and never re-parses these slots, a non-name slot
                // simply yields "" here; the raw bytes still survive in
                // NativeSocialBlob.  The consumer (UsrEngn:3680) only keeps names up
                // to btStudentCount, so an overflowed [0] with count 0 is inert.
                data.sStudentNames = new string[StudentSlotCount];
                for (var i = 0; i < StudentSlotCount; i++)
                    data.sStudentNames[i] = ReadBlockName(raw,
                        StudentNameBaseOffset + i * StudentNameStride);
                data.wCurX = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(0x36, 2));
                data.wCurY = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(0x38, 2));
                data.btDir = raw[0x3A];
                // 战神 sub_6AFD7C @0x6AFFBD `mov al,[eax+0x3E]` -> @0x6AFFC3
                // `mov [edx+0x70],al`.  Hair is 0x3E, NOT 0x3B (0x3B is never read or
                // written anywhere in the native record).  RTTI confirms obj+0x70/0x71/0x72
                // = Hair/Sex/Job, and the 0x3F/0x40 reads just below are the same triple.
                data.btHair = raw[0x3E];
                data.Abil.Level = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(0x3C, 2));
                data.btSex = raw[0x3F];
                data.btJob = raw[0x40];
                data.nGold = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x44, 4));
                // 战神 loads HP/MP as PLAIN dwords with no validation whatsoever:
                //   0x6AFFF3 `mov eax,[eax+0x48]` -> 0x6AFFF9 `mov [edx+0x2ac],eax`
                //   0x6B0002 `mov eax,[eax+0x4c]` -> 0x6B0008 `mov [edx+0x2b4],eax`
                // and SAVE mirrors it (0x6B10BB / 0x6B10C4).  The old
                // ReadNonNegativeAbilityValue guard THREW on a negative value, which
                // made TryDecode reject the whole record and left the character
                // unable to log in — where native would load them without complaint.
                // The guard never fired on the golden corpus (HP 0..2124, MP 9..2158,
                // 0/30 negative), so it was audit-invisible.  Read them raw.
                data.Abil.HP = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x48, 4));
                data.Abil.MP = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0x4C, 4));
                data.Abil.MaxHP = data.Abil.HP;
                data.Abil.MaxMP = data.Abil.MP;
                data.Abil.Exp = unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x50, 4)));
                data.sHomeMap = ReadShortString(raw, 0x00B4, 15);
                data.wHomeX = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(0xC4, 2));
                data.wHomeY = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(0xC6, 2));
                data.btAllowGroup = raw[AllowGroupFlagOffset]; // rec[0xDE] <-> obj+0xBA5
                data.nShengWan = BinaryPrimitives.ReadInt32LittleEndian(
                    raw.AsSpan(ShengWanOffset, 4));
                data.nLingFu = BinaryPrimitives.ReadInt32LittleEndian(
                    raw.AsSpan(LingFuOffset, 4));
                data.nUsedLingFu = BinaryPrimitives.ReadInt32LittleEndian(
                    raw.AsSpan(UsedLingFuOffset, 4));
                for (var i = 0;
                     i < ExchangeBookPersonalRareCounterCount; i++)
                {
                    data.ExchangeBookPersonalRareCounters[i] =
                        BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(
                            ExchangeBookPersonalRareCountersOffset + i * 4, 4));
                }
                data.nNickLinFu = BinaryPrimitives.ReadInt32LittleEndian(
                    raw.AsSpan(NickLinFuOffset, 4));
                data.btGoldActNextLevel = raw[GoldActNextLevelOffset];
                data.btFirstUsedGiftStage = raw[FirstUsedGiftStageOffset];
                data.nActivePoint = BinaryPrimitives.ReadInt32LittleEndian(
                    raw.AsSpan(ActivePointOffset, 4));
                data.NativeHeroIntimacy = BitConverter.Int64BitsToDouble(
                    BinaryPrimitives.ReadInt64LittleEndian(
                        raw.AsSpan(HeroIntimacyOffset, 8)));
                data.NativeHeroExperienceAccumulator = raw.AsSpan(
                    HeroExperienceAccumulatorOffset,
                    HeroExperienceAccumulatorSize).ToArray();
                // Social/marriage/master block, carried VERBATIM.  Native copies
                // it as an opaque block (load 0x6B096C `rep movsd 0x20`,
                // rec+0x658 -> obj+0xc48); it never reads names out of it, so
                // neither do we.  No length byte is validated — a block copy has
                // no length to validate, which is exactly why this can no longer
                // reject a record the original engine wrote.
                data.NativeSocialBlob = raw.AsSpan(NativeSocialBlockOffset,
                    NativeSocialBlockLength).ToArray();
                data.ForceLv = BinaryPrimitives.ReadInt32LittleEndian(
                    raw.AsSpan(ForceLvOffset, 4));
                data.ForceExp = BinaryPrimitives.ReadInt32LittleEndian(
                    raw.AsSpan(ForceExpOffset, 4));
                data.FightPoints = BinaryPrimitives.ReadInt32LittleEndian(
                    raw.AsSpan(FightPointsOffset, 4));
                data.sfLevel = BinaryPrimitives.ReadInt32LittleEndian(
                    raw.AsSpan(SfLevelOffset, 4));
                data.btSecHeroPracticeRewardMode = raw[SecHeroPracticeRewardModeOffset];
                data.btSecHeroPracticeCostTier = raw[SecHeroPracticeCostTierOffset];
                data.wSecHeroPracticeLevel = BinaryPrimitives.ReadUInt16LittleEndian(
                    raw.AsSpan(SecHeroPracticeLevelOffset, 2));
                var storedSpaceCount = BinaryPrimitives.ReadUInt16LittleEndian(
                    raw.AsSpan(StorageSpaceCountOffset, 2));
                // Preserve the persisted word at the DB boundary. The M2 load stage
                // applies its `> 48` runtime-container rule in UserEngine.GetHumData;
                // normalizing here would destroy the original record on a DB round trip.
                data.StorageSpaceCount = storedSpaceCount;

                for (var i = 0; i < MagicCount; i++)
                    data.Magic[i] = DecodeMagic(raw.AsSpan(MagicBase + i * MagicRecordSize, MagicRecordSize));
                for (var i = 0; i < EquippedItemCount; i++)
                    data.HumItems[i] = DecodeItem(raw.AsSpan(
                        EquippedItemBase + i * ItemRecordSize, ItemRecordSize));
                for (var i = 0; i < BagItemCount; i++)
                    data.BagItems[i] = DecodeItem(raw.AsSpan(BagItemBase + i * ItemRecordSize, ItemRecordSize));
                for (var i = 0; i < StorageItemCount; i++)
                    data.StorageItems[i] = DecodeItem(raw.AsSpan(StorageBase + i * ItemRecordSize, ItemRecordSize));
            }
            catch (Exception ex) when (ex is DecoderFallbackException || ex is ArgumentException)
            {
                error = "invalid GBK data in native human record: " + ex.Message;
                return false;
            }

            if (scriptBlob != null && scriptBlob.Length > 0)
            {
                if (!TryUnwrap(scriptBlob, null, null, out var scriptRaw,
                        out var scriptCrc, out _, out error))
                    return false;
                if (!TryParseScript(scriptRaw, data.ScriptV, data.ScriptS,
                        out var sections, out error))
                    return false;
                var yanshenSections = sections.Where(section =>
                    section.Type == YanshenScriptSectionType).ToArray();
                if (yanshenSections.Length > 1)
                {
                    error = "duplicate native eye ScriptData section";
                    return false;
                }
                if (yanshenSections.Length == 1
                    && !YanshenItemSidecarCodec.TryApply(yanshenSections[0].Payload,
                        data.HumItems, data.BagItems, data.StorageItems,
                        clearUnlisted: false, out error))
                {
                    error = "native eye ScriptData: " + error;
                    return false;
                }
                YanshenNativeItemLayout.PackAll(data.HumItems, data.BagItems, data.StorageItems);
                info.NativeScriptData = (byte[])scriptRaw.Clone();
                info.NativeScriptDataCrc = scriptCrc;
            }

            result = info;
            return true;
        }

        public static bool TryDecodeRaw(byte[] rawData, byte[] rawScriptData,
            out THumDataInfo result, out string error)
        {
            result = null;
            error = string.Empty;
            if (rawData == null || rawData.Length != DataRecordSize)
            {
                error = $"native human raw data must be {DataRecordSize} bytes";
                return false;
            }
            if (rawScriptData == null || rawScriptData.Length == 0)
            {
                error = "native human raw ScriptData is empty";
                return false;
            }

            try
            {
                var dataBlob = WrapCompressed(rawData, DataSizeMarker);
                var scriptBlob = rawScriptData.Length <= ushort.MaxValue
                    ? WrapCompressed(rawScriptData, (ushort)rawScriptData.Length)
                    : WrapUncompressed(rawScriptData);
                return TryDecode(dataBlob, scriptBlob, out result, out error);
            }
            catch (InvalidDataException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryEncode(THumDataInfo info, out byte[] dataBlob,
            out byte[] scriptBlob, out string error)
        {
            dataBlob = null;
            scriptBlob = null;
            error = string.Empty;
            if (info?.Data == null)
            {
                error = "human record is null";
                return false;
            }

            var raw = info.NativeData?.Length == DataRecordSize
                ? (byte[])info.NativeData.Clone()
                : new byte[DataRecordSize];
            var data = info.Data;
            if (!YanshenItemSidecarCodec.TryEncode(data.HumItems, data.BagItems,
                    data.StorageItems, out var yanshenPayload, out error))
                return false;
            try
            {
                WriteShortString(raw, 0x0000, 15, data.sCharName);
                WriteShortString(raw, 0x0010, 15, data.sCurMap);
                WriteShortString(raw, 0x0020, 20, data.sAccount);
                // 0x650/0x660/0x680 are NOT written here — those offsets are fork
                // contamination; the social state is restored as one opaque block
                // (NativeSocialBlockOffset) further below.
            }
            catch (EncoderFallbackException ex)
            {
                error = "native human string is not GBK: " + ex.Message;
                return false;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                error = ex.Message;
                return false;
            }

            raw[AllowMarryFlagOffset] = data.boAllowMarry ? (byte)1 : (byte)0;
            raw[MarriedFlagOffset] = data.boMarried ? (byte)1 : (byte)0;
            raw[AllowMasterFlagOffset] = data.boAllowMaster ? (byte)1 : (byte)0;
            raw[MasterFlagOffset] = data.boMaster ? (byte)1 : (byte)0;
            raw[StudentFlagOffset] = data.boStudent ? (byte)1 : (byte)0;
            raw[StudentOrderOffset] = data.btStudentOrder;
            raw[StudentCountOffset] = data.btStudentCount;
            BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(0x36, 2), data.wCurX);
            BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(0x38, 2), data.wCurY);
            raw[0x3A] = data.btDir;
            // 战神 sub_6B0FF0 @0x6B109A `mov al,[ebx+0x70]` -> @0x6B109D
            // `mov [esi+0x3E],al`.  Hair goes to 0x3E, and 0x3B is left untouched —
            // native never writes it.  The neighbours below (0x3F Sex from obj+0x71
            // @0x6B10A0, 0x40 Job from obj+0x72 @0x6B10A6) were already correct, which
            // is what made the odd one out visible.
            raw[0x3E] = data.btHair;
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x3C, 2), data.Abil?.Level ?? 1);
            raw[0x3F] = data.btSex;
            raw[0x40] = data.btJob;
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x44, 4), data.nGold);
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x48, 4), data.Abil?.HP ?? 0);
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0x4C, 4), data.Abil?.MP ?? 0);
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x50, 4),
                unchecked((uint)(data.Abil?.Exp ?? 0)));
            try
            {
                WriteShortString(raw, 0x00B4, 15, data.sHomeMap);
            }
            catch (Exception ex) when (ex is EncoderFallbackException || ex is ArgumentOutOfRangeException)
            {
                error = ex.Message;
                return false;
            }
            BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(0xC4, 2), data.wHomeX);
            BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(0xC6, 2), data.wHomeY);
            raw[AllowGroupFlagOffset] = data.btAllowGroup; // rec[0xDE] <-> obj+0xBA5
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(ShengWanOffset, 4),
                data.nShengWan);
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(LingFuOffset, 4),
                data.nLingFu);
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(UsedLingFuOffset, 4),
                data.nUsedLingFu);
            for (var i = 0; i < ExchangeBookPersonalRareCounterCount; i++)
            {
                var value = data.ExchangeBookPersonalRareCounters != null &&
                            i < data.ExchangeBookPersonalRareCounters.Length
                    ? data.ExchangeBookPersonalRareCounters[i]
                    : 0;
                BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(
                    ExchangeBookPersonalRareCountersOffset + i * 4, 4), value);
            }
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(NickLinFuOffset, 4),
                data.nNickLinFu);
            raw[GoldActNextLevelOffset] = data.btGoldActNextLevel;
            raw[FirstUsedGiftStageOffset] = data.btFirstUsedGiftStage;
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(ActivePointOffset, 4),
                data.nActivePoint);
            BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(HeroIntimacyOffset, 8),
                BitConverter.DoubleToInt64Bits(data.NativeHeroIntimacy));
            if (data.NativeHeroExperienceAccumulator?.Length ==
                HeroExperienceAccumulatorSize)
            {
                data.NativeHeroExperienceAccumulator.AsSpan().CopyTo(raw.AsSpan(
                    HeroExperienceAccumulatorOffset,
                    HeroExperienceAccumulatorSize));
            }
            // Social/marriage/master block restored VERBATIM (save 0x6B1699 does
            // the inverse `rep movsd` of the load).  Same pattern as
            // NativeHeroExperienceAccumulator: exact length or leave the clone
            // bytes alone.  This stops encode from truncating the block the way
            // the old 0x680 ShortString write did.
            if (data.NativeSocialBlob?.Length == NativeSocialBlockLength)
            {
                data.NativeSocialBlob.AsSpan().CopyTo(
                    raw.AsSpan(NativeSocialBlockOffset, NativeSocialBlockLength));
            }
            // ...then re-apply the three NAME slots on top of the restored block.
            // 战神 keeps the relationship FLAG and the relationship NAME in sync
            // because every state change writes the name straight into the block:
            //   marry   0x6C55F5 `mov [ebx+0xb94],1` + 0x6C55FC..0x6C560A assign
            //           obj+0xC48 (== rec 0x650) with cl=0xF
            //   divorce 0x6C52BC `mov [esi+0xb94],0` + 0x6C52C3 `mov [esi+0xc48],0`
            //   master  0x6C58A0 assign obj+0xC58 (== rec 0x660)
            //   student 0x6C58D5 assign obj+0xC78+i*16 (== rec 0x680+i*16)
            // and only THEN block-copies the region out (SAVE 0x6B1699).
            // Restoring the block verbatim and stopping there desynced the two: a
            // marriage formed on this server persisted boMarried=1 with an EMPTY
            // name slot ("married but nameless", so login's name-keyed CheckMarry
            // never ran), and a divorce persisted boMarried=0 while the old spouse
            // name stayed in the slot, which made the next login relink a phantom
            // spouse.  Writing the names here closes both directions.
            // NOTE the writer is WriteBlockName, not WriteShortString: native's
            // sub_4039E4 truncates at 15 and does NOT zero-fill the slot tail.  The
            // tail matters — golden shows a foreign ':'/'$' string at slot 0x670
            // whose data reaches 0x68C in 30/30 records, i.e. it overruns the
            // students[0] slot, and zero-filling would corrupt it.
            WriteBlockName(raw, DearNameOffset, data.sDearName);
            WriteBlockName(raw, MasterNameOffset, data.sMasterName);
            for (var i = 0; i < StudentSlotCount; i++)
            {
                var studentName = data.sStudentNames != null
                                  && i < data.sStudentNames.Length
                    ? data.sStudentNames[i]
                    : string.Empty;
                WriteBlockName(raw, StudentNameBaseOffset + i * StudentNameStride,
                    studentName);
            }
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(ForceLvOffset, 4),
                data.ForceLv);
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(ForceExpOffset, 4),
                data.ForceExp);
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(FightPointsOffset, 4),
                data.FightPoints);
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(SfLevelOffset, 4),
                data.sfLevel);
            raw[SecHeroPracticeRewardModeOffset] = data.btSecHeroPracticeRewardMode;
            raw[SecHeroPracticeCostTierOffset] = data.btSecHeroPracticeCostTier;
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(SecHeroPracticeLevelOffset, 2),
                data.wSecHeroPracticeLevel);
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(StorageSpaceCountOffset, 2),
                unchecked((ushort)data.StorageSpaceCount));

            if (!TryWriteMagic(raw, data.Magic, out error)
                || !TryWriteItems(raw, data.HumItems, EquippedItemBase,
                    EquippedItemCount, "equipment", out error)
                || !TryWriteItems(raw, data.BagItems, BagItemBase, BagItemCount, "bag", out error)
                || !TryWriteItems(raw, data.StorageItems, StorageBase, StorageItemCount, "storage", out error))
                return false;

            try
            {
                dataBlob = WrapCompressed(raw, DataSizeMarker);
            }
            catch (InvalidDataException ex)
            {
                error = ex.Message;
                return false;
            }
            info.NativeData = raw;
            info.NativeDataCrc = BinaryPrimitives.ReadUInt32LittleEndian(dataBlob.AsSpan(0, 4));

            if (info.NativeScriptData != null || (data.ScriptV?.Count ?? 0) > 0
                || (data.ScriptS?.Count ?? 0) > 0 || yanshenPayload.Length > 0)
            {
                if (!TryBuildScript(info.NativeScriptData, data.ScriptV, data.ScriptS,
                        yanshenPayload, out var scriptRaw, out error))
                    return false;
                var keepUncompressed = info.NativeScriptData != null && info.NativeScriptDataCrc == 0;
                if (!keepUncompressed && scriptRaw.Length > ushort.MaxValue)
                {
                    error = "native compressed ScriptData marker exceeds 65535 bytes";
                    return false;
                }
                try
                {
                    scriptBlob = keepUncompressed
                        ? WrapUncompressed(scriptRaw)
                        : WrapCompressed(scriptRaw, (ushort)scriptRaw.Length);
                }
                catch (InvalidDataException ex)
                {
                    error = ex.Message;
                    return false;
                }
                info.NativeScriptData = scriptRaw;
                info.NativeScriptDataCrc = BinaryPrimitives.ReadUInt32LittleEndian(scriptBlob.AsSpan(0, 4));
            }
            return true;
        }

        public static uint ComputeNativeCrc(ReadOnlySpan<byte> data)
        {
            var crc = uint.MaxValue;
            foreach (var value in data)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc;
        }

        /// <summary>
        /// Rewrites only the raw SS20 account field while preserving every other
        /// native human byte. The source may be either of the native envelope
        /// forms accepted by <see cref="TryDecode"/>.
        /// </summary>
        public static bool TryRewriteAccount(byte[] dataBlob,
            byte[] accountBytes, out byte[] rewritten, out string error)
        {
            rewritten = null;
            error = string.Empty;
            accountBytes ??= Array.Empty<byte>();
            if (accountBytes.Length == 0 || accountBytes.Length > 20)
            {
                error = "native human account must fit SS20";
                return false;
            }
            try
            {
                _ = Gbk.GetString(accountBytes);
            }
            catch (DecoderFallbackException ex)
            {
                error = "native human account is not valid GBK: " + ex.Message;
                return false;
            }

            if (!TryUnwrap(dataBlob, DataRecordSize, DataSizeMarker,
                    out var raw, out _, out var uncompressed, out error))
                return false;

            var field = raw.AsSpan(0x20, 21);
            field.Clear();
            field[0] = (byte)accountBytes.Length;
            accountBytes.CopyTo(field.Slice(1));
            if (uncompressed)
            {
                rewritten = (byte[])dataBlob.Clone();
                raw.CopyTo(rewritten, 8);
                return true;
            }

            try
            {
                rewritten = WrapCompressed(raw, DataSizeMarker);
                // Fixed 0xEF00 envelopes are common on the native tool path.
                // Keep their padding width when the updated zlib stream fits.
                if (rewritten.Length < dataBlob.Length)
                    Array.Resize(ref rewritten, dataBlob.Length);
                return true;
            }
            catch (InvalidDataException ex)
            {
                error = ex.Message;
                rewritten = null;
                return false;
            }
        }

        private static bool TryUnwrap(byte[] blob, int? expectedRawLength, ushort? expectedMarker,
            out byte[] raw, out uint crc, out bool uncompressed, out string error)
        {
            raw = null;
            crc = 0;
            uncompressed = false;
            error = string.Empty;
            if (blob == null || blob.Length < 8 || blob.Length % 256 != 0)
            {
                error = "native blob length is not 256-byte aligned";
                return false;
            }

            crc = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(0, 4));
            if (crc == 0)
            {
                uncompressed = true;
                var rawLength = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(4, 4));
                if (expectedRawLength.HasValue && expectedMarker.HasValue
                    && rawLength == expectedMarker.Value
                    && 8L + expectedRawLength.Value <= blob.Length)
                    rawLength = expectedRawLength.Value;
                if (rawLength < 0 || 8L + rawLength > blob.Length)
                {
                    error = "invalid uncompressed native blob length";
                    return false;
                }
                raw = blob.AsSpan(8, rawLength).ToArray();
                if (!PaddingIsZero(blob, 8 + rawLength))
                {
                    error = "nonzero native blob padding";
                    return false;
                }
            }
            else
            {
                var marker = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(4, 2));
                var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(6, 2));
                if (compressedLength < 6 || 8 + compressedLength > blob.Length)
                {
                    error = "invalid native zlib length";
                    return false;
                }
                if (expectedMarker.HasValue && marker != expectedMarker.Value)
                {
                    error = $"native size marker mismatch: {marker} != {expectedMarker.Value}";
                    return false;
                }
                var compressed = blob.AsSpan(8, compressedLength);
                if (ComputeNativeCrc(compressed) != crc)
                {
                    error = "native blob CRC mismatch";
                    return false;
                }
                if (!PaddingIsZero(blob, 8 + compressedLength))
                {
                    error = "nonzero native blob padding";
                    return false;
                }
                try
                {
                    using var input = new MemoryStream(compressed.ToArray(), false);
                    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                    using var output = new MemoryStream(expectedRawLength ?? marker);
                    zlib.CopyTo(output);
                    raw = output.ToArray();
                }
                catch (InvalidDataException ex)
                {
                    error = "invalid native zlib stream: " + ex.Message;
                    return false;
                }
                if (!expectedRawLength.HasValue && marker != raw.Length)
                {
                    error = $"native uncompressed size mismatch: {raw.Length} != {marker}";
                    return false;
                }
            }

            if (expectedRawLength.HasValue && raw.Length != expectedRawLength.Value)
            {
                error = $"native record size mismatch: {raw.Length} != {expectedRawLength.Value}";
                return false;
            }
            return true;
        }

        private static byte[] WrapCompressed(byte[] raw, ushort sizeMarker)
        {
            using var compressedStream = new MemoryStream();
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, true))
                zlib.Write(raw, 0, raw.Length);
            var compressed = compressedStream.ToArray();
            if (compressed.Length > ushort.MaxValue)
                throw new InvalidDataException("native compressed record exceeds 65535 bytes");
            var result = new byte[RoundUp256(8 + compressed.Length)];
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), ComputeNativeCrc(compressed));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4, 2), sizeMarker);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6, 2), (ushort)compressed.Length);
            compressed.CopyTo(result, 8);
            return result;
        }

        private static byte[] WrapUncompressed(byte[] raw)
        {
            var result = new byte[RoundUp256(8 + raw.Length)];
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4, 4), raw.Length);
            raw.CopyTo(result, 8);
            return result;
        }

        private static TUserItem DecodeItem(ReadOnlySpan<byte> record)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(4, 2)) == 0)
                return null;
            var item = new TUserItem
            {
                MakeIndex = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(0, 4)),
                wIndex = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(4, 2)),
                Dura = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(6, 2)),
                DuraMax = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(8, 2)),
                UpgradeFlags = record[UpgradeFlagsOffset],
                Bind = record[BindOffset],
                NativeRecord = record.ToArray()
            };
            record.Slice(10, 14).CopyTo(item.btValue);
            YanshenNativeItemLayout.Unpack(item);
            return item;
        }

        private static TMagicRcd DecodeMagic(ReadOnlySpan<byte> record)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0, 2)) == 0)
                return null;
            return new TMagicRcd
            {
                wMagIdx = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0, 2)),
                btLevel = record[2],
                btKey = 0, // native does NOT persist hotkey here (slot[3] unused); client re-sends on login
                nTranPoint = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(12, 4)), // 天龙 40B magic: training accumulator is slot[12] (was misread as classic slot[4])
                NativeRecord = record.ToArray()
            };
        }

        private static bool TryWriteItems(byte[] raw, TUserItem[] items, int offset,
            int count, string area, out string error)
        {
            error = string.Empty;
            if (items != null)
            {
                for (var i = count; i < items.Length; i++)
                {
                    if (items[i]?.wIndex > 0)
                    {
                        error = $"native {area} capacity is {count}; item at {i} cannot be saved";
                        return false;
                    }
                }
            }
            for (var i = 0; i < count; i++)
            {
                var item = items != null && i < items.Length ? items[i] : null;
                if (!TryEncodeItem(item, raw.AsSpan(offset + i * ItemRecordSize, ItemRecordSize), out error))
                {
                    error = $"{area}[{i}]: {error}";
                    return false;
                }
            }
            return true;
        }

        private static bool TryEncodeItem(TUserItem item, Span<byte> destination, out string error)
        {
            error = string.Empty;
            if (item == null)
            {
                destination.Clear();
                return true;
            }
            if (item.btValue == null || item.btValue.Length != 14)
            {
                error = "invalid 14-byte item value array";
                return false;
            }

            var hasNative = item.NativeRecord?.Length == ItemRecordSize;
            if (hasNative)
                item.NativeRecord.AsSpan().CopyTo(destination);
            else
            {
                destination.Clear();
            }

            var originalUnknownFlags = destination[UpgradeFlagsOffset] & ~KnownUpgradeFlags;
            if ((item.UpgradeFlags & ~KnownUpgradeFlags) != originalUnknownFlags)
            {
                error = "unknown native refine flags changed";
                return false;
            }
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(0, 4), item.MakeIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), item.wIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), item.Dura);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(8, 2), item.DuraMax);
            item.btValue.AsSpan().CopyTo(destination.Slice(10, 14));
            destination[UpgradeFlagsOffset] = item.UpgradeFlags;
            destination[BindOffset] = item.Bind;
            YanshenNativeItemLayout.Pack(item, destination);
            item.NativeRecord = destination.ToArray();
            return true;
        }

        private static bool TryWriteMagic(byte[] raw, TMagicRcd[] magic, out string error)
        {
            error = string.Empty;
            if (magic != null)
            {
                for (var i = MagicCount; i < magic.Length; i++)
                {
                    if (magic[i]?.wMagIdx > 0)
                    {
                        error = $"native magic capacity is {MagicCount}; magic at {i} cannot be saved";
                        return false;
                    }
                }
            }
            for (var i = 0; i < MagicCount; i++)
            {
                var entry = magic != null && i < magic.Length ? magic[i] : null;
                var destination = raw.AsSpan(MagicBase + i * MagicRecordSize, MagicRecordSize);
                if (entry == null)
                {
                    destination.Clear();
                    continue;
                }
                if (entry.NativeRecord?.Length == MagicRecordSize)
                    entry.NativeRecord.AsSpan().CopyTo(destination);
                else
                    destination.Clear();
                BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(0, 2), entry.wMagIdx);
                destination[2] = entry.btLevel;
                // slot[3] not written: native leaves it, hotkey is client-sourced -> clone-preserved
                BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(12, 4), entry.nTranPoint); // nTranPoint -> slot[12]; slot[4-7] & slot[6] clone-preserved
                entry.NativeRecord = destination.ToArray();
            }
            return true;
        }

        private static bool TryParseScript(byte[] raw, Dictionary<int, int> scriptV,
            Dictionary<int, int> scriptS, out List<ScriptSection> sections, out string error)
        {
            sections = new List<ScriptSection>();
            error = string.Empty;
            if (raw == null || raw.Length < 4
                || BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0, 4)) != raw.Length - 4)
            {
                error = "invalid native ScriptData length header";
                return false;
            }
            var offset = 4;
            while (offset < raw.Length)
            {
                if (raw.Length - offset < 7
                    || BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(offset, 4)) != ScriptSectionMagic)
                {
                    error = $"invalid native ScriptData section at {offset}";
                    return false;
                }
                var length = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(offset + 4, 2));
                var type = raw[offset + 6];
                offset += 7;
                if (offset + length > raw.Length)
                {
                    error = "native ScriptData section exceeds record";
                    return false;
                }
                var payload = raw.AsSpan(offset, length).ToArray();
                sections.Add(new ScriptSection(type, payload));
                // Section type 0 is the S bank and type 1 is the V bank, not the
                // other way round. The native decoder sub_6E448C dispatches through
                // the 9-entry table at 0x6E4520 and each arm names its own bank:
                //   type0 arm 0x6E4544: 0x6E457C  05 04 08 00 00        add eax, 0x804
                //                       0x6E459D  8B 90 04 08 00 00     mov edx,[eax+0x804]
                //   type1 arm 0x6E45F7: 0x6E462E  05 08 08 00 00        add eax, 0x808
                //                       0x6E464F  8B 90 08 08 00 00     mov edx,[eax+0x808]
                // and the script API registry pins which offset is which bank:
                //   GetS 0x6DF1CF  8B 93 04 08 00 00  mov edx,[ebx+0x804]
                //   SetS 0x6DF26D  8D 93 04 08 00 00  lea edx,[ebx+0x804]
                //   GetV 0x6DF225  8B 93 08 08 00 00  mov edx,[ebx+0x808]
                //   SetV 0x6DF2CF  8D 93 08 08 00 00  lea edx,[ebx+0x808]
                // A section whose payload is not a whole number of 8-byte pairs is
                // SKIPPED, not fatal.  Both bank arms test it and branch to the logging
                // tail, which then rejoins the section walk:
                //   type0 0x6E4561 f7 7d ec       idiv dword [ebp-0x14]  ; = 8
                //         0x6E4564 85 d2 / 75 4a  test edx,edx / jne 0x6E45B2
                //   type1 0x6E4614 f7 7d ec / 0x6E4617 85 d2 / 75 49 -> 0x6E4664
                //   both tails end 0x6E45F2 / 0x6E46A4  jmp 0x6E48B4 (next section)
                // Returning false here refused the whole record — the character could
                // not log in at all, where 战神 logs one line and loads them fine.
                if ((type == 0 || type == 1) && length % 8 == 0 && length > 0
                    && !DecodeKeyValues(payload, type == 0 ? scriptS : scriptV, out error))
                    return false;
                offset += length;
            }
            return offset == raw.Length;
        }

        private static bool TryBuildScript(byte[] original, Dictionary<int, int> scriptV,
            Dictionary<int, int> scriptS, byte[] yanshenPayload, out byte[] raw,
            out string error)
        {
            raw = null;
            error = string.Empty;
            List<ScriptSection> sections;
            if (original != null)
            {
                var originalV = new Dictionary<int, int>();
                var originalS = new Dictionary<int, int>();
                if (!TryParseScript(original, originalV, originalS, out sections, out error))
                    return false;
                var originalYanshen = sections.Where(section =>
                    section.Type == YanshenScriptSectionType).ToArray();
                if (originalYanshen.Length > 1)
                {
                    error = "duplicate native eye ScriptData section";
                    return false;
                }
                var yanshenEquivalent = yanshenPayload.Length == 0
                    ? originalYanshen.Length == 0
                    : originalYanshen.Length == 1
                      && originalYanshen[0].Payload.AsSpan().SequenceEqual(yanshenPayload);
                if (ScriptValuesEquivalent(originalV, scriptV)
                    && ScriptValuesEquivalent(originalS, scriptS) && yanshenEquivalent)
                {
                    raw = (byte[])original.Clone();
                    return true;
                }
            }
            else
            {
                sections = new List<ScriptSection>();
            }

            // 战神's encoder emits a section ONLY when its byte length is non-zero — the
            // S and V arms are both guarded, and the same shape repeats for types 2/6/7/8:
            //   0x6E4DCC  85 f6 / 7e 2e   test esi,esi / jle 0x6E4DFE   (skip type 0)
            //   0x6E4DD3  c7 00 aa ef cd ab   mov [eax],0xABCDEFAA
            //   0x6E4DDD  c6 40 06 00         mov byte [eax+6],0        ; type 0 = S bank
            //   0x6E4DFE  85 ff / 7e 2e   test edi,edi / jle 0x6E4E30   (skip type 1)
            //   0x6E4E0F  c6 40 06 01         mov byte [eax+6],1        ; type 1 = V bank
            // Writing an empty type-0/type-1 header is therefore a byte the original
            // never produces, and its own decoder treats a zero-length section as an
            // error case (0x6E4553 cmp word [sec+4],0 / 0x6E4558 jbe 0x6E45B2 -> log).
            // So an original M2Server reading a record this codec wrote would log a
            // bogus warning line per empty bank on every single login.
            var foundYanshen = false;
            for (var i = 0; i < sections.Count; i++)
            {
                if (sections[i].Type == 0 || sections[i].Type == 1)
                {
                    var merged = MergeKeyValues(sections[i].Payload,
                        sections[i].Type == 0 ? scriptS : scriptV);
                    if (merged.Length == 0)
                        sections.RemoveAt(i--);
                    else
                        sections[i] = new ScriptSection(sections[i].Type, merged);
                }
                else if (sections[i].Type == YanshenScriptSectionType)
                {
                    if (yanshenPayload.Length > 0)
                    {
                        sections[i] = new ScriptSection(YanshenScriptSectionType, yanshenPayload);
                        foundYanshen = true;
                    }
                    else
                    {
                        sections.RemoveAt(i--);
                    }
                }
            }
            // An empty bank contributes NO section, not a zero-length one. The native
            // encoder sub_6E4CD8 sizes each bank as DynArrayLength*8 and gates both the
            // size accumulation and the emit on it being positive:
            //   S  0x6E4CF8  mov eax,[eax+0x804] / 0x6E4CFE call 0x406A88 (DynArrayLength)
            //      0x6E4D05  C1 E6 03  shl esi,3
            //      0x6E4D08  85 F6 / 7E 07     test esi,esi / jle   -> skip 7+esi
            //      0x6E4DCC  85 F6 / 7E 2E     test esi,esi / jle   -> skip the emit
            //   V  0x6E4D23  C1 E7 03  shl edi,3
            //      0x6E4D26  85 FF / 7E 07     test edi,edi / jle
            //      0x6E4DFE  85 FF / 7E 2E     test edi,edi / jle
            // and the same `jle` shape guards types 2/6/7/8 at 0x6E4D39 / 0x6E4D51 /
            // 0x6E4D69 / 0x6E4D7F.
            //
            // Appending an unconditional pair mattered for the ordinary character who
            // uses one bank and not the other: saving a V variable also wrote a
            // zero-length type-0 header. The original DBServer does not read it back as
            // an empty bank, it treats it as corrupt -- the type-0 arm opens with
            // 0x6E4558 `cmp word [eax+4],0 / 76 58 jbe 0x6E45B2` straight into the
            // log-and-skip branch (type 1 mirrors it at 0x6E4606 `76 57 jbe 0x6E4664`) --
            // so the record stopped being byte-identical to what the original writes.
            //
            // The presence test reads the surviving sections rather than a flag set
            // inside the rewrite loop above, because that loop drops a bank whose merge
            // came out empty; a flag would still claim the section is there.
            var foundS = sections.Exists(section => section.Type == 0);
            var foundV = sections.Exists(section => section.Type == 1);
            if (!foundS)
            {
                var payload = MergeKeyValues(null, scriptS);
                if (payload.Length > 0) sections.Add(new ScriptSection(0, payload));
            }
            if (!foundV)
            {
                var payload = MergeKeyValues(null, scriptV);
                if (payload.Length > 0) sections.Add(new ScriptSection(1, payload));
            }
            if (!foundYanshen && yanshenPayload.Length > 0)
                sections.Add(new ScriptSection(YanshenScriptSectionType, yanshenPayload));

            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);
            writer.Write(0);
            foreach (var section in sections)
            {
                if (section.Payload.Length == 0)
                {
                    continue;
                }
                if (section.Payload.Length > ushort.MaxValue)
                {
                    error = $"native ScriptData type {section.Type} exceeds 65535 bytes";
                    return false;
                }
                writer.Write(ScriptSectionMagic);
                writer.Write((ushort)section.Payload.Length);
                writer.Write(section.Type);
                writer.Write(section.Payload);
            }
            raw = output.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0, 4), raw.Length - 4);
            return true;
        }

        private static bool DecodeKeyValues(byte[] payload, Dictionary<int, int> target,
            out string error)
        {
            error = string.Empty;
            if (payload.Length % 8 != 0)
            {
                error = "native V/S section is not an 8-byte record array";
                return false;
            }
            for (var offset = 0; offset < payload.Length; offset += 8)
            {
                var key = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset, 4));
                var value = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4, 4));
                target[key] = value;
            }
            return true;
        }

        private static byte[] MergeKeyValues(byte[] original, Dictionary<int, int> current)
        {
            // Encoder 0x6E4DE7 / 0x6E4E19  8B 80 xx 08 00 00  mov eax,[eax+0x804/0x808]
            //                       0x6E4DEF / 0x6E4E21  E8 … call 0x403260 (Move)
            // bulk-Moves the CURRENT dynarray (ecx = DynArrayLength*8). Keys that
            // are not in the live array are simply absent, not rewritten as 0 —
            // the next keyed GetV/GetS then returns -1 (0x6E427A), not 0.
            // Merging disk-only keys back as 0 inverted that miss.
            //
            // Keys below 1001 cannot be produced by SetV/SetS: group 0 lives in
            // the inline table and is never persisted (decoder sub_6E448C only
            // touches +0x804/+0x808), and the keyed path requires group>0 so
            // key = group*1000+index >= 1001. Old C# blobs that filed group-0
            // as flat keys 1..100 must not be round-tripped.
            //
            // Emitting ascending is required rather than merely tidy, because native
            // reads the array back with a binary search (sub_6E4270: 0x6E428C lo = 0,
            // 0x6E428E hi = len-1, 0x6E42A2 `cmp edi,[esi+eax*8]`, 0x6E42B3 `jle`
            // descend left, seeded to -1 at 0x6E427A). Ascending is also what a native
            // record always is: the upsert's grow-by-one arm picks an insertion side
            // and memmoves to make room rather than appending —
            //   0x6E41FD  8B 04 D8        mov eax,[eax+ebx*8]   ; key at the landing slot
            //   0x6E4200  3B 45 F8        cmp eax,[ebp-8]       ; vs the new key
            //   0x6E4203  7D 32           jge 0x6E4237
            //   existing <  new -> shift slot ebx+1 to ebx+2 (0x6E4216 / 0x6E421C /
            //                      0x6E4220 Move), store at ebx+1 (0x6E422A, 0x6E4231)
            //   existing >= new -> shift slot ebx to ebx+1 (0x6E4247 / 0x6E424D /
            //                      0x6E4250 Move), store at ebx (0x6E425A, 0x6E4260)
            // Both arms preserve the order. (The ledger carries this as QST-10 BLOCKED,
            // "the element-shift/insert-position logic is NOT in the captured dump" —
            // it is at 0x6E41FB..0x6E4264 and it does keep the array sorted.)
            _ = original;
            current ??= new Dictionary<int, int>();
            var merged = new SortedDictionary<int, int>();
            foreach (var pair in current)
            {
                if (pair.Key < 1001) continue;
                merged[pair.Key] = pair.Value;
            }

            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);
            foreach (var pair in merged)
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value);
            }
            return output.ToArray();
        }

        private static bool ScriptValuesEquivalent(Dictionary<int, int> original,
            Dictionary<int, int> current)
        {
            // A key holding 0 and a key that is absent are different states, so this
            // comparison has to be strict on the key set too. Native's upsert
            // sub_6E4140 has no zero test anywhere - the four value stores
            //   0x6E4187  89 50 04        mov [eax+4], edx
            //   0x6E41C2  89 54 D8 04     mov [eax+ebx*8+4], edx
            //   0x6E4231  89 54 D8 0C     mov [eax+ebx*8+0xC], edx
            //   0x6E4260  89 54 D8 04     mov [eax+ebx*8+4], edx
            // run unconditionally, and the result byte is written once on entry
            // (0x6E4152 mov byte [ebp-9],1) and read once at the single exit
            // (0x6E4264), so the call always reports success. The encoder then
            // bulk-Moves DynArrayLength*8 bytes, zeros included.
            //
            // Treating absent as 0 here let a rebuild be skipped when the only change
            // was a key newly set to 0, so that key never reached the record. A keyed
            // miss reads back as -1 (0x6E427A), not 0, so the value did not merely
            // fail to persist - it came back as a different number.
            current ??= new Dictionary<int, int>();
            if (original.Count != current.Count) return false;
            foreach (var pair in original)
            {
                if (!current.TryGetValue(pair.Key, out var value) || value != pair.Value)
                    return false;
            }
            return true;
        }

        private static string ReadShortString(byte[] raw, int offset, int maxBytes)
        {
            var length = raw[offset];
            if (length > maxBytes)
                throw new ArgumentException($"short string length {length} exceeds {maxBytes} at 0x{offset:X}");
            return Gbk.GetString(raw, offset + 1, length);
        }

        /// <summary>
        /// Reads a name out of ONE ShortString[15] slot inside the opaque social
        /// block: spouse 0x650, master 0x660, students 0x680 + i*0x10.  Those slots
        /// are proven by the block RE (Delphi assign helper cl=0x0F at 0x6C5608
        /// spouse, 0x6C58A0 master, 0x6C58D5 students; empty-slot scan sub_6C59F8
        /// @0x6C5A0C).  战神 NEVER re-parses these slots off the record — it
        /// block-copies the whole 128-byte region both ways — so a slot that does
        /// not hold a valid 15-byte ShortString must NOT fail the load.  The
        /// clearest example is the external ':'/'$' companion string in slot 0x670,
        /// whose ':' filler byte 0x3A (= 58) lands in a length position and made the
        /// old code throw on 30/30 real records.  The authoritative bytes always
        /// survive verbatim in <see cref="THumDataInfo.NativeSocialBlob"/>; this only
        /// DERIVES the display/logic name, returning "" whenever the slot is not a
        /// clean GBK name.  It never throws.
        /// </summary>
        private static string ReadBlockName(byte[] raw, int offset)
        {
            var length = raw[offset];
            // 15 == the block-slot ShortString capacity (DearName/Master/Student all
            // share it; native's assign uses cl=0x0F).  A larger length byte is not a
            // real name — it is block-copied garbage / companion overflow.
            if (length > DearNameCapacity)
                return string.Empty;
            try
            {
                return Gbk.GetString(raw, offset + 1, length);
            }
            catch (DecoderFallbackException)
            {
                return string.Empty;
            }
        }

        private static void WriteShortString(byte[] raw, int offset, int maxBytes, string value)
        {
            var bytes = Gbk.GetBytes(value ?? string.Empty);
            if (bytes.Length > maxBytes)
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"GBK string at 0x{offset:X} exceeds {maxBytes} bytes");
            raw.AsSpan(offset, maxBytes + 1).Clear();
            raw[offset] = (byte)bytes.Length;
            bytes.CopyTo(raw, offset + 1);
        }

        /// <summary>
        /// Writes one social-block name slot exactly the way 战神 does.
        ///
        /// All three block writers go through the Delphi ShortString-assign helper
        /// sub_4039E4 with cl = 0x0F:
        ///   spouse   sub_6C55F5..0x6C560A  `lea eax,[ebx+0xc48]; mov cl,0xF`
        ///   master   0x6C58A0..0x6C58AC    `lea eax,[esi+0xc58]; mov cl,0xF`
        ///   students 0x6C58D5..0x6C58E4    `lea eax,[edi+eax*8+0xc78]; mov cl,0xF`
        ///                                   with eax = i*2, i.e. stride 16
        /// sub_4039E4 itself (0x4039E4):
        ///   `mov bl,[edx]; cmp cl,bl; jbe +2; mov ecx,ebx` -> len = min(srcLen, 15)
        ///   `mov [eax],cl`                                 -> store length byte
        ///   then sub_403260 copies exactly `len` bytes.
        /// So it TRUNCATES at 15 rather than throwing, and it does NOT zero-fill the
        /// unused capacity — unlike <see cref="WriteShortString"/>, which clears
        /// maxBytes+1 first.  Zero-filling here would be a real divergence: the
        /// golden corpus shows an externally-written ':'/'$' string living at slot
        /// 0x670 whose data runs to 0x68C (len 23..28 in 30/30 records), i.e. it
        /// spills across the students[0] slot.  Clearing 16 bytes per slot would
        /// truncate that foreign string; native's assign leaves the tail alone.
        /// </summary>
        private static void WriteBlockName(byte[] raw, int offset, string value)
        {
            // Guard: if the current length byte exceeds DearNameCapacity (15), this
            // position holds a byte written by an external system, not by M2Server's
            // ShortString-assign helper sub_4039E4 (which caps at cl=0x0F).  The
            // canonical example is the companion string at rec[0x670] (len 0x19/0x1A
            // in all 30 golden records) whose data spills into rec[0x680] (= students[0]
            // slot start), leaving a 0x3A (':') content byte there.  Overwriting that
            // byte would corrupt the foreign payload; since M2Server only block-copies
            // the whole 128-byte region it never independently touches this position.
            // In practice this situation only arises when btStudentCount==0 (no real
            // student at slot 0): a live student-assign calls sub_4039E4 with cl=0x0F
            // and leaves a valid len ≤ 15 at that position, which this check would pass.
            if (raw[offset] > DearNameCapacity)
                return;
            var bytes = Gbk.GetBytes(value ?? string.Empty);
            var length = Math.Min(bytes.Length, DearNameCapacity);
            raw[offset] = (byte)length;
            new ReadOnlySpan<byte>(bytes, 0, length).CopyTo(
                raw.AsSpan(offset + 1, length));
        }

        private static bool PaddingIsZero(byte[] blob, int offset)
        {
            for (var i = offset; i < blob.Length; i++)
                if (blob[i] != 0) return false;
            return true;
        }

        private static int RoundUp256(int value) => checked((value + 255) & ~255);

        private readonly struct ScriptSection
        {
            public ScriptSection(byte type, byte[] payload)
            {
                Type = type;
                Payload = payload;
            }

            public byte Type { get; }
            public byte[] Payload { get; }
        }
    }
}
