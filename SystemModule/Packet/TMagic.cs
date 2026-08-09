using System;
using System.IO;

namespace SystemModule
{
    public class TMagic : Packets
    {
        public const int RecordSize = 76;
        
        
        
        public ushort wMagicID;
        
        
        
        public string sMagicName;
        
        
        
        public byte btEffectType;
        
        
        
        public byte btEffect;
        
        
        
        public ushort wSpell;
        
        
        
        public ushort wPower;
        
        
        
        public byte[] TrainLevel;
        
        
        
        public int[] MaxTrain;
        
        
        
        public byte btTrainLv;
        
        
        
        public byte btJob;
        
        
        
        public int dwDelayTime;
        
        
        
        public byte btDefSpell;
        
        
        
        public byte btDefPower;
        
        
        
        public ushort wMaxPower;
        
        
        
        public byte btDefMaxPower;

        // Native 60-byte definition fields that are not part of this 76-byte packet.
        public byte NeedLevel5;
        public int ColdMilliseconds;
        public int SpellMilliseconds;
        
        
        
        public string sDescr;

        public TMagic()
        {
            TrainLevel = new byte[4];
            MaxTrain = new int[4];
        }

        protected override void ReadPacket(BinaryReader reader)
        {
            wMagicID = reader.ReadUInt16();
            sMagicName = ReadShortString(reader, 12);
            btEffectType = reader.ReadByte();
            btEffect = reader.ReadByte();
            reader.ReadByte();
            wSpell = reader.ReadUInt16();
            wPower = reader.ReadUInt16();
            TrainLevel = reader.ReadBytes(4);
            if (TrainLevel.Length != 4) throw new EndOfStreamException();
            reader.ReadUInt16();
            MaxTrain = new int[4];
            for (var i = 0; i < MaxTrain.Length; i++)
            {
                MaxTrain[i] = reader.ReadInt32();
            }
            btTrainLv = reader.ReadByte();
            btJob = reader.ReadByte();
            reader.ReadUInt16();
            dwDelayTime = reader.ReadInt32();
            btDefSpell = reader.ReadByte();
            btDefPower = reader.ReadByte();
            wMaxPower = reader.ReadUInt16();
            btDefMaxPower = reader.ReadByte();
            sDescr = ReadShortString(reader, 18);
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(wMagicID);
            WriteShortString(writer, sMagicName, 12);
            writer.Write(btEffectType);
            writer.Write(btEffect);
            writer.Write((byte)0);
            writer.Write(wSpell);
            writer.Write(wPower);
            writer.Write(TrainLevel);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(MaxTrain[0]);
            writer.Write(MaxTrain[1]);
            writer.Write(MaxTrain[2]);
            writer.Write(MaxTrain[3]);
            writer.Write(btTrainLv);
            writer.Write(btJob);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(dwDelayTime);
            writer.Write(btDefSpell);
            writer.Write(btDefPower);
            writer.Write(wMaxPower);
            writer.Write(btDefMaxPower);
            WriteShortString(writer, sDescr, 18);
        }

        private static string ReadShortString(BinaryReader reader, int capacity)
        {
            var length = reader.ReadByte();
            var bytes = reader.ReadBytes(capacity);
            if (length > capacity || bytes.Length != capacity)
                throw new InvalidDataException("Invalid TMagic short string.");
            return HUtil32.GbkEncoding.GetString(bytes, 0, length);
        }

        private static void WriteShortString(BinaryWriter writer, string value, int capacity)
        {
            var bytes = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            if (bytes.Length > capacity)
                Array.Resize(ref bytes, capacity);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
            if (bytes.Length < capacity)
                writer.Write(new byte[capacity - bytes.Length]);
        }
    }
}
