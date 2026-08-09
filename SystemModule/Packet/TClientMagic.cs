
using System.IO;

namespace SystemModule
{
    public class TClientMagic : Packets
    {
        public const int RecordSize = 84;
        public char Key;
        public byte Level;
        public int CurTrain;
        public TMagic Def;

        public TClientMagic()
        {
            Def = new TMagic();
        }

        protected override void ReadPacket(BinaryReader reader)
        {
            Key = (char)reader.ReadByte();
            Level = reader.ReadByte();
            reader.ReadUInt16();
            CurTrain = reader.ReadInt32();
            var defBytes = reader.ReadBytes(TMagic.RecordSize);
            if (defBytes.Length != TMagic.RecordSize)
                throw new EndOfStreamException();
            Def = Packets.ToPacket<TMagic>(defBytes)
                ?? throw new InvalidDataException("Invalid TClientMagic definition.");
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write((byte)Key);
            writer.Write(Level);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(CurTrain);
            var def = Def?.GetBuffer() ?? throw new InvalidDataException("Missing TClientMagic definition.");
            if (def.Length != TMagic.RecordSize)
                throw new InvalidDataException("Invalid TClientMagic definition.");
            writer.Write(def);
        }
    }
}
