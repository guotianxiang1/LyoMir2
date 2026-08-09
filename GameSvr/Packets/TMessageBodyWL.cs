using SystemModule;

namespace GameSvr
{
    public class TMessageBodyWL : Packets
    {
        public const int RecordSize = 16;

        public int lParam1;
        public int lParam2;
        public int lTag1;
        public int lTag2;

        protected override void ReadPacket(BinaryReader reader)
        {
            lParam1 = reader.ReadInt32();
            lParam2 = reader.ReadInt32();
            lTag1 = reader.ReadInt32();
            lTag2 = reader.ReadInt32();
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(lParam1);
            writer.Write(lParam2);
            writer.Write(lTag1);
            writer.Write(lTag2);
        }
    }
}
