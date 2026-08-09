using System;
using System.Buffers.Binary;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr.Services
{
    internal static class NativeForceDisconnectClient
    {
        internal const ushort ResponseCommand = 0x0052;
        internal const int HeaderSize = 0x48;
        internal const int AccountOffset = 0x10;
        internal const int AccountCapacity = 20;
        internal const string ClientMessage = "NPC/与服务器断开连接...";

        internal static void ProcessResponse(LegacyDbServerFrame frame)
        {
            TryProcessResponse(frame, SendDisconnectMessage);
        }

        internal static bool TryProcessResponse(LegacyDbServerFrame frame,
            Action<TPlayObject, short, int, string> sendMessage)
        {
            if (sendMessage == null)
                throw new ArgumentNullException(nameof(sendMessage));
            if (!TryDecodeAccount(frame, out var account))
                return false;

            var player = FindOnlinePlayer(account);
            if (player == null)
                return false;

            player.m_boKickFlag = true;
            sendMessage(player, (short)Grobal2.SM_MERCHANTSAY,
                M2Share.g_FunctionNPC?.ObjectId ?? 0, ClientMessage);
            player.m_boSoftClose = true;
            return true;
        }

        internal static bool TryDecodeAccount(LegacyDbServerFrame frame,
            out byte[] account)
        {
            account = null;
            if (frame == null || frame.Type != 1)
                return false;

            var payload = frame.Payload;
            if (payload == null || payload.Length < HeaderSize
                                || BinaryPrimitives.ReadUInt16LittleEndian(
                                    payload.AsSpan(0, 2)) != ResponseCommand)
                return false;

            var length = payload[AccountOffset];
            if (length > AccountCapacity)
                return false;

            account = payload.AsSpan(AccountOffset + 1, length).ToArray();
            return true;
        }

        internal static bool NativeAccountEquals(ReadOnlySpan<byte> left,
            ReadOnlySpan<byte> right)
        {
            if (left.Length != right.Length)
                return false;

            for (var i = 0; i < left.Length; i++)
            {
                if (FoldAscii(left[i]) != FoldAscii(right[i]))
                    return false;
            }
            return true;
        }

        private static TPlayObject FindOnlinePlayer(ReadOnlySpan<byte> account)
        {
            var players = M2Share.UserEngine?.GetPlayerList();
            if (players == null)
                return null;

            foreach (var player in players)
            {
                if (player == null)
                    continue;
                var candidate = HUtil32.GbkEncoding.GetBytes(
                    player.m_sUserID ?? string.Empty);
                if (NativeAccountEquals(account, candidate))
                    return player;
            }
            return null;
        }

        private static byte FoldAscii(byte value)
        {
            return value >= (byte)'A' && value <= (byte)'Z'
                ? (byte)(value + ('a' - 'A'))
                : value;
        }

        private static void SendDisconnectMessage(TPlayObject player,
            short ident, int recog, string message)
        {
            player.SendDefMessage(ident, recog, 0, 0, 0, message);
        }
    }
}
