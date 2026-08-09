using System.Buffers.Binary;
using GameSvr.Services;

namespace GameSvr
{
    public partial class TBaseObject
    {
        private const int NativeUnionFlatReductionOffset = 0x154;
        private const int NativeUnionPercentReductionOffset = 0x167;

        internal int m_nNativeUnionFastness;

        internal void ResetNativeUnionFastness()
        {
            m_nNativeUnionFastness = 0;
        }

        internal void AddNativeUnionFastness(int value)
        {
            m_nNativeUnionFastness = unchecked(
                m_nNativeUnionFastness + value);
        }

        internal int ApplyNativeUnionDamageReductions(int damage)
        {
            NativeFastnessTable table = M2Share.NativeFastnessUnionTable;
            if (table != null)
            {
                damage = table.ApplyReduction(damage,
                    m_nNativeUnionFastness);
            }

            ReadOnlySpan<byte> record = GetNativeFixedAbilityRecord();
            if (record.Length <= NativeUnionPercentReductionOffset)
                return damage;

            int flatReduction = BinaryPrimitives.ReadUInt16LittleEndian(
                record.Slice(NativeUnionFlatReductionOffset, sizeof(ushort)));
            damage = unchecked(damage - flatReduction);
            int multiplier = 100 - record[NativeUnionPercentReductionOffset];
            return unchecked(damage * multiplier) / 100;
        }
    }
}
