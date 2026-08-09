using System.IO;

namespace SystemModule.Packet
{
    public sealed class LegendClientPacket : Packets
    {
        public int Recog;
        public ushort Ident;
        public ushort Param;
        public ushort Tag;
        public ushort Series;
        public int SessionID;

        public const int PackSize = 16;

        protected override void ReadPacket(BinaryReader reader)
        {
            Recog = reader.ReadInt32();
            Ident = reader.ReadUInt16();
            Param = reader.ReadUInt16();
            Tag = reader.ReadUInt16();
            Series = reader.ReadUInt16();
            SessionID = reader.ReadInt32();
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(Recog);
            writer.Write(Ident);
            writer.Write(Param);
            writer.Write(Tag);
            writer.Write(Series);
            writer.Write(SessionID);
        }
    }
}
