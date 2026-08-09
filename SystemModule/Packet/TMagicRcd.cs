using ProtoBuf;
using System;
using System.IO;

namespace SystemModule
{
    [ProtoContract]
    public class TMagicRcd : Packets
    {
        
        
        
        [ProtoMember(1)]
        public ushort wMagIdx;
        
        
        
        [ProtoMember(2)]
        public byte btLevel;
        
        
        
        [ProtoMember(3)]
        public byte btKey;
        
        
        
        [ProtoMember(4)]
        public int nTranPoint;

        // Native magic entries are 40 bytes. Only the first eight bytes are mapped today.
        [ProtoMember(5, OverwriteList = true)]
        public byte[] NativeRecord;

        internal bool IsTransportPlaceholder()
        {
            if (wMagIdx != 0 || btLevel != 0 || btKey != 0 || nTranPoint != 0)
                return false;
            if (NativeRecord == null) return true;
            for (var i = 0; i < NativeRecord.Length; i++)
                if (NativeRecord[i] != 0) return false;
            return true;
        }

        protected override void ReadPacket(BinaryReader reader)
        {
            this.wMagIdx = reader.ReadUInt16();
            this.btLevel = reader.ReadByte();
            this.btKey = reader.ReadByte();
            this.nTranPoint = reader.ReadInt32();
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(wMagIdx);
            writer.Write(btLevel);
            writer.Write(btKey);
            writer.Write(nTranPoint);
        }
    }
}
