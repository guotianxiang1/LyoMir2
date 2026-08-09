using System;
using System.IO;

namespace SystemModule
{
    public class TOStdItem : Packets
    {
        public string Name;
        public byte StdMode;
        public byte Shape;
        public byte Weight;
        public byte AniCount;
        public byte Source;
        public byte Reserved;
        public byte NeedIdentify;
        public ushort Looks;
        public ushort DuraMax;
        public ushort AC;
        public ushort MAC;
        public ushort DC;
        public ushort MC;
        public ushort SC;
        public byte Need;
        public byte NeedLevel;
        public int Price;

        protected override void ReadPacket(BinaryReader reader)
        {
            var nameBuff = reader.ReadBytes(33);
            var nameLen = nameBuff[0];
            Name = HUtil32.GbkEncoding.GetString(nameBuff, 1, nameLen > 32 ? 32 : nameLen);
            StdMode = reader.ReadByte();
            Shape = reader.ReadByte();
            Weight = reader.ReadByte();
            AniCount = reader.ReadByte();
            Source = reader.ReadByte();
            Reserved = reader.ReadByte();
            NeedIdentify = reader.ReadByte();
            Looks = reader.ReadUInt16();
            DuraMax = reader.ReadUInt16();
            AC = reader.ReadUInt16();
            MAC = reader.ReadUInt16();
            DC = reader.ReadUInt16();
            MC = reader.ReadUInt16();
            SC = reader.ReadUInt16();
            Need = reader.ReadByte();
            NeedLevel = reader.ReadByte();
            Price = reader.ReadInt32();
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(Name.ToByte(33));  // length(1) + Name[32]
            writer.Write(StdMode);
            writer.Write(Shape);
            writer.Write(Weight);
            writer.Write(AniCount);
            writer.Write(Source);
            writer.Write(Reserved);
            writer.Write(NeedIdentify);
            writer.Write(Looks);
            writer.Write(DuraMax);
            writer.Write(AC);
            writer.Write(MAC);
            writer.Write(DC);
            writer.Write(MC);
            writer.Write(SC);
            writer.Write(Need);
            writer.Write(NeedLevel);
            writer.Write(Price);
        }
    }
}

