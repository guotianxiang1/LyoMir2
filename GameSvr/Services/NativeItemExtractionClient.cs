using System.Globalization;
using DBSvr.Core;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr.Services
{
    internal static class NativeItemExtractionClient
    {
        internal const string BagCapacityMessage = "请确认有足够的包裹空位。";
        internal const string MissingItemSuffix = " 身上没有该物品。";

        internal static bool TryEncodeRequest(TPlayObject requester,
            string targetName, int makeIndex, out byte[] wire, out string error)
        {
            wire = null;
            error = string.Empty;
            if (requester == null)
            {
                error = "native GetUserItem requester is null";
                return false;
            }

            var frame = NativeItemExtractionProtocol.CreateRequest(makeIndex,
                HUtil32.GbkEncoding.GetBytes(requester.m_sUserID ?? string.Empty),
                HUtil32.GbkEncoding.GetBytes(requester.m_sCharName ?? string.Empty),
                HUtil32.GbkEncoding.GetBytes(targetName ?? string.Empty));
            return LegacyDbServerFrameCodec.TryEncode(frame, out wire, out error);
        }

        internal static bool SendRequest(TPlayObject requester,
            string targetName, int makeIndex)
        {
            if (!TryEncodeRequest(requester, targetName, makeIndex,
                    out var wire, out var error))
            {
                M2Share.ErrorMessage("[GetUserItem] 原生0153请求编码失败: " + error);
                return false;
            }

            return M2Share.DataServer != null
                   && M2Share.DataServer.SendNativeFrame(wire);
        }

        internal static void ProcessResponse(LegacyDbServerFrame frame)
        {
            if (!NativeItemExtractionProtocol.TryDecodeResponse(frame,
                    out var response, out _))
                return;

            var requesterName = HUtil32.GbkEncoding.GetString(
                response.RequesterName);
            var requester = M2Share.UserEngine?.GetPlayObject(requesterName);
            if (requester == null || requester.m_boGhost
                                  || !requester.m_boReadyRun)
                return;

            var targetName = HUtil32.GbkEncoding.GetString(response.TargetName);
            if (response.Status != NativeItemExtractionProtocol.Success
                || response.ItemRecord.Length
                != NativeItemExtractionProtocol.ItemSize)
            {
                requester.SysMsg(targetName + MissingItemSuffix,
                    MsgColor.Red, MsgType.Hint);
                return;
            }

            if (!NativeMailAttachmentCodec.TryDecode(response.ItemRecord,
                    out var item, out _))
                return;

            if (item.MakeIndex == 0)
                return;

            var stdItem = M2Share.UserEngine?.GetStdItem(item.wIndex);
            if (stdItem == null)
                return;

            var itemName = stdItem.Name ?? string.Empty;
            WriteSuccessLog(requester, targetName, item, itemName,
                stdItem.StdMode == 7);
            requester.SysMsg("成功收取 " + targetName + " 的 " + itemName
                             + "(" + unchecked((uint)item.MakeIndex)
                                 .ToString(CultureInfo.InvariantCulture) + ")",
                MsgColor.Green, MsgType.Hint);

            if (requester.AddItemToBag(item))
                requester.SendAddItem(item);
        }

        private static void WriteSuccessLog(TPlayObject requester,
            string targetName, TUserItem item, string itemName,
            bool durabilityIsCount)
        {
            var count = durabilityIsCount ? item.Dura : 1;
            M2Share.AddGameDataLog(string.Join('\t', "8",
                requester.m_sMapName ?? string.Empty,
                requester.m_nCurrX,
                requester.m_nCurrY,
                requester.m_sCharName ?? string.Empty,
                itemName,
                unchecked((uint)item.MakeIndex),
                count,
                targetName ?? string.Empty));
        }
    }
}
