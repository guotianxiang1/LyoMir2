using System;
using System.Buffers.Binary;
using SystemModule.Packet;

namespace DBSvr.Core
{
    public static class NativeCharacterBusyProtocol
    {
        public const ushort Command = 0x016A;
        public const int HeaderSize = 0x48;

        public static bool TryDecode(LegacyDbServerFrame frame,
            out byte[] characterName, out string error)
        {
            characterName = null;
            error = string.Empty;
            if (frame == null || frame.Type != 1
                || frame.Payload.Length < HeaderSize)
            {
                error = "native 0x016A envelope is invalid";
                return false;
            }
            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload) != Command)
            {
                error = "native 0x016A command mismatch";
                return false;
            }
            var length = payload[0x25];
            if (length > 15)
            {
                error = "native 0x016A character name exceeds 15 bytes";
                return false;
            }
            characterName = payload.Slice(0x26, length).ToArray();
            return true;
        }
    }
}
