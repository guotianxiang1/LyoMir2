using SystemModule;

namespace GameSvr
{
    public class TShortMessage : Packets
    {
        public ushort Ident;
        public ushort wMsg;

        protected override void ReadPacket(BinaryReader reader)
        {
            Ident = reader.ReadUInt16();
            wMsg = reader.ReadUInt16();
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(Ident);
            writer.Write(wMsg);
        }
    }
}
