using System.Buffers.Binary;
using System.Text;
using SystemModule;

namespace GameSvr
{
    public sealed class NativeHeroRuntimeState
    {
        internal NativeHeroRuntimeState(byte[] fixedRecord, NativeHeroDynamicData dynamicData,
            bool[] unknownNormalMagic, bool[] unknownSpecialMagic)
        {
            FixedRecord = fixedRecord;
            DynamicData = dynamicData;
            UnknownNormalMagic = unknownNormalMagic;
            UnknownSpecialMagic = unknownSpecialMagic;
        }

        internal byte[] FixedRecord { get; }
        internal NativeHeroDynamicData DynamicData { get; }
        internal bool[] UnknownNormalMagic { get; }
        internal bool[] UnknownSpecialMagic { get; }

        public NativeHeroRecord Record
        {
            get
            {
                NativeHeroDbFrameCodec.TryCreateRecord(FixedRecord, out var record, out _);
                return record;
            }
        }

        public NativeHeroDynamicData GetDynamicData() => CloneDynamicData(DynamicData);

        internal static NativeHeroDynamicData CloneDynamicData(NativeHeroDynamicData source)
        {
            var sourceSections = source?.Sections ?? Array.Empty<NativeHeroDynamicSection>();
            var sections = new NativeHeroDynamicSection[sourceSections.Count];
            for (var i = 0; i < sourceSections.Count; i++)
                sections[i] = new NativeHeroDynamicSection(sourceSections[i].Type, sourceSections[i].Payload);
            return new NativeHeroDynamicData(sections);
        }
    }

    public static class NativeHeroRuntimeCodec
    {
        private const int UpgradeFlagsOffset = 0x27;
        private const int BindOffset = 0xB8;
        private const byte KnownUpgradeFlags = 0xC0;
        private const ushort SpecialMagicId = 69;
        private static readonly Encoding Gbk;

        static NativeHeroRuntimeCodec()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Gbk = Encoding.GetEncoding(936,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        public static bool TryApply(HeroObject hero, NativeHeroRecord record,
            NativeHeroDynamicData dynamicData, out string error)
        {
            error = string.Empty;
            if (hero == null)
            {
                error = "hero runtime object is null";
                return false;
            }
            if (record == null)
            {
                error = "native hero record is null";
                return false;
            }

            var raw = record.ToArray();
            if (raw.Length != NativeHeroDbFrameCodec.HeroRecordSize)
            {
                error = $"native hero record length must be {NativeHeroDbFrameCodec.HeroRecordSize}";
                return false;
            }
            if (!TryCloneValidatedDynamicData(dynamicData, out var dynamicCopy, out error))
                return false;
            if (record.Hp > int.MaxValue || record.Mp > int.MaxValue)
            {
                error = "native hero HP/MP exceeds the C# ability range";
                return false;
            }
            if (record.Sex > (byte)PlayGender.WoMan)
            {
                error = $"unsupported native hero sex {record.Sex}";
                return false;
            }
            if (record.Job > M2Share.jTaos)
            {
                error = $"unsupported native hero job {record.Job}";
                return false;
            }
            if (record.HeroType < 1 || record.HeroType > 2)
            {
                error = $"unsupported native hero type {record.HeroType}";
                return false;
            }

            var equipped = new TUserItem[NativeHeroDbFrameCodec.EquippedItemCount];
            for (var i = 0; i < equipped.Length; i++)
                equipped[i] = DecodeItem(raw.AsSpan(
                    NativeHeroDbFrameCodec.EquippedItemsOffset
                    + i * NativeHeroDbFrameCodec.ItemRecordSize,
                    NativeHeroDbFrameCodec.ItemRecordSize));

            var bag = new TUserItem[NativeHeroDbFrameCodec.BagItemCount];
            for (var i = 0; i < NativeHeroDbFrameCodec.BagItemCount; i++)
            {
                var item = DecodeItem(raw.AsSpan(
                    NativeHeroDbFrameCodec.BagItemsOffset
                    + i * NativeHeroDbFrameCodec.ItemRecordSize,
                    NativeHeroDbFrameCodec.ItemRecordSize));
                bag[i] = item;
            }

            if (!YanshenHeroDynamicCodec.TryExtract(dynamicCopy, record.Job,
                    out var yanshenPayload, out error))
            {
                error = "native hero eye dynamic data: " + error;
                return false;
            }
            if (yanshenPayload.Length > 0
                && !YanshenItemSidecarCodec.TryApply(yanshenPayload, equipped, bag,
                    Array.Empty<TUserItem>(), clearUnlisted: false, out error))
            {
                error = "native hero eye item data: " + error;
                return false;
            }
            YanshenNativeItemLayout.PackAll(equipped, bag);

            var magic = new List<TUserMagic>(
                NativeHeroDbFrameCodec.NormalMagicCount + NativeHeroDbFrameCodec.SpecialMagicCount);
            var unknownNormal = new bool[NativeHeroDbFrameCodec.NormalMagicCount];
            var unknownSpecial = new bool[NativeHeroDbFrameCodec.SpecialMagicCount];
            DecodeMagicArea(raw, NativeHeroDbFrameCodec.NormalMagicOffset,
                unknownNormal, false, magic);
            DecodeMagicArea(raw, NativeHeroDbFrameCodec.SpecialMagicOffset,
                unknownSpecial, true, magic);

            hero.MasterName = record.MasterName;
            hero.m_sCharName = record.HeroName;
            hero.NativeRace = record.Race;
            hero.m_btRaceImg = record.Race;
            hero.HeroType = record.HeroType;
            hero.HeroRank = record.HeroRank;
            hero.m_nForceExp = record.ForceExp;
            hero.m_nForceLv = record.ForceLv;
            // 英雄模式（0 攻击 / 1 跟随 / 2 休息）是**持久化字段**，不是每次召唤重置成
            // 构造函数默认的 1。原生解码 sub_6888FC：
            //   688A9C  8A 83 9C 00 00 00     mov al,[record+0x9C]   ; ebx = 记录+8 -> 记录 +0xA4
            //   688AA5  88 82 A1 06 00 00     mov [hero+0x6A1],al
            // 原生编码 sub_689034：
            //   68910A  8A 86 A1 06 00 00     mov al,[hero+0x6A1]
            //   689110  88 83 9C 00 00 00     mov [record+0x9C],al
            // 逐字节拷贝、不夹取值域：Run 只做 `cmp byte [+0x6A1],0`（0x68A1F5）和
            // `cmp byte [+0x6A1],2`（0x68A4DA），>2 的脏值在原生等同「非攻击且非休息」。
            hero.m_btNativeHeroMode =
                (HeroObject.NativeHeroMode)raw[NativeHeroDbFrameCodec.HeroModeOffset];
            hero.m_btNativeUnionState = raw[NativeHeroDbFrameCodec.NativeUnionStateOffset];
            hero.m_wNativeUnionEnergy = BinaryPrimitives.ReadUInt16LittleEndian(
                raw.AsSpan(NativeHeroDbFrameCodec.NativeUnionEnergyOffset, 2));
            hero.m_wNativeUnionChargeTier = BinaryPrimitives.ReadUInt16LittleEndian(
                raw.AsSpan(NativeHeroDbFrameCodec.NativeUnionChargeTierOffset, 2));
            hero.m_boNativeCommonInformationOption1 = record.NativeCommonInformationOption1;
            hero.m_nNativeCommonInformationOption2 = record.NativeCommonInformationOption2;
            hero.m_boNativeCommonInformationOption3 = record.NativeCommonInformationOption3;
            hero.InitializeNativeForceState();
            hero.m_btGender = (PlayGender)record.Sex;
            hero.m_btJob = record.Job;
            hero.m_nGold = record.Gold;
            hero.m_Abil.Level = record.Level;
            hero.HeroLevel = record.Level;
            hero.m_Abil.Exp = unchecked((int)record.Exp);
            hero.m_Abil.MaxExp = hero.GetLevelExp(record.Level); // 0x68720E
            hero.m_Abil.HP = 0;
            hero.m_Abil.MP = 0;
            hero.RecalcLevelAbilitys();
            hero.m_Abil.HP = (int)record.Hp;
            hero.m_Abil.MP = (int)record.Mp;
            hero.m_WAbil.CopyFrom(hero.m_Abil);

            hero.m_UseItems = equipped;
            hero.m_ItemList.Clear();
            foreach (var item in bag)
                if (item != null) hero.m_ItemList.Add(item);
            hero.m_MagicList.Clear();
            foreach (var userMagic in magic)
                hero.m_MagicList.Add(userMagic);
            hero.m_HeroMagicList = hero.m_MagicList;
            hero.NativeHeroState = new NativeHeroRuntimeState(raw, dynamicCopy,
                unknownNormal, unknownSpecial);
            hero.RecalcAbilitys();

            // sub_6888FC @0x688EDB..0x688F12 copies record+0x4694 into a
            // standalone 32-byte TSlaveInfo and queues RM_10401 to the hero.
            // The message is deliberately not executed here: the hero is
            // attached to its owner/map only after TryApply returns.
            if (!TryDecodeNativeHeroSlaveRecord(raw, out var slaveInfo, out error))
                return false;
            if (slaveInfo != null)
            {
                hero.SendMsg(hero, Grobal2.RM_10401, 0, 0, 0, 0,
                    string.Empty, slaveInfo);
            }
            return true;
        }

        internal static bool TryDecodeNativeHeroSlaveRecord(
            ReadOnlySpan<byte> fixedRecord, out TSlaveInfo slaveInfo,
            out string error)
        {
            slaveInfo = null;
            error = string.Empty;
            if (fixedRecord.Length != NativeHeroDbFrameCodec.HeroRecordSize)
            {
                error = $"native hero record length must be {NativeHeroDbFrameCodec.HeroRecordSize}";
                return false;
            }

            return NativeSlaveInfoCodec.TryDecode(fixedRecord.Slice(
                    NativeHeroDbFrameCodec.NativeSlaveRecordOffset,
                    NativeHeroDbFrameCodec.NativeSlaveRecordSize),
                out slaveInfo, out error);
        }

        public static bool TryCreateSnapshot(HeroObject hero, out NativeHeroRecord record,
            out NativeHeroDynamicData dynamicData, out string error)
        {
            record = null;
            dynamicData = null;
            error = string.Empty;
            if (hero?.NativeHeroState == null)
            {
                error = "hero has no native runtime state";
                return false;
            }
            if (hero.m_btJob > M2Share.jTaos)
            {
                error = $"unsupported C# hero job {hero.m_btJob}";
                return false;
            }
            if (hero.m_UseItems == null
                || hero.m_UseItems.Length != NativeHeroDbFrameCodec.EquippedItemCount)
            {
                error = $"native hero equipment array must contain exactly {NativeHeroDbFrameCodec.EquippedItemCount} slots";
                return false;
            }
            if (hero.m_ItemList == null || hero.m_ItemList.Count > NativeHeroDbFrameCodec.BagItemCount)
            {
                error = $"native hero bag capacity is {NativeHeroDbFrameCodec.BagItemCount}";
                return false;
            }
            if (hero.m_MagicList == null
                || hero.m_MagicList.Count
                > NativeHeroDbFrameCodec.NormalMagicCount + NativeHeroDbFrameCodec.SpecialMagicCount)
            {
                error = "native hero magic capacity is 58";
                return false;
            }

            var bag = new TUserItem[NativeHeroDbFrameCodec.BagItemCount];
            for (var i = 0; i < hero.m_ItemList.Count; i++)
                bag[i] = hero.m_ItemList[i];
            if (!YanshenItemSidecarCodec.TryEncode(hero.m_UseItems, bag,
                    Array.Empty<TUserItem>(), out var yanshenPayload, out error))
            {
                error = "hero eye item data: " + error;
                return false;
            }

            var raw = (byte[])hero.NativeHeroState.FixedRecord.Clone();
            var nativeSwitchSlave = hero.GetNativeSwitchSlaveForSave();

            // sub_689034 writes into a fresh fixed record. The embedded slot is
            // therefore zero for every ordinary save and for every rejected
            // switch-save arm; cloning the loaded record without this clear would
            // resurrect a stale summon on the next load.
            raw.AsSpan(NativeHeroDbFrameCodec.NativeSlaveRecordOffset,
                NativeHeroDbFrameCodec.NativeSlaveRecordSize).Clear();
            if (nativeSwitchSlave != null
                && !TryWriteNativeSlaveRecord(raw, nativeSwitchSlave, out error))
            {
                return false;
            }
            if (!TryWriteShortString(raw, NativeHeroDbFrameCodec.MasterNameOffset, 15,
                    hero.MasterName, out error)
                || !TryWriteShortString(raw, NativeHeroDbFrameCodec.HeroNameOffset, 15,
                    hero.m_sCharName, out error))
                return false;

            raw[NativeHeroDbFrameCodec.RaceOffset] = hero.m_btRaceImg;
            raw[NativeHeroDbFrameCodec.SexOffset] = (byte)hero.m_btGender;
            raw[NativeHeroDbFrameCodec.JobOffset] = hero.m_btJob;
            raw[NativeHeroDbFrameCodec.HeroTypeOffset] = hero.HeroType;
            raw[NativeHeroDbFrameCodec.HeroRankOffset] = hero.HeroRank;
            BinaryPrimitives.WriteInt32LittleEndian(
                raw.AsSpan(NativeHeroDbFrameCodec.ForceExpOffset, 4), hero.m_nForceExp);
            BinaryPrimitives.WriteInt32LittleEndian(
                raw.AsSpan(NativeHeroDbFrameCodec.ForceLvOffset, 4), hero.m_nForceLv);
            // sub_689034 @0x68910A/0x689110：英雄模式回写记录 +0xA4。
            raw[NativeHeroDbFrameCodec.HeroModeOffset] = (byte)hero.m_btNativeHeroMode;
            raw[NativeHeroDbFrameCodec.NativeUnionStateOffset] = hero.m_btNativeUnionState;
            BinaryPrimitives.WriteUInt16LittleEndian(
                raw.AsSpan(NativeHeroDbFrameCodec.NativeUnionEnergyOffset, 2),
                hero.m_wNativeUnionEnergy);
            BinaryPrimitives.WriteUInt16LittleEndian(
                raw.AsSpan(NativeHeroDbFrameCodec.NativeUnionChargeTierOffset, 2),
                hero.m_wNativeUnionChargeTier);
            raw[NativeHeroDbFrameCodec.NativeCommonInformationOption1Offset] =
                hero.m_boNativeCommonInformationOption1 ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32LittleEndian(
                raw.AsSpan(NativeHeroDbFrameCodec.NativeCommonInformationOption2Offset, 4),
                hero.m_nNativeCommonInformationOption2);
            raw[NativeHeroDbFrameCodec.NativeCommonInformationOption3Offset] =
                hero.m_boNativeCommonInformationOption3 ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(
                raw.AsSpan(NativeHeroDbFrameCodec.LevelOffset, 2), hero.m_Abil.Level);
            BinaryPrimitives.WriteInt32LittleEndian(
                raw.AsSpan(NativeHeroDbFrameCodec.GoldOffset, 4), hero.m_nGold);
            BinaryPrimitives.WriteUInt32LittleEndian(
                raw.AsSpan(NativeHeroDbFrameCodec.ExpOffset, 4), unchecked((uint)hero.m_Abil.Exp));
            WriteSplitUInt32(raw, NativeHeroDbFrameCodec.HpLowOffset,
                NativeHeroDbFrameCodec.HpHighOffset, (uint)Math.Max(0, hero.m_WAbil.HP));
            WriteSplitUInt32(raw, NativeHeroDbFrameCodec.MpLowOffset,
                NativeHeroDbFrameCodec.MpHighOffset, (uint)Math.Max(0, hero.m_WAbil.MP));
            BinaryPrimitives.WriteInt32LittleEndian(
                raw.AsSpan(NativeHeroDbFrameCodec.CurrentXOffset, 4), hero.m_nCurrX);
            BinaryPrimitives.WriteInt32LittleEndian(
                raw.AsSpan(NativeHeroDbFrameCodec.CurrentYOffset, 4), hero.m_nCurrY);

            for (var i = 0; i < NativeHeroDbFrameCodec.EquippedItemCount; i++)
            {
                var destination = raw.AsSpan(
                    NativeHeroDbFrameCodec.EquippedItemsOffset
                    + i * NativeHeroDbFrameCodec.ItemRecordSize,
                    NativeHeroDbFrameCodec.ItemRecordSize);
                var item = hero.m_UseItems[i];
                if (!IsActiveItem(item))
                {
                    if (BinaryPrimitives.ReadUInt16LittleEndian(destination.Slice(4, 2)) != 0)
                        destination.Clear();
                    continue;
                }
                if (!TryEncodeItem(item, destination, out error))
                {
                    error = $"equipment[{i}]: {error}";
                    return false;
                }
            }

            for (var i = 0; i < NativeHeroDbFrameCodec.BagItemCount; i++)
            {
                var destination = raw.AsSpan(
                    NativeHeroDbFrameCodec.BagItemsOffset
                    + i * NativeHeroDbFrameCodec.ItemRecordSize,
                    NativeHeroDbFrameCodec.ItemRecordSize);
                if (i >= hero.m_ItemList.Count)
                {
                    if (BinaryPrimitives.ReadUInt16LittleEndian(destination.Slice(4, 2)) != 0)
                        destination.Clear();
                    continue;
                }
                var item = bag[i];
                if (!IsActiveItem(item))
                {
                    error = $"bag[{i}] is empty";
                    return false;
                }
                if (!TryEncodeItem(item, destination, out error))
                {
                    error = $"bag[{i}]: {error}";
                    return false;
                }
            }

            if (!TryWriteMagic(raw, hero.m_MagicList, hero.NativeHeroState, out error))
                return false;
            if (!NativeHeroDbFrameCodec.TryCreateRecord(raw, out record, out error))
                return false;
            if (!YanshenHeroDynamicCodec.TryMerge(hero.NativeHeroState.DynamicData,
                    hero.m_btJob, yanshenPayload, out dynamicData, out error))
            {
                error = "hero eye dynamic data: " + error;
                record = null;
                return false;
            }

            return true;
        }

        public static bool TryRename(HeroObject hero, string newHeroName, out string error)
        {
            error = string.Empty;
            if (hero?.NativeHeroState == null)
            {
                error = "hero has no native runtime state";
                return false;
            }
            if (!NativeHeroDbFrameCodec.TryRenameRecord(
                    hero.NativeHeroState.FixedRecord, newHeroName, out var renamed, out error))
                return false;

            renamed.CopyTo(hero.NativeHeroState.FixedRecord, 0);
            hero.m_sCharName = newHeroName;
            return true;
        }

        private static TUserItem DecodeItem(ReadOnlySpan<byte> source)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4, 2)) == 0)
                return null;
            var item = new TUserItem
            {
                MakeIndex = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(0, 4)),
                wIndex = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4, 2)),
                Dura = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(6, 2)),
                DuraMax = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(8, 2)),
                UpgradeFlags = source[UpgradeFlagsOffset],
                Bind = source[BindOffset],
                NativeRecord = source.ToArray()
            };
            source.Slice(10, 14).CopyTo(item.btValue);
            YanshenNativeItemLayout.Unpack(item);
            NativeSpecialDropItemRollCore.HydrateConstructorState(item);
            return item;
        }

        private static bool TryEncodeItem(TUserItem item, Span<byte> destination, out string error)
        {
            error = string.Empty;
            if (item.btValue == null || item.btValue.Length != 14)
            {
                error = "invalid 14-byte item value array";
                return false;
            }
            if (item.NativeRecord != null && item.NativeRecord.Length != NativeHeroDbFrameCodec.ItemRecordSize)
            {
                error = $"native item record must be {NativeHeroDbFrameCodec.ItemRecordSize} bytes";
                return false;
            }
            if (item.NativeRecord != null)
                item.NativeRecord.AsSpan().CopyTo(destination);
            else
                destination.Clear();

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

        private static void DecodeMagicArea(byte[] raw, int offset, bool[] unknown,
            bool specialArea, List<TUserMagic> destination)
        {
            for (var i = 0; i < unknown.Length; i++)
            {
                var source = raw.AsSpan(offset + i * NativeHeroDbFrameCodec.MagicRecordSize,
                    NativeHeroDbFrameCodec.MagicRecordSize);
                var magicId = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(0, 2));
                if (magicId == 0)
                    continue;
                var classificationMatches = specialArea == (magicId == SpecialMagicId);
                var magicInfo = classificationMatches
                    ? M2Share.UserEngine?.FindHeroMagic(magicId)
                    : null;
                if (magicInfo == null)
                {
                    unknown[i] = true;
                    continue;
                }
                destination.Add(new TUserMagic
                {
                    MagicInfo = magicInfo,
                    wMagIdx = magicId,
                    btLevel = source[2],
                    btKey = 0, // slot[3] not persisted by native (hotkey client-sourced on login)
                    nTranPoint = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(12, 4)), // 天龙 40B magic: nTranPoint at slot[12]
                    NativeRecord = source.ToArray()
                });
            }
        }

        private static bool TryWriteMagic(byte[] raw, IList<TUserMagic> magic,
            NativeHeroRuntimeState state, out string error)
        {
            error = string.Empty;
            var normal = new List<TUserMagic>();
            var special = new List<TUserMagic>();
            for (var i = 0; i < magic.Count; i++)
            {
                var entry = magic[i];
                if (entry == null || entry.wMagIdx == 0)
                {
                    error = $"magic[{i}] is empty";
                    return false;
                }
                (entry.wMagIdx == SpecialMagicId ? special : normal).Add(entry);
            }

            var normalCapacity = NativeHeroDbFrameCodec.NormalMagicCount
                                 - CountTrue(state.UnknownNormalMagic);
            var specialCapacity = NativeHeroDbFrameCodec.SpecialMagicCount
                                  - CountTrue(state.UnknownSpecialMagic);
            if (normal.Count > normalCapacity || special.Count > specialCapacity)
            {
                error = $"native hero magic capacity exceeded: normal={normal.Count}/{normalCapacity}, special={special.Count}/{specialCapacity}";
                return false;
            }

            if (!TryWriteMagicArea(raw, NativeHeroDbFrameCodec.NormalMagicOffset,
                    state.UnknownNormalMagic, normal, out error))
                return false;
            return TryWriteMagicArea(raw, NativeHeroDbFrameCodec.SpecialMagicOffset,
                state.UnknownSpecialMagic, special, out error);
        }

        private static bool TryWriteMagicArea(byte[] raw, int offset, bool[] unknown,
            List<TUserMagic> magic, out string error)
        {
            error = string.Empty;
            for (var i = 0; i < unknown.Length; i++)
            {
                if (!unknown[i])
                    raw.AsSpan(offset + i * NativeHeroDbFrameCodec.MagicRecordSize,
                        NativeHeroDbFrameCodec.MagicRecordSize).Clear();
            }

            var sourceIndex = 0;
            for (var i = 0; i < unknown.Length && sourceIndex < magic.Count; i++)
            {
                if (unknown[i])
                    continue;
                var entry = magic[sourceIndex++];
                var target = raw.AsSpan(offset + i * NativeHeroDbFrameCodec.MagicRecordSize,
                    NativeHeroDbFrameCodec.MagicRecordSize);
                if (entry.NativeRecord != null
                    && entry.NativeRecord.Length != NativeHeroDbFrameCodec.MagicRecordSize)
                {
                    error = $"native magic record must be {NativeHeroDbFrameCodec.MagicRecordSize} bytes";
                    return false;
                }
                if (entry.NativeRecord != null)
                    entry.NativeRecord.AsSpan().CopyTo(target);
                BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(0, 2), entry.wMagIdx);
                target[2] = entry.btLevel;
                // slot[3] not written: native leaves it, hotkey client-sourced -> clone-preserved
                BinaryPrimitives.WriteInt32LittleEndian(target.Slice(12, 4), entry.nTranPoint); // nTranPoint -> slot[12]; slot[6] clone-preserved
            }
            return true;
        }

        private static bool TryCloneValidatedDynamicData(NativeHeroDynamicData source,
            out NativeHeroDynamicData result, out string error)
        {
            source ??= new NativeHeroDynamicData(Array.Empty<NativeHeroDynamicSection>());
            if (!NativeHeroDbFrameCodec.TryEncodeDynamicData(source, out var encoded, out error)
                || !NativeHeroDbFrameCodec.TryDecodeDynamicData(encoded, out result, out error))
            {
                result = null;
                return false;
            }
            return true;
        }

        private static bool TryWriteNativeSlaveRecord(byte[] destination,
            TBaseObject slave, out string error)
        {
            var offset = NativeHeroDbFrameCodec.NativeSlaveRecordOffset;
            return NativeSlaveInfoCodec.TryEncode(destination.AsSpan(offset,
                    NativeHeroDbFrameCodec.NativeSlaveRecordSize),
                slave, HUtil32.GetTickCount(), out error);
        }

        private static bool TryWriteShortString(byte[] destination, int offset, int maximumLength,
            string value, out string error)
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
            destination.AsSpan(offset, maximumLength + 1).Clear();
            destination[offset] = (byte)bytes.Length;
            bytes.CopyTo(destination, offset + 1);
            return true;
        }

        private static void WriteSplitUInt32(byte[] destination, int lowOffset, int highOffset,
            uint value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(lowOffset, 2),
                (ushort)value);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(highOffset, 2),
                (ushort)(value >> 16));
        }

        private static bool IsActiveItem(TUserItem item) => item != null && item.wIndex != 0;

        private static int CountTrue(bool[] values)
        {
            var count = 0;
            foreach (var value in values)
                if (value) count++;
            return count;
        }

    }
}
