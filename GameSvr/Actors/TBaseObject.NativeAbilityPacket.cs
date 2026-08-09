using System.IO;
using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        protected byte[] BuildNativeAbilityPacket()
        {
            var body = new byte[184];
            var ability = m_WAbil;
            using var stream = new MemoryStream(body);
            using var writer = new BinaryWriter(stream);

            writer.Write(ability.Level);
            writer.Write((ushort)m_btHitPoint);
            writer.Write(m_wSpeedPoint);
            writer.Write(m_nHealthRecover);
            writer.Write(m_nSpellRecover);
            writer.Write(m_wEffectResistance);
            writer.Write(m_nPoisonRecover);
            writer.Write(m_nAntiMagic);
            writer.Write(m_wNativeType74MagicHit);
            writer.Write(m_nHitSpeed);
            writer.Write((byte)(ability.Level >> 8));
            writer.Write(new byte[3]);

            writer.Write((int)HUtil32.LoWord(ability.AC));
            writer.Write((int)HUtil32.HiWord(ability.AC));
            writer.Write((int)HUtil32.LoWord(ability.MAC));
            writer.Write((int)HUtil32.HiWord(ability.MAC));
            writer.Write((int)HUtil32.LoWord(ability.DC));
            writer.Write((int)HUtil32.HiWord(ability.DC));
            writer.Write((int)HUtil32.LoWord(ability.MC));
            writer.Write((int)HUtil32.HiWord(ability.MC));
            writer.Write((int)HUtil32.LoWord(ability.SC));
            writer.Write((int)HUtil32.HiWord(ability.SC));
            writer.Write(ability.HP);
            writer.Write(ability.MaxHP);
            writer.Write(ability.MP);
            writer.Write(ability.MaxMP);
            writer.Write(ability.Exp);
            writer.Write(ability.MaxExp);
            writer.Write((int)ability.Weight);
            writer.Write((int)ability.MaxWeight);
            writer.Write((int)ability.WearWeight);
            writer.Write((int)ability.MaxWearWeight);
            writer.Write((int)ability.HandWeight);
            writer.Write((int)ability.MaxHandWeight);

            writer.BaseStream.Position = 0x80;
            writer.Write((uint)m_wEffectStrength);

            writer.BaseStream.Position = 0xA0;
            writer.Write(m_wNativeDrugSpellBonus);
            writer.Write(m_wNativeDrugHealthBonus);
            writer.Write(m_wNativeDrugJobBonus);

            writer.BaseStream.Position = 0xA8;
            writer.Write(unchecked((ushort)m_nNativeUnionFastness));

            writer.BaseStream.Position = 0xAA;
            writer.Write(unchecked((ushort)m_nNativeNearHitFastness));

            writer.BaseStream.Position = 0xB0;
            writer.Write(m_NativeCoreWorkingAbility.CCLow);
            writer.Write(m_NativeCoreWorkingAbility.CCHigh);
            return body;
        }
    }
}
