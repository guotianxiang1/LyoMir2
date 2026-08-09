using ProtoBuf;
using System;
using System.IO;

namespace SystemModule
{
    [ProtoContract]
    public class TAbility : Packets
    {
        [ProtoMember(1)]
        public ushort Level;
        [ProtoMember(2)]
        public int AC;
        [ProtoMember(3)]
        public int MAC;
        [ProtoMember(4)]
        public int DC;
        [ProtoMember(5)]
        public int MC;
        [ProtoMember(6)]
        public int SC;
        
        
        
        [ProtoMember(7)]
        public int HP;
        
        
        
        [ProtoMember(8)]
        public int MP;
        [ProtoMember(9)]
        public int MaxHP;
        [ProtoMember(10)]
        public int MaxMP;
        
        
        
        [ProtoMember(11)]
        public int Exp;
        
        
        
        [ProtoMember(12)]
        public int MaxExp;
        
        
        
        [ProtoMember(13)]
        public ushort Weight;
        
        
        
        [ProtoMember(14)]
        public ushort MaxWeight;
        
        
        
        [ProtoMember(15)]
        public ushort WearWeight;
        
        
        
        [ProtoMember(16)]
        public ushort MaxWearWeight;
        
        
        
        [ProtoMember(17)]
        public ushort HandWeight;
        
        
        
        [ProtoMember(18)]
        public ushort MaxHandWeight;

        public TAbility() { }

        // Bug1 fix 2026-04-22: field-by-field copy so callers can replace
        // reference assignments like `m_WAbil = m_Abil;` which caused aliasing
        // and per-Recalc accumulation of equipment bonuses onto the base ability.
        public void CopyFrom(TAbility other)
        {
            if (other == null) return;
            Level = other.Level;
            AC = other.AC;
            MAC = other.MAC;
            DC = other.DC;
            MC = other.MC;
            SC = other.SC;
            HP = other.HP;
            MP = other.MP;
            MaxHP = other.MaxHP;
            MaxMP = other.MaxMP;
            Exp = other.Exp;
            MaxExp = other.MaxExp;
            Weight = other.Weight;
            MaxWeight = other.MaxWeight;
            WearWeight = other.WearWeight;
            MaxWearWeight = other.MaxWearWeight;
            HandWeight = other.HandWeight;
            MaxHandWeight = other.MaxHandWeight;
        }

        public TAbility(byte[] buff)
        {
            Level = BitConverter.ToUInt16(buff, 0);
            AC = BitConverter.ToInt32(buff, 2);
            MAC = BitConverter.ToInt32(buff, 6);
            DC = BitConverter.ToInt32(buff, 10);
            MC = BitConverter.ToInt32(buff, 14);
            SC = BitConverter.ToInt32(buff, 18);
            HP = BitConverter.ToInt32(buff, 22);
            MP = BitConverter.ToInt32(buff, 26);
            MaxHP = BitConverter.ToInt32(buff, 30);
            MaxMP = BitConverter.ToInt32(buff, 34);
            Exp = BitConverter.ToInt32(buff, 38);
            MaxExp = BitConverter.ToInt32(buff, 42);
            Weight = BitConverter.ToUInt16(buff, 46);
            MaxWeight = BitConverter.ToUInt16(buff, 48);
            WearWeight = BitConverter.ToUInt16(buff, 50);
            MaxWearWeight = BitConverter.ToUInt16(buff, 52);
            HandWeight = BitConverter.ToUInt16(buff, 54);
            MaxHandWeight = BitConverter.ToUInt16(buff, 56);
        }

        protected override void ReadPacket(BinaryReader reader)
        {
            Level = reader.ReadUInt16();
            AC = reader.ReadInt32();
            MAC = reader.ReadInt32();
            DC = reader.ReadInt32();
            MC = reader.ReadInt32();
            SC = reader.ReadInt32();
            HP = reader.ReadInt32();
            MP = reader.ReadInt32();
            MaxHP = reader.ReadInt32();
            MaxMP = reader.ReadInt32();
            Exp = reader.ReadInt32();
            MaxExp = reader.ReadInt32();
            Weight = reader.ReadUInt16();
            MaxWeight = reader.ReadUInt16();
            WearWeight = reader.ReadUInt16();
            MaxWearWeight = reader.ReadUInt16();
            HandWeight = reader.ReadUInt16();
            MaxHandWeight = reader.ReadUInt16();
        }

        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(Level);
            writer.Write(AC);
            writer.Write(MAC);
            writer.Write(DC);
            writer.Write(MC);
            writer.Write(SC);
            writer.Write(HP);
            writer.Write(MP);
            writer.Write(MaxHP);
            writer.Write(MaxMP);
            writer.Write(Exp);
            writer.Write(MaxExp);
            writer.Write(Weight);
            writer.Write(MaxWeight);
            writer.Write(WearWeight);
            writer.Write(MaxWearWeight);
            writer.Write(HandWeight);
            writer.Write(MaxHandWeight);
        }
    }
}
