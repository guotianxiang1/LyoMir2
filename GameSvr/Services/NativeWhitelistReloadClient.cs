using System.Buffers.Binary;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr.Services
{
    // GS1 command 0x00629465 sends Type2 0184. Type1 case 0132 at
    // 0x00654407 delegates to sub_653E60 for name lookup and 0xFFDB SysMsg.
    internal static class NativeWhitelistReloadClient
    {
        internal const ushort RequestCommand = 0x0184;
        internal const ushort ResponseCommand = 0x0132;
        internal const int Type2HeaderSize = 0x0C;
        internal const int Type1HeaderSize = 0x48;
        internal const int CharacterNameOffset = 0x25;
        internal const int CharacterNameCapacity = 15;
        internal const int NativeSysMsgIdent = 0xFFDB;
        internal const byte ForegroundColor = 0xDB;
        internal const byte BackgroundColor = 0xFF;

        internal static bool TryEncodeRequest(TPlayObject player,
            out byte[] wire, out string error)
        {
            wire = null;
            error = string.Empty;
            if (player == null)
            {
                error = "native whitelist reload player is null";
                return false;
            }

            var characterName = HUtil32.GbkEncoding.GetBytes(
                player.m_sCharName ?? string.Empty);
            if (characterName.Length > CharacterNameCapacity)
            {
                error = $"native whitelist reload character name exceeds "
                        + $"{CharacterNameCapacity} GBK bytes";
                return false;
            }

            var payload = new byte[Type2HeaderSize + characterName.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(payload, RequestCommand);
            characterName.CopyTo(payload, Type2HeaderSize);
            return LegacyDbServerFrameCodec.TryEncode(
                new LegacyDbServerFrame(2, 0, payload),
                out wire, out error);
        }

        internal static bool SendRequest(TPlayObject player)
        {
            if (!TryEncodeRequest(player, out var wire, out var error))
            {
                M2Share.ErrorMessage(
                    "[ReloadWhiteList] 原生0184请求编码失败: " + error);
                return false;
            }

            return M2Share.DataServer != null
                   && M2Share.DataServer.SendNativeFrame(wire);
        }

        internal static void ProcessResponse(LegacyDbServerFrame frame)
        {
            TryProcessResponse(frame, FindOnlinePlayer,
                static (player, message, foreground, background) =>
                    player.SendMsg(player, Grobal2.RM_SYSMESSAGE, 0,
                        foreground, background, 0, message));
        }

        internal static bool TryProcessResponse(LegacyDbServerFrame frame,
            Func<byte[], TPlayObject> findPlayer,
            Action<TPlayObject, string, byte, byte> sendMessage)
        {
            if (findPlayer == null || sendMessage == null
                || !TryDecodeResponse(frame, out var characterName,
                    out var messageBytes))
                return false;

            var player = findPlayer(characterName);
            if (player == null)
                return false;

            var message = HUtil32.GbkEncoding.GetString(messageBytes);
            sendMessage(player, message, ForegroundColor, BackgroundColor);
            return true;
        }

        internal static bool TryDecodeResponse(LegacyDbServerFrame frame,
            out byte[] characterName, out byte[] message)
        {
            characterName = null;
            message = null;
            if (frame == null || frame.Type != 1
                              || frame.Payload == null
                              || frame.Payload.Length < Type1HeaderSize)
                return false;

            var payload = frame.Payload.AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload)
                != ResponseCommand)
                return false;

            var nameLength = payload[CharacterNameOffset];
            if (nameLength > CharacterNameCapacity)
                return false;

            characterName = payload.Slice(
                CharacterNameOffset + 1, nameLength).ToArray();
            message = payload.Slice(Type1HeaderSize).ToArray();
            return true;
        }

        internal static TPlayObject FindOnlinePlayer(byte[] characterName)
        {
            return FindOnlinePlayer(M2Share.UserEngine?.GetPlayerList(),
                characterName);
        }

        internal static TPlayObject FindOnlinePlayer(
            IEnumerable<TPlayObject> players, byte[] characterName)
        {
            if (players == null || characterName == null
                                || characterName.Length == 0)
                return null;

            foreach (var player in players)
            {
                if (player == null)
                    continue;
                var candidate = HUtil32.GbkEncoding.GetBytes(
                    player.m_sCharName ?? string.Empty);
                if (!NativeNameEquals(candidate, characterName))
                    continue;

                // sub_652784 returns its indexed match only when both gates pass.
                return player.m_boGhost || !player.m_boReadyRun
                    ? null : player;
            }
            return null;
        }

        internal static bool NativeNameEquals(ReadOnlySpan<byte> left,
            ReadOnlySpan<byte> right)
        {
            if (left.Length != right.Length)
                return false;
            for (var index = 0; index < left.Length; index++)
            {
                if (FoldAscii(left[index]) != FoldAscii(right[index]))
                    return false;
            }
            return true;
        }

        private static byte FoldAscii(byte value) =>
            value is >= (byte)'A' and <= (byte)'Z'
                ? (byte)(value + ('a' - 'A'))
                : value;
    }
}
