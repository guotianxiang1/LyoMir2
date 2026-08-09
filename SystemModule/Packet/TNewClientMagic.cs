using System;
using System.IO;

namespace SystemModule
{
    public class TNewClientMagic : Packets
    {
        public const int RecordSize = 46;

        public string MagicName = string.Empty;
        public byte MagicType;
        public byte EffectType;
        public byte Effect;
        public ushort MagicId;
        public short Level;
        public short Key;
        public short NeedMp;
        public short SpellTick;
        public short NextNeedLv;
        public int ColdTick;
        public int CurTrain;
        public int MaxTrain;
        public int DelayTime;

        protected override void ReadPacket(BinaryReader reader)
        {
            var nameLength = reader.ReadByte();
            var nameBytes = reader.ReadBytes(14);
            if (nameBytes.Length != 14 || nameLength > 14)
                throw new InvalidDataException("Invalid TNewClientMagic name field.");

            MagicName = HUtil32.GbkEncoding.GetString(nameBytes, 0, nameLength);
            MagicType = reader.ReadByte();
            EffectType = reader.ReadByte();
            Effect = reader.ReadByte();
            MagicId = reader.ReadUInt16();
            Level = reader.ReadInt16();
            Key = reader.ReadInt16();
            NeedMp = reader.ReadInt16();
            SpellTick = reader.ReadInt16();
            NextNeedLv = reader.ReadInt16();
            ColdTick = reader.ReadInt32();
            CurTrain = reader.ReadInt32();
            MaxTrain = reader.ReadInt32();
            DelayTime = reader.ReadInt32();
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            WriteShortString(writer, MagicName, 14);
            writer.Write(MagicType);
            writer.Write(EffectType);
            writer.Write(Effect);
            writer.Write(MagicId);
            writer.Write(Level);
            writer.Write(Key);
            writer.Write(NeedMp);
            writer.Write(SpellTick);
            writer.Write(NextNeedLv);
            writer.Write(ColdTick);
            writer.Write(CurTrain);
            writer.Write(MaxTrain);
            writer.Write(DelayTime);
        }

        private static void WriteShortString(BinaryWriter writer, string value, int maxBytes)
        {
            value ??= string.Empty;
            var charCount = value.Length;
            while (charCount > 0 &&
                   HUtil32.GbkEncoding.GetByteCount(value.Substring(0, charCount)) > maxBytes)
            {
                charCount--;
            }

            var bytes = HUtil32.GbkEncoding.GetBytes(value.Substring(0, charCount));
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
            if (bytes.Length < maxBytes)
                writer.Write(new byte[maxBytes - bytes.Length]);
        }
    }
}
