using SystemModule;

namespace GameSvr
{
    public class TOClientItem : Packets
    {
        public TOStdItem Item;
        public int MakeIndex;
        public ushort Dura;
        public ushort DuraMax;

        public TOClientItem()
        {
            Item = new TOStdItem();
        }

        protected override void ReadPacket(BinaryReader reader)
        {
            Item = Packets.ToPacket<TOStdItem>(reader.ReadBytes(60));
            MakeIndex = reader.ReadInt32();
            Dura = reader.ReadUInt16();
            DuraMax = reader.ReadUInt16();
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(Item.GetBuffer());
            writer.Write(MakeIndex);
            writer.Write(Dura);
            writer.Write(DuraMax);
        }
    }
}
