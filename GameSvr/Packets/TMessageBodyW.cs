using SystemModule;

namespace GameSvr
{
    public class TMessageBodyW : Packets
    {
        public const int RecordSize = 8;

        public ushort Param1;
        public ushort Param2;
        public ushort Tag1;
        public ushort Tag2;

        protected override void ReadPacket(BinaryReader reader)
        {
            Param1 = reader.ReadUInt16();
            Param2 = reader.ReadUInt16();
            Tag1 = reader.ReadUInt16();
            Tag2 = reader.ReadUInt16();
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(Param1);
            writer.Write(Param2);
            writer.Write(Tag1);
            writer.Write(Tag2);
        }
    }
}
