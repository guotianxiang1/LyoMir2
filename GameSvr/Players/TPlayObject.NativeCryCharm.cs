using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const ushort NativeCryCharmDuraCost = 1000;
        private const int NativeCryCharmMaximumBodyLength = 255;
        private const int NativeCryCharmMaximumNameLength = 15;
        private const int NativeCryCharmMinimumInterval = 1000;

        private static readonly ushort[] NativeCryCharmPalette =
        {
            0xFDFF, 0x38FF, 0xEA97, 0xFFFF, 0xFCFF
        };

        private static readonly Regex NativeCryCharmPlaceholderRegex = new(
            @"\{@([^<>}]*)\}", RegexOptions.CultureInvariant);
        private static readonly Regex NativeCryCharmPiRegex = new(
            @"\{@[pP][iI]([^<>}]*)\|([^<>}]*)\|([^<>}]*)\}",
            RegexOptions.CultureInvariant);
        private static readonly Regex NativeCryCharmItemRegex = new(
            @"\{@[iI][tT]([^<>}]*)\|([^<>}]*)\|([^<>}]*)\|([^<>}]*)\}",
            RegexOptions.CultureInvariant);

        private int _nativeCryCharmSendTick;

        internal virtual int GetNativeCryCharmTick()
        {
            return HUtil32.GetTickCount();
        }

        internal void ProcessNativeCryCharmCommand(string rawLine,
            byte[] rawPayload, int bodyLength)
        {
            var inputBytes = ExtractNativeCryCharmBody(rawLine, rawPayload,
                bodyLength);
            if (inputBytes == null)
                return;

            if (m_PEnvir == null || m_PEnvir.Flag.boBLACKROOM)
                return;

            var now = GetNativeCryCharmTick();
            if (unchecked((uint)(now - _nativeCryCharmSendTick))
                < NativeCryCharmMinimumInterval)
                return;

            if (!TryGetNativeCryCharm(out var charm, out var stdItem))
                return;
            if (inputBytes.Length > NativeCryCharmMaximumBodyLength)
                return;

            var nameBytes = HUtil32.GbkEncoding.GetBytes(m_sCharName ?? string.Empty);
            var inputText = HUtil32.GbkEncoding.GetString(inputBytes);
            var replacedText = NativeCryCharmPlaceholderRegex.Replace(inputText,
                "***");
            var replacedLength = HUtil32.GbkEncoding.GetByteCount(replacedText);

            if (!ValidateNativeCryCharmPictures(inputText))
            {
                SendNativeCryCharmSystemMessage(
                    "对不起，图片信息过大，无法发送", 0x38FF);
                return;
            }
            if (nameBytes.Length > NativeCryCharmMaximumNameLength)
                return;

            var visibleLength = nameBytes.Length + inputBytes.Length + 2;
            now = GetNativeCryCharmTick();
            if (unchecked((uint)(now - _nativeCryCharmSendTick))
                < NativeCryCharmMinimumInterval)
                return;
            _nativeCryCharmSendTick = now;

            var tag = unchecked((byte)stdItem.AniCount);
            if (tag > 0
                && nameBytes.Length + 2 + replacedLength + 1 > 64)
            {
                SendNativeCryCharmSystemMessage(
                    "对不起,你所输入的字太多,无法发送", 0xFFDB);
                return;
            }

            var paletteIndex = unchecked((byte)stdItem.Source);
            if (!TryUseItemEffect(stdItem, charm))
                return;
            if (paletteIndex > 4)
                paletteIndex = 0;

            if (charm.Dura >= NativeCryCharmDuraCost)
            {
                SendMsg(this, Grobal2.RM_DURACHANGE, Grobal2.U_CHARM,
                    charm.Dura, charm.DuraMax, 0, string.Empty);
            }
            else
            {
                m_UseItems[Grobal2.U_CHARM] = null;
                RecalcAbilitys();
                SendDelItems(charm);
                M2Share.AddNativeGameDataLog(this, 0x0A, stdItem.Name,
                    charm.MakeIndex, 1, "持久耗尽");
                Dispose(charm);
            }

            if (replacedLength + 1 < inputBytes.Length
                && !ValidateAndBroadcastNativeCryCharmItems(inputText))
            {
                SendNativeCryCharmSystemMessage(
                    "对不起，无效物品信息，无法发送", 0x38FF);
                return;
            }

            var visibleBody = new byte[visibleLength];
            Buffer.BlockCopy(nameBytes, 0, visibleBody, 0, nameBytes.Length);
            visibleBody[nameBytes.Length] = (byte)':';
            visibleBody[nameBytes.Length + 1] = (byte)' ';
            Buffer.BlockCopy(inputBytes, 0, visibleBody, nameBytes.Length + 2,
                inputBytes.Length);

            var palette = NativeCryCharmPalette[paletteIndex];
            var header = Grobal2.MakeDefaultMsg(Grobal2.SM_MICROWHELK,
                ObjectId, palette, tag, 0);
            if (IsNativeChatMuted())
            {
                SendSocket(header, visibleBody);
                return;
            }

            BroadcastNativeCryCharm(new LegacyGateType18
            {
                FilterUserIndex = 0,
                Recog = ObjectId,
                Ident = Grobal2.SM_MICROWHELK,
                Param = palette,
                Tag = tag,
                Series = 0,
                TextBytes = visibleBody,
                AppendTextTerminator = false
            });
        }

        private static byte[] ExtractNativeCryCharmBody(string rawLine,
            byte[] rawPayload, int bodyLength)
        {
            byte[] source;
            var logicalLength = 0;
            if (rawPayload != null)
            {
                source = rawPayload;
                logicalLength = Math.Clamp(bodyLength, 0, source.Length);
            }
            else
            {
                source = HUtil32.GbkEncoding.GetBytes(rawLine ?? string.Empty);
                logicalLength = source.Length;
            }

            if (logicalLength > 0 && source[logicalLength - 1] == 0)
                logicalLength--;
            if (logicalLength < 5)
                return null;

            var result = new byte[logicalLength - 4];
            Buffer.BlockCopy(source, 4, result, 0, result.Length);
            return result;
        }

        private bool TryGetNativeCryCharm(out TUserItem charm,
            out GoodItem stdItem)
        {
            charm = m_UseItems != null && m_UseItems.Length > Grobal2.U_CHARM
                ? m_UseItems[Grobal2.U_CHARM]
                : null;
            stdItem = charm == null
                ? null
                : M2Share.UserEngine?.GetStdItem(charm.wIndex);
            return charm != null
                   && charm.Dura > 0
                   && stdItem != null
                   && NativeItemFactory.IsClassOrDescendantOf(stdItem,
                       "TCryCharm");
        }

        private bool UseNativeCryCharm(TUserItem item)
        {
            if (m_btRaceServer != Grobal2.RC_PLAYOBJECT
                || item == null || item.Dura == 0)
                return false;

            item.Dura = item.Dura >= NativeCryCharmDuraCost
                ? unchecked((ushort)(item.Dura - NativeCryCharmDuraCost))
                : (ushort)0;
            return true;
        }

        private static bool ValidateNativeCryCharmPictures(string input)
        {
            foreach (Match match in NativeCryCharmPiRegex.Matches(input))
            {
                var parsed = int.TryParse(match.Groups[2].Value,
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var value)
                    ? value
                    : 1025;
                if (parsed > 1024)
                    return false;
            }
            return true;
        }

        private bool ValidateAndBroadcastNativeCryCharmItems(string input)
        {
            var items = new List<TUserItem>();
            foreach (Match match in NativeCryCharmItemRegex.Matches(input))
            {
                var clientItemId = int.TryParse(match.Groups[1].Value,
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var value)
                    ? value
                    : -1;
                var item = FindOwnedItemByClientId(clientItemId, false);
                var stdItem = item == null
                    ? null
                    : M2Share.UserEngine?.GetStdItem(item.wIndex);
                if (item == null || stdItem == null
                    || !string.Equals(match.Groups[3].Value, stdItem.Name,
                        StringComparison.Ordinal))
                    return false;
                items.Add(item);
            }

            foreach (var item in items)
                BroadcastNativeCryCharmItem(BuildNativeCryCharmItemPacket(item));
            return true;
        }

        private InternalPacket77 BuildNativeCryCharmItemPacket(TUserItem item)
        {
            var clientItemId = EnsureClientItemId(item);
            var record = EncodeOwnedClientItemRecord(item);
            var payload = new byte[LegacyGateType18.ClientPacketSize
                                   + record.Length];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4),
                clientItemId);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2),
                unchecked((ushort)Grobal2.SM_QUERY_FOCUS_ITEM));
            record.CopyTo(payload, LegacyGateType18.ClientPacketSize);
            return InternalPacket77.FromClientFrame(0, 0, 24, payload);
        }

        internal virtual void BroadcastNativeCryCharm(
            LegacyGateType18 packet)
        {
            M2Share.GateManager?.BroadcastLegacyType18(packet);
        }

        internal virtual void BroadcastNativeCryCharmItem(
            InternalPacket77 packet)
        {
            M2Share.GateManager?.BroadcastInternalPacket77(packet);
        }

        private void SendNativeCryCharmSystemMessage(string message,
            ushort colorWord)
        {
            SendMsg(this, Grobal2.RM_SYSMESSAGE, 0, colorWord & 0xFF,
                colorWord >> 8, 0, message);
        }
    }
}
