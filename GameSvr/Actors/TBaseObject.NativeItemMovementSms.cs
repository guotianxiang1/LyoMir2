using System.Buffers.Binary;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    public partial class TBaseObject
    {
        internal const int NativeItemMovementSmsPayloadSize = 0x78;
        internal const ushort NativeItemMovementSmsManagerIdent = 0x87;
        internal const byte NativeItemMovementSmsDeathEvent = 0;
        internal const byte NativeItemMovementSmsConsignmentEvent = 1;

        // actor+0x4C6. Player login normalizes suffix+0x56 bit 0 to this byte;
        // THeroAct snapshots it from its owner before Initialize.
        internal bool m_boNativeItemMovementSmsEnabled;

        internal static byte[] BuildNativeItemMovementSmsPayload(
            string serverName, string ownerUserId, string ownerCharacterName,
            string itemName, int makeIndex, byte eventKind)
        {
            var payload = new byte[NativeItemMovementSmsPayloadSize];
            WriteNativeSmsPChar(payload.AsSpan(0x00, 0x10), serverName, 0x0F);
            WriteNativeSmsPChar(payload.AsSpan(0x10, 0x20), ownerUserId, 0x1F);
            WriteNativeSmsPChar(payload.AsSpan(0x30, 0x20), ownerCharacterName,
                0x1F);
            WriteNativeSmsPChar(payload.AsSpan(0x50, 0x20), itemName, 0x1F);
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(0x70, sizeof(int)), makeIndex);
            payload[0x74] = eventKind;
            return payload;
        }

        internal void CopyNativeItemMovementSmsState(TPlayObject nativeOwner)
        {
            m_boNativeItemMovementSmsEnabled =
                nativeOwner?.m_boNativeItemMovementSmsEnabled == true;
        }

        internal bool TryNotifyNativeItemMovementSms(TPlayObject nativeOwner,
            GoodItem stdItem, TUserItem item, byte eventKind)
        {
            // Native callers test actor+0x4C6 and std[+3]&2 before sub_743F14.
            if (!m_boNativeItemMovementSmsEnabled || nativeOwner == null
                || stdItem == null || item == null
                || (stdItem.NativeReserved02 & 0x0200) == 0)
                return false;

            var itemName = stdItem.Name ?? string.Empty;
            // sub_743F14 calls sub_768BE0 first and still logs when the manager is down.
            M2Share.AddNativeGameDataLog(this, 0x99, itemName,
                item.MakeIndex, eventKind, "短信提醒");

            var payload = BuildNativeItemMovementSmsPayload(
                M2Share.g_Config?.sServerName, nativeOwner.m_sUserID,
                nativeOwner.m_sCharName, itemName, item.MakeIndex, eventKind);
            return YbDbClient.Instance.TryEnqueueNativeItemMovementSms(payload);
        }

        private static void WriteNativeSmsPChar(Span<byte> destination,
            string value, int maximumBytes)
        {
            destination.Clear();
            var encoded = HUtil32.GbkEncoding.GetBytes(value ?? string.Empty);
            var sourceLength = Array.IndexOf(encoded, (byte)0);
            if (sourceLength < 0)
                sourceLength = encoded.Length;
            var count = Math.Min(Math.Min(sourceLength, maximumBytes),
                destination.Length - 1);
            encoded.AsSpan(0, count).CopyTo(destination);
            // Deliberately byte-based: StrPLCopy can leave half a GBK character at
            // the 15/31-byte boundary, then writes this trailing NUL.
            destination[count] = 0;
        }
    }
}
