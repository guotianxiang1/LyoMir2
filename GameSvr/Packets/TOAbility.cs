using SystemModule;

namespace GameSvr
{
    public class TOAbility : Packets
    {
        public ushort Level;
        public ushort AC;
        public ushort MAC;
        public ushort DC;
        public ushort MC;
        public ushort SC;
        public int HP;
        public int MP;
        public int MaxHP;
        public int MaxMP;
        public int dw1AC;
        public int Exp;
        public int MaxExp;
        public ushort Weight;
        public ushort MaxWeight;
        public byte WearWeight;
        public byte MaxWearWeight;
        public byte HandWeight;
        public byte MaxHandWeight;

        protected override void ReadPacket(BinaryReader reader)
        {
            Level = reader.ReadUInt16();
            AC = reader.ReadUInt16();
            MAC = reader.ReadUInt16();
            DC = reader.ReadUInt16();
            MC = reader.ReadUInt16();
            SC = reader.ReadUInt16();
            HP = reader.ReadUInt16();
            MP = reader.ReadUInt16();
            MaxHP = reader.ReadUInt16();
            MaxMP = reader.ReadUInt16();
            dw1AC = reader.ReadInt32();
            Exp = reader.ReadInt32();
            MaxExp = reader.ReadInt32();
            Weight = reader.ReadUInt16();
            MaxWeight = reader.ReadUInt16();
            WearWeight = reader.ReadByte();
            MaxWearWeight = reader.ReadByte();
            HandWeight = reader.ReadByte();
            MaxHandWeight = reader.ReadByte();
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(Level);
            writer.Write(AC);
            writer.Write(MAC);
            writer.Write(DC);
            writer.Write(MC);
            writer.Write(SC);
            writer.Write((ushort)Math.Clamp(HP, 0, ushort.MaxValue));
            writer.Write((ushort)Math.Clamp(MP, 0, ushort.MaxValue));
            writer.Write((ushort)Math.Clamp(MaxHP, 0, ushort.MaxValue));
            writer.Write((ushort)Math.Clamp(MaxMP, 0, ushort.MaxValue));
            writer.Write(dw1AC);
            writer.Write(Exp);
            writer.Write(MaxExp);
            writer.Write(Weight);
            writer.Write(MaxWeight);
            writer.Write(WearWeight);
            writer.Write(MaxWearWeight);
            writer.Write(HandWeight);
            writer.Write(MaxHandWeight);
        }
    }
}
