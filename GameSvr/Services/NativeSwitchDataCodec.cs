using System.Buffers.Binary;
using SystemModule;

namespace GameSvr
{
    internal static class NativeSwitchDataCodec
    {
        internal const int SessionPrefixSize = 0xA0;
        internal const int ExtensionSize = 0x108;
        internal const int SessionSuffixSize = SessionPrefixSize + ExtensionSize;
        internal const int SlaveOffset = 0x68;
        internal const int SlaveSlotCount = 5;

        private const int SerialOffset = 0x04;
        private const int FlagsOffset = 0x08;
        private const int ValueD38Offset = 0x0A;
        private const int ValueD3COffset = 0x0C;
        private const int ValueD40Offset = 0x10;
        private const int HeroKindOffset = 0x14;
        private const int HeroSlotOffset = 0x15;
        private const ushort FlagPassThrough = 0x08;
        private const ushort FlagHeroHandoff = 0x10;
        private const ushort FlagOffsetB75 = 0x20;

        internal static bool TryEncode(TPlayObject player,
            out byte[] extension, out string error) =>
            TryEncode(player, HUtil32.GetTickCount(), out extension, out error);

        internal static bool TryEncode(TPlayObject player, int currentTick,
            out byte[] extension, out string error)
        {
            extension = null;
            error = string.Empty;
            if (player == null)
            {
                error = "native switch player is null";
                return false;
            }

            extension = new byte[ExtensionSize];
            BinaryPrimitives.WriteInt32LittleEndian(
                extension.AsSpan(SerialOffset, 4),
                unchecked(player.m_nNativeSwitchSerial + 1));

            ushort flags = 0;
            if (player.HasNativeCellPassThroughGrant()) flags |= FlagPassThrough;
            if (player.m_boNativeSwitchHeroHandoffPending) flags |= FlagHeroHandoff;
            if (player.m_boNativeSwitchOffsetB75) flags |= FlagOffsetB75;
            BinaryPrimitives.WriteUInt16LittleEndian(
                extension.AsSpan(FlagsOffset, 2), flags);
            BinaryPrimitives.WriteUInt16LittleEndian(
                extension.AsSpan(ValueD38Offset, 2), player.m_wNativeSwitchOffsetD38);
            if (player.m_wNativeSwitchOffsetD38 == 0)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    extension.AsSpan(ValueD3COffset, 4),
                    player.m_nNativeSwitchOffsetD3C);
                BinaryPrimitives.WriteInt32LittleEndian(
                    extension.AsSpan(ValueD40Offset, 4),
                    player.m_nNativeSwitchOffsetD40);
            }
            else
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    extension.AsSpan(ValueD3COffset, 4),
                    unchecked(currentTick - player.m_dwNativeSwitchOffsetD44));
            }
            extension[HeroKindOffset] = player.m_btNativeHeroRequestKind;
            extension[HeroSlotOffset] = player.m_btNativeHeroRequestSlot;

            var written = 0;
            foreach (var slave in player.m_SlaveList)
            {
                if (slave == null || slave.m_boDeath || slave.m_boGhost)
                    continue;
                if (player.m_HeroObject?.IsNativeHeroSummonSlave(slave) == true)
                    continue;
                if (written >= SlaveSlotCount)
                    break;

                var target = extension.AsSpan(
                    SlaveOffset + written * NativeSlaveInfoCodec.RecordSize,
                    NativeSlaveInfoCodec.RecordSize);
                if (!NativeSlaveInfoCodec.TryEncode(target, slave,
                        currentTick, out error))
                {
                    extension = null;
                    return false;
                }
                written++;
            }
            return true;
        }

        internal static bool TryRestoreFromSessionSuffix(TPlayObject player,
            ReadOnlySpan<byte> sessionSuffix, out bool restored, out string error)
        {
            restored = false;
            error = string.Empty;
            if (sessionSuffix.Length == 0)
                return true;
            if (sessionSuffix.Length != SessionSuffixSize)
            {
                error = $"native session suffix length must be {SessionSuffixSize}";
                return false;
            }
            return TryRestore(player,
                sessionSuffix.Slice(SessionPrefixSize, ExtensionSize),
                HUtil32.GetTickCount(), out restored, out error);
        }

        internal static bool TryRestore(TPlayObject player,
            ReadOnlySpan<byte> extension, int currentTick,
            out bool restored, out string error)
        {
            restored = false;
            error = string.Empty;
            if (player == null)
            {
                error = "native switch player is null";
                return false;
            }
            if (extension.Length != ExtensionSize)
            {
                error = $"native switch extension length must be {ExtensionSize}";
                return false;
            }

            var serial = BinaryPrimitives.ReadInt32LittleEndian(
                extension.Slice(SerialOffset, 4));
            if (serial == 0)
                return true;

            var slaves = new TSlaveInfo[SlaveSlotCount];
            for (var index = 0; index < SlaveSlotCount; index++)
            {
                var record = extension.Slice(
                    SlaveOffset + index * NativeSlaveInfoCodec.RecordSize,
                    NativeSlaveInfoCodec.RecordSize);
                if (!NativeSlaveInfoCodec.TryDecode(record,
                        out slaves[index], out error))
                    return false;
            }

            player.m_nNativeSwitchSerial = serial;
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(
                extension.Slice(FlagsOffset, 2));
            player.m_boNativeSwitchOffsetB75 = (flags & FlagOffsetB75) != 0;
            player.m_boObMode = (flags & FlagPassThrough) != 0;
            player.m_boNativeSwitchHeroHandoffPending =
                (flags & FlagHeroHandoff) != 0;
            if (player.m_boNativeSwitchHeroHandoffPending)
                player.RecordNativeSwitchHeroRequestTick(currentTick);
            player.m_wNativeSwitchOffsetD38 =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    extension.Slice(ValueD38Offset, 2));
            if (player.m_wNativeSwitchOffsetD38 == 0)
            {
                player.m_nNativeSwitchOffsetD3C =
                    BinaryPrimitives.ReadInt32LittleEndian(
                        extension.Slice(ValueD3COffset, 4));
                player.m_nNativeSwitchOffsetD40 =
                    BinaryPrimitives.ReadInt32LittleEndian(
                        extension.Slice(ValueD40Offset, 4));
                player.m_dwNativeSwitchOffsetD44 = currentTick;
            }
            else
            {
                player.m_dwNativeSwitchOffsetD44 = unchecked(
                    currentTick - player.m_nNativeSwitchOffsetD3C);
            }
            player.m_btNativeHeroRequestKind = extension[HeroKindOffset];
            player.m_btNativeHeroRequestSlot = extension[HeroSlotOffset];

            for (var index = 0; index < SlaveSlotCount; index++)
            {
                var slaveInfo = slaves[index];
                if (slaveInfo == null)
                    continue;
                player.SendDelayMsg(player, Grobal2.RM_10401,
                    0, 0, 0, 0, string.Empty, 1500, slaveInfo);
            }

            restored = true;
            return true;
        }
    }
}
