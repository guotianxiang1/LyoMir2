using System.Buffers.Binary;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr
{
    public partial class TPlayObject
    {
        internal const int NativeQuestOrderRequestIdent = 1060;
        internal const int NativeOrderListResponseIdent = 1108;

        private const int NativeHeroOverallRankingOffset = 0x00B0;
        private const int NativeHeroJobRankingOffset = 0x00B2;

        internal bool HandleNativeQuestOrder(int requestedPage, byte category)
        {
            // 眼神「屏蔽排行榜」把本函数（sub_6CBA88）的序言首字节 55 改成 C3，
            // 整个处理在建栈帧之前就返回：不回包、无副作用。返回值两侧都丢弃。
            // 证据与 opcode 1060 的推导见 Plugins/YanshenHideRank。
            if (Plugins.YanshenHideRank.HandlerStubbed()) return true;
            if (category is 9 or 10) return true;
            if (!TryCreateNativeQuestOrderResponse(requestedPage, category,
                    out var header, out var body))
                return false;
            var recipient = ResolveNativeQuestOrderRecipient(category);
            recipient?.SendSocket(header, body);
            return true;
        }

        internal TPlayObject ResolveNativeQuestOrderRecipient(byte category)
        {
            // Only category 16 is built by sub_60EFE4. Its two send legs call
            // sub_652784 by character name immediately before VMT+0x254.
            // The ordinary sub_6CBA88 ranking categories send on Self.
            return category == 16
                ? M2Share.UserEngine?.GetNativeReadyPlayObject(m_sCharName)
                : this;
        }

        internal bool TryCreateNativeQuestOrderResponse(int requestedPage,
            byte category, out ClientPacket header, out byte[] body)
        {
            header = null;
            body = Array.Empty<byte>();
            if (category is 9 or 10) return false;

            if (category == 16)
            {
                if (M2Share.HonorValueManager == null)
                {
                    if (requestedPage != -1) return false;
                    header = Grobal2.MakeDefaultMsg(NativeOrderListResponseIdent,
                        -2, category, 0, 0);
                    return true;
                }

                if (!M2Share.HonorValueManager.TryCreateRankingPage(
                        requestedPage, m_sCharName, out var honorPage,
                        out var lastHonorPage, out body))
                    return false;
                header = Grobal2.MakeDefaultMsg(NativeOrderListResponseIdent,
                    honorPage, category, lastHonorPage, 0);
                return true;
            }

            var correctedPage = requestedPage;
            if (requestedPage == -1)
            {
                var ranking = GetNativeQuestOrderPersonalRanking(category);
                correctedPage = ranking == 0 ? -1 : (ranking - 1) / 7;
            }

            if (correctedPage >= 0 && category <
                Services.NativeType2SecondaryRankingState.BucketCount)
            {
                var bodyLength = 0;
                if (M2Share.DataServer == null
                    || !M2Share.DataServer.TryGetSecondaryRankingPage(category,
                        correctedPage, out correctedPage, out bodyLength,
                        out body))
                {
                    correctedPage = -1;
                    body = Array.Empty<byte>();
                }
                else if (bodyLength > 0 && body.Length != bodyLength)
                {
                    var exactBody = new byte[bodyLength];
                    body.AsSpan(0, Math.Min(body.Length, bodyLength))
                        .CopyTo(exactBody);
                    body = exactBody;
                }
            }

            if (requestedPage == -1 && correctedPage == -1)
                correctedPage = -2;

            header = Grobal2.MakeDefaultMsg(NativeOrderListResponseIdent,
                correctedPage, category, 2,
                GetNativeCurrentPersonalRanking());
            return true;
        }

        private ushort GetNativeQuestOrderPersonalRanking(byte category)
        {
            var current = GetNativeCurrentPersonalRanking();
            return category switch
            {
                <= 2 when m_btJob == category => current,
                3 => GetNativeOverallPersonalRanking(),
                >= 4 and <= 6 when m_HeroObject != null
                    && m_HeroObject.m_btJob == category - 4 =>
                    ReadNativeHeroRanking(NativeHeroJobRankingOffset),
                7 => ReadNativeHeroRanking(NativeHeroOverallRankingOffset),
                8 => GetNativeApprenticeRanking(),
                13 when m_btJob == 3 => current,
                _ => 0
            };
        }

        private ushort ReadNativeHeroRanking(int offset)
        {
            var record = m_HeroObject?.NativeHeroState?.FixedRecord;
            return record != null && record.Length >= offset + sizeof(ushort)
                ? BinaryPrimitives.ReadUInt16LittleEndian(
                    record.AsSpan(offset, sizeof(ushort)))
                : (ushort)0;
        }
    }
}
