using SystemModule;

namespace GameSvr
{
    public class TOMagic : Packets
    {
        public ushort wMagicID;
        public byte btEffectType;
        public byte btEffect;
        public ushort wSpell;
        public ushort wPower;
        public byte btTrainLv;
        public byte btJob;
        public int dwDelayTime;
        public byte btDefSpell;
        public byte btDefPower;
        public ushort wMaxPower;
        public byte btDefMaxPower;

        protected override void ReadPacket(BinaryReader reader)
        {
            wMagicID = reader.ReadUInt16();
            btEffectType = reader.ReadByte();
            btEffect = reader.ReadByte();
            wSpell = reader.ReadUInt16();
            wPower = reader.ReadUInt16();
            btTrainLv = reader.ReadByte();
            btJob = reader.ReadByte();
            dwDelayTime = reader.ReadInt32();
            btDefSpell = reader.ReadByte();
            btDefPower = reader.ReadByte();
            wMaxPower = reader.ReadUInt16();
            btDefMaxPower = reader.ReadByte();
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(wMagicID);
            writer.Write(btEffectType);
            writer.Write(btEffect);
            writer.Write(wSpell);
            writer.Write(wPower);
            writer.Write(btTrainLv);
            writer.Write(btJob);
            writer.Write(dwDelayTime);
            writer.Write(btDefSpell);
            writer.Write(btDefPower);
            writer.Write(wMaxPower);
            writer.Write(btDefMaxPower);
        }
    }
}
