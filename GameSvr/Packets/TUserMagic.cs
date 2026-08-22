using SystemModule;

namespace GameSvr
{
    public class TUserMagic : Packets
    {
        public TMagic MagicInfo;
        public byte btLevel;
        public ushort wMagIdx;
        public int nTranPoint;
        public byte btKey;
        public byte[] NativeRecord;
        internal byte NativeLevelBonus;

        public TUserMagic()
        {
            MagicInfo = new TMagic();
        }

        protected override void ReadPacket(BinaryReader reader)
        {
            var magicBytes = reader.ReadBytes(TMagic.RecordSize);
            if (magicBytes.Length != TMagic.RecordSize)
                throw new EndOfStreamException();

            MagicInfo = Packets.ToPacket<TMagic>(magicBytes)
                ?? throw new InvalidDataException("Invalid TUserMagic definition.");
            btLevel = reader.ReadByte();
            wMagIdx = reader.ReadUInt16();
            nTranPoint = reader.ReadInt32();
            btKey = reader.ReadByte();
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(MagicInfo.GetBuffer());
            writer.Write(btLevel);
            writer.Write(wMagIdx);
            writer.Write(nTranPoint);
            writer.Write(btKey);
        }
    }
}
