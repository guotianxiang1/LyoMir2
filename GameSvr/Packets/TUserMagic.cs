using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    public class TUserMagic : Packets
    {
        public const ushort NativeSkillSwitchOn = 0;
        public const ushort NativeSkillSwitchOff = 0x00FF;

        public TMagic MagicInfo;
        public byte btLevel;
        public ushort wMagIdx;
        public int nTranPoint;
        public byte btKey;
        private byte[] _nativeRecord;
        private ushort _nativeSkillSwitchValue = NativeSkillSwitchOn;
        internal byte NativeLevelBonus;

        /// <summary>
        /// Native runtime word at magic entry +0x0E. The 40-byte persisted
        /// magic record carries the same word at +0x06.
        /// </summary>
        public ushort NativeSkillSwitchValue => _nativeSkillSwitchValue;

        /// <summary>Native treats zero as enabled; the command writes 0xFF when disabled.</summary>
        public bool NativeSkillEnabled => _nativeSkillSwitchValue == NativeSkillSwitchOn;

        public byte[] NativeRecord
        {
            get => _nativeRecord;
            set
            {
                _nativeRecord = value;
                if (value != null && value.Length >= 8)
                {
                    _nativeSkillSwitchValue = BinaryPrimitives.ReadUInt16LittleEndian(
                        value.AsSpan(6, 2));
                }
            }
        }

        public TUserMagic()
        {
            MagicInfo = new TMagic();
        }

        public void SetNativeSkillSwitch(bool enabled)
        {
            _nativeSkillSwitchValue = enabled
                ? NativeSkillSwitchOn
                : NativeSkillSwitchOff;
            if (_nativeRecord != null && _nativeRecord.Length >= 8)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    _nativeRecord.AsSpan(6, 2), _nativeSkillSwitchValue);
            }
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
