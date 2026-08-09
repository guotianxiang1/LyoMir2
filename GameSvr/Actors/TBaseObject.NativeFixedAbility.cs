using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        private const int NativeFixedAbilityMinimumSize = 0x22E;

        internal struct NativeCoreWorkingAbility
        {
            internal int MaxHP;
            internal int MaxMP;
            internal int HitPoint;
            internal int SpeedPoint;
            internal int ACLow;
            internal int ACHigh;
            internal int MACLow;
            internal int MACHigh;
            internal int DCLow;
            internal int DCHigh;
            internal int MCLow;
            internal int MCHigh;
            internal int SCLow;
            internal int SCHigh;
            internal int CCLow;
            internal int CCHigh;
        }

        internal NativeCoreWorkingAbility m_NativeCoreWorkingAbility;

        protected virtual ReadOnlySpan<byte> GetNativeFixedAbilityRecord() =>
            ReadOnlySpan<byte>.Empty;

        private void SeedNativeFixedAbility(ref TAddAbility addAbility)
        {
            m_NativeCoreWorkingAbility = default;
            if (m_btJob == 3)
            {
                int mainAbility = m_Abil.Level / 5;
                m_NativeCoreWorkingAbility.HitPoint = 15;
                m_NativeCoreWorkingAbility.SpeedPoint = 23;
                m_NativeCoreWorkingAbility.CCLow = Math.Max(mainAbility - 1, 1);
                m_NativeCoreWorkingAbility.CCHigh = Math.Max(mainAbility, 1);
            }
            NativeMagicLevelBonus = 0;
            ReadOnlySpan<byte> record = GetNativeFixedAbilityRecord();
            if (record.Length < NativeFixedAbilityMinimumSize)
                return;

            NativeMagicLevelBonus = record[0x138];
            m_NativeCoreWorkingAbility.MaxHP = ReadNativeFixedInt32(record, 0x48);
            m_NativeCoreWorkingAbility.MaxMP = ReadNativeFixedInt32(record, 0x4C);
            m_NativeCoreWorkingAbility.HitPoint = unchecked(
                m_NativeCoreWorkingAbility.HitPoint +
                ReadNativeFixedUInt16(record, 0x50));
            m_NativeCoreWorkingAbility.SpeedPoint = unchecked(
                m_NativeCoreWorkingAbility.SpeedPoint +
                ReadNativeFixedUInt16(record, 0x52));
            m_NativeCoreWorkingAbility.ACLow = ReadNativeFixedInt32(record, 0x54);
            m_NativeCoreWorkingAbility.ACHigh = ReadNativeFixedInt32(record, 0x58);
            m_NativeCoreWorkingAbility.MACLow = ReadNativeFixedInt32(record, 0x5C);
            m_NativeCoreWorkingAbility.MACHigh = ReadNativeFixedInt32(record, 0x60);
            m_NativeCoreWorkingAbility.DCLow = ReadNativeFixedInt32(record, 0x64);
            m_NativeCoreWorkingAbility.DCHigh = ReadNativeFixedInt32(record, 0x68);
            m_NativeCoreWorkingAbility.MCLow = ReadNativeFixedInt32(record, 0x6C);
            m_NativeCoreWorkingAbility.MCHigh = ReadNativeFixedInt32(record, 0x70);
            m_NativeCoreWorkingAbility.SCLow = ReadNativeFixedInt32(record, 0x74);
            m_NativeCoreWorkingAbility.SCHigh = ReadNativeFixedInt32(record, 0x78);
            m_NativeCoreWorkingAbility.CCLow = unchecked(
                m_NativeCoreWorkingAbility.CCLow +
                ReadNativeFixedInt32(record, 0x7C));
            m_NativeCoreWorkingAbility.CCHigh = unchecked(
                m_NativeCoreWorkingAbility.CCHigh +
                ReadNativeFixedInt32(record, 0x80));

            if (this is TPlayObject && record.Length >=
                TPlayObject.NativeSubmitBallQuestJob3Offset + sizeof(int))
            {
                m_NativeCoreWorkingAbility.DCHigh = unchecked(
                    m_NativeCoreWorkingAbility.DCHigh +
                    (sbyte)record[TPlayObject.NativeSubmitBallQuestJob012Offset]);
                m_NativeCoreWorkingAbility.MCHigh = unchecked(
                    m_NativeCoreWorkingAbility.MCHigh +
                    (sbyte)record[TPlayObject.NativeSubmitBallQuestJob012Offset + 1]);
                m_NativeCoreWorkingAbility.SCHigh = unchecked(
                    m_NativeCoreWorkingAbility.SCHigh +
                    (sbyte)record[TPlayObject.NativeSubmitBallQuestJob012Offset + 2]);
                m_NativeCoreWorkingAbility.CCHigh = unchecked(
                    m_NativeCoreWorkingAbility.CCHigh +
                    BinaryPrimitives.ReadInt32LittleEndian(record.Slice(
                        TPlayObject.NativeSubmitBallQuestJob3Offset, sizeof(int))));
            }

            addAbility.wAntiPoison = BinaryPrimitives.ReadUInt16LittleEndian(
                record.Slice(0x84, sizeof(ushort)));
            addAbility.wPoisonRecover = ReadNativeFixedUInt16(record, 0x86);
            addAbility.wHealthRecover = ReadNativeFixedUInt16(record, 0x88);
            addAbility.wSpellRecover = ReadNativeFixedUInt16(record, 0x8A);
            addAbility.wAntiMagic = ReadNativeFixedUInt16(record, 0x8C);
            addAbility.btLuck = record[0x8E];
            addAbility.btUnLuck = record[0x8F];
            addAbility.nHitSpeed = ReadNativeFixedUInt16(record, 0x94);
            addAbility.NativeMagicHitHealAmount =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(0xA8, sizeof(ushort)));
            addAbility.NativeMagicHitHealChance =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(0xAA, sizeof(ushort)));
            addAbility.NativeHumanMagicPercentReductionRaw =
                ReadNativeFixedInt32(record, 0xA0);
            addAbility.NativeBreakPower =
                ReadNativeFixedUInt16(record, 0xBA);
            addAbility.NativeCrazyPower =
                ReadNativeFixedUInt16(record, 0xCE);
            addAbility.wEffectStrength = BinaryPrimitives.ReadUInt16LittleEndian(
                record.Slice(0xFE, sizeof(ushort)));
            addAbility.NativeBaseMagicDamagePercent =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(0x100, sizeof(ushort)));
            addAbility.NativeDrugHealthBonus =
                ReadNativeFixedUInt16(record, 0x140);
            addAbility.NativeDrugSpellBonus =
                ReadNativeFixedUInt16(record, 0x142);
            addAbility.NativeDrugJobBonus =
                ReadNativeFixedUInt16(record, 0x144);
            addAbility.NativeUnionFastnessSelector =
                ReadNativeFixedUInt16(record, 0x146);
            addAbility.NativeHqFastnessSelector =
                ReadNativeFixedUInt16(record, 0x148);
            addAbility.NativeNearHitFastnessSelector =
                ReadNativeFixedUInt16(record, 0x14A);
            addAbility.NativeState26DeadlineBonus =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(0x160, sizeof(ushort)));
            addAbility.NativeBreakThroughChance =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(0x118, sizeof(ushort)));
            addAbility.NativeSteelBodyReduction =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(0x13A, sizeof(ushort)));
            addAbility.NativeAwakening = record[0x226] != 0;
            addAbility.NativeFlatMagicDamageIncrease =
                BinaryPrimitives.ReadInt32LittleEndian(
                    record.Slice(0x11C, sizeof(int)));
            addAbility.NativeGoldenBellReduction =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(0x14E, sizeof(ushort)));
            addAbility.NativeDragonBodyReduction =
                BinaryPrimitives.ReadInt32LittleEndian(
                    record.Slice(0x150, sizeof(int)));
            addAbility.NativeDamageIncreasePercent = record[0x166];
            addAbility.NativeCriticalChance =
                BinaryPrimitives.ReadInt32LittleEndian(
                    record.Slice(0x16C, sizeof(int)));
            addAbility.NativeCriticalDamageIncrease =
                BinaryPrimitives.ReadInt32LittleEndian(
                    record.Slice(0x170, sizeof(int)));
            addAbility.NativeAntiCriticalChance =
                BinaryPrimitives.ReadInt32LittleEndian(
                    record.Slice(0x174, sizeof(int)));
            addAbility.NativeCriticalDamageReduction =
                BinaryPrimitives.ReadInt32LittleEndian(
                    record.Slice(0x178, sizeof(int)));
            addAbility.NativeMagicFastnessSelector =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(0x17C, sizeof(ushort)));
            addAbility.NativeSoulFastnessSelector =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(0x17E, sizeof(ushort)));
            addAbility.NativeMagicDamageReductionPercent = record[0x1DA];
            addAbility.NativeType74MagicHit =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    record.Slice(0x1F6, sizeof(ushort)));
            addAbility.NativeState26DirectStrong = record[0x1FC] != 0;
            addAbility.NativeState26DirectWeak = record[0x1FD] != 0;
            addAbility.NativeState26SingleStrong = record[0x1FE] != 0;
            addAbility.NativeState26SingleWeak = record[0x1FF] != 0;
            addAbility.NativeStandardMagicShield = record[0x202] != 0;
            addAbility.NativeHalfMagicShield = record[0x203] != 0;
            addAbility.NativeUserMove = record[0x204] != 0;
            addAbility.NativeSearchHuman = record[0x20B] != 0;
            addAbility.NativeDragonPossessionLevel = record[0x218];
            addAbility.NativeFullMagicShield = record[0x21B] != 0;
        }

        private static ushort ReadNativeFixedUInt16(ReadOnlySpan<byte> record,
            int offset) => BinaryPrimitives.ReadUInt16LittleEndian(
                record.Slice(offset, sizeof(ushort)));

        private static int ReadNativeFixedInt32(ReadOnlySpan<byte> record,
            int offset) => BinaryPrimitives.ReadInt32LittleEndian(
                record.Slice(offset, sizeof(int)));
    }
}
