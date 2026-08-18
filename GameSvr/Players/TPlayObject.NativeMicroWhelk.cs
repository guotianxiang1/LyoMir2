using System;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const int NativeCharacterNameMaximumBytes = 14;
        private const ushort NativeMicroWhelkDuraCost = 1000;

        /// <summary>
        /// CM 3295 leaf 0x6DAA99 -> worker 0x6EB8E4. The request Recog selects
        /// the first matching client id in the player's main bag. A usable
        /// TMicroWhelk consumes 1000 durability, then emits SM 106 either to the
        /// muted player or through the native type-18 broadcast envelope.
        /// </summary>
        private void HandleNativeCm3295(TProcessMessage processMessage)
        {
            if (processMessage == null)
                return;

            var item = FindClientItemIn(m_ItemList, processMessage.nParam1, false);
            var stdItem = item == null
                ? null
                : M2Share.UserEngine?.GetStdItem(item.wIndex);
            if (item == null
                || stdItem == null
                || !NativeItemFactory.IsClassOrDescendantOf(stdItem, "TMicroWhelk")
                || item.Dura < NativeMicroWhelkDuraCost)
            {
                return;
            }

            var visibleText = BuildNativeMicroWhelkVisibleText(
                m_sCharName, processMessage.sMsg, processMessage.Payload);

            item.Dura = unchecked((ushort)(item.Dura - NativeMicroWhelkDuraCost));
            if (item.Dura >= NativeMicroWhelkDuraCost)
            {
                SendDefMessage(Grobal2.SM_BAGITEMDURACHG,
                    processMessage.nParam1, item.Dura, item.DuraMax, 0,
                    string.Empty);
            }
            else
            {
                if (m_ItemList.Remove(item))
                    SendDelItems(item);
                Dispose(item);
            }

            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_MICROWHELK,
                ObjectId, processMessage.nParam2, processMessage.nParam3, 1);
            if (IsNativeChatMuted())
            {
                // The worker's logical AnsiString already ends in one NUL;
                // vmt+0x250 adds the second transport terminator.
                var directBody = new byte[visibleText.Length + 2];
                Buffer.BlockCopy(visibleText, 0, directBody, 0,
                    visibleText.Length);
                SendSocket(header, directBody);
                return;
            }

            BroadcastNativeCm3295(new LegacyGateType18
            {
                FilterUserIndex = 0,
                Recog = ObjectId,
                Ident = Grobal2.SM_MICROWHELK,
                Param = unchecked((ushort)processMessage.nParam2),
                Tag = unchecked((ushort)processMessage.nParam3),
                Series = 1,
                // LegacyGateType18 appends the worker's single broadcast NUL.
                TextBytes = visibleText
            });
        }

        internal virtual void BroadcastNativeCm3295(LegacyGateType18 packet)
        {
            M2Share.GateManager?.BroadcastLegacyType18(packet);
        }

        internal static byte[] BuildNativeMicroWhelkVisibleText(string characterName,
            string textBody, object payload)
        {
            var encodedName = HUtil32.GbkEncoding.GetBytes(characterName ?? string.Empty);
            var nameLength = Math.Min(encodedName.Length,
                NativeCharacterNameMaximumBytes);
            var requestBytes = GetNativeMicroWhelkRequestBytes(textBody, payload);
            var result = new byte[nameLength + 2 + requestBytes.Length];
            Buffer.BlockCopy(encodedName, 0, result, 0, nameLength);
            result[nameLength] = 0x0D;
            result[nameLength + 1] = 0x0A;
            Buffer.BlockCopy(requestBytes, 0, result, nameLength + 2,
                requestBytes.Length);
            return result;
        }

        private static byte[] GetNativeMicroWhelkRequestBytes(string textBody,
            object payload)
        {
            if (payload is byte[] rawBody)
            {
                var length = Array.IndexOf(rawBody, (byte)0);
                if (length < 0)
                    length = rawBody.Length;
                var request = new byte[length];
                Buffer.BlockCopy(rawBody, 0, request, 0, length);
                return request;
            }

            var text = textBody ?? string.Empty;
            var zeroIndex = text.IndexOf('\0');
            if (zeroIndex >= 0)
                text = text.Substring(0, zeroIndex);
            return HUtil32.GbkEncoding.GetBytes(text);
        }
    }
}
