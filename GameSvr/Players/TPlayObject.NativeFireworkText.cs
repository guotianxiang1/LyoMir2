using SystemModule;
using SystemModule.Packet;

namespace GameSvr
{
    public partial class TPlayObject
    {
        private const int NativeFireworkTextLifetime = 88000;
        private const int NativeFireworkTextMaximumBytes = 12;

        private void ClientNativeFireworkText(TProcessMessage processMessage)
        {
            if (processMessage == null || m_PEnvir == null ||
                M2Share.EventManager == null)
                return;
            // CM_YANHUA_TEXT body is 6-bit-ENCODED on the wire (non-raw ident):
            // decode before rendering, else the firework shows encoded garbage.
            var payload = DecodeNativeSocialBody(processMessage.Payload);
            if (payload.Length <= 1)
                return;

            var item = FindClientItemIn(m_ItemList, processMessage.nParam1);
            if (item == null) return;

            var glyphs = BuildNativeFireworkTextGlyphs(payload, m_nCurrX,
                m_nCurrY, out var logText);
            if (glyphs.Count == 0) return;

            for (var index = 0; index < glyphs.Count; index++)
            {
                var glyph = glyphs[index];
                if (glyph.X < 0 || glyph.X >= m_PEnvir.wWidth ||
                    glyph.Y < 0 || glyph.Y >= m_PEnvir.wHeight)
                {
                    SendMsg(this, Grobal2.RM_SYSMESSAGE, 0,
                        0xDB, 0xFF, 0, "此处无法施放传情烟花");
                    return;
                }
            }

            for (var index = 0; index < glyphs.Count; index++)
            {
                var glyph = glyphs[index];
                var fireworkEvent = new FireworksEvent(m_PEnvir, glyph.X,
                    glyph.Y, NativeFireworkTextLifetime, glyph.Text,
                    glyph.RawBytes);
                M2Share.EventManager.AddEvent(fireworkEvent);
            }

            var itemName = M2Share.UserEngine?.GetStdItemName(item.wIndex)
                           ?? string.Empty;
            M2Share.AddGameDataLog(string.Join('\t', 100,
                m_PEnvir.sMapName, m_nCurrX, m_nCurrY, m_sCharName,
                itemName, item.ClientItemID, 1, logText));

            var broadcast = string.Format(
                "{0}在{1}[{2},{3}]施放{4}，请大家前往观看.",
                m_sCharName, m_PEnvir.sMapDesc, m_nCurrX, m_nCurrY,
                itemName);
            M2Share.GateManager?.BroadcastLegacyType18(
                BuildNativeFireworkBroadcastPacket(broadcast));

            m_ItemList.Remove(item);
            SendDelItems(item);
            Dispose(item);
            WeightChanged();
        }

        private static LegacyGateType18 BuildNativeFireworkBroadcastPacket(
            string text)
        {
            return new LegacyGateType18
            {
                FilterUserIndex = 0,
                Recog = 0,
                Ident = Grobal2.SM_SYSMESSAGE,
                Param = 0x38FF,
                Tag = 0,
                Series = 0,
                TextBytes = HUtil32.GbkEncoding.GetBytes(text ?? string.Empty)
            };
        }

        private static List<(string Text, int X, int Y, byte[] RawBytes)>
            BuildNativeFireworkTextGlyphs(byte[] payload, int startX,
                int startY, out string logText)
        {
            var glyphs = new List<(string Text, int X, int Y,
                byte[] RawBytes)>();
            logText = string.Empty;
            if (payload == null || payload.Length <= 1) return glyphs;

            var visibleByteLength = Math.Min(NativeFireworkTextMaximumBytes,
                payload.Length - 1);
            if (visibleByteLength <= 0) return glyphs;

            logText = HUtil32.GbkEncoding.GetString(payload, 0,
                visibleByteLength);
            var byteIndex = 0;
            var glyphIndex = 0;
            while (byteIndex < visibleByteLength)
            {
                var glyphByteLength = unchecked((sbyte)payload[byteIndex]) < 0
                    ? 2
                    : 1;
                if (glyphByteLength == 2 && byteIndex + 1 >= payload.Length)
                    break;

                var text = HUtil32.GbkEncoding.GetString(payload, byteIndex,
                    glyphByteLength);
                var rawBytes = new byte[glyphByteLength];
                Buffer.BlockCopy(payload, byteIndex, rawBytes, 0,
                    glyphByteLength);
                glyphs.Add((text, startX + glyphIndex * 2, startY,
                    rawBytes));
                byteIndex += glyphByteLength;
                glyphIndex++;
            }
            return glyphs;
        }
    }
}
