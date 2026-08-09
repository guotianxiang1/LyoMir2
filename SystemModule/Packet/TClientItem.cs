
using System.IO;

namespace SystemModule
{
    public class TClientItem : Packets
    {
        public TStdItem Item;
        public int MakeIndex;
        public ushort Dura;
        public ushort DuraMax;

        public TClientItem()
        {
            Item = new TStdItem();
        }

        protected override void ReadPacket(BinaryReader reader)
        {
            Item = Packets.ToPacket<TStdItem>(reader.ReadBytes(68));
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
