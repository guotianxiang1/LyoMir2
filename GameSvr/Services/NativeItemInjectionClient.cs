using System.Globalization;
using DBSvr.Core;
using SystemModule;
using SystemModule.Packet;

namespace GameSvr.Services
{
    internal static class NativeItemInjectionClient
    {
        internal const string SuccessPrefix = "成功交易 ";
        internal const string SuccessSeparator = " 给 ";
        internal const string FailurePrefix = "给予失败: 没有这个玩家或 ";
        internal const string FailureSuffix = "背包空位不足。";

        internal static void ProcessResponse(LegacyDbServerFrame frame)
        {
            if (!NativeItemInjectionProtocol.TryDecodeMailResponse(frame,
                    out var response, out _))
                return;

            var senderName = HUtil32.GbkEncoding.GetString(
                response.CharacterName);
            var sender = M2Share.UserEngine?.GetPlayObject(senderName);
            if (sender == null || sender.m_boGhost || !sender.m_boReadyRun)
                return;

            var targetName = HUtil32.GbkEncoding.GetString(
                response.TargetName);
            if (response.Status != NativeItemInjectionProtocol.Success)
            {
                sender.SysMsg(FailurePrefix + targetName + FailureSuffix,
                    MsgColor.Red, MsgType.Hint);
                return;
            }

            var item = FindFirstByMakeIndex(sender.m_ItemList,
                response.MakeIndex);
            if (item == null)
                return;

            var stdItem = M2Share.UserEngine?.GetStdItem(item.wIndex);
            if (stdItem == null || !sender.m_ItemList.Remove(item))
                return;

            sender.SendDelItems(item);

            var itemName = stdItem.Name ?? string.Empty;
            var count = stdItem.StdMode == 7 ? item.Dura : 1;
            M2Share.AddGameDataLog(string.Join('\t',
                "8",
                sender.m_sMapName ?? string.Empty,
                sender.m_nCurrX.ToString(CultureInfo.InvariantCulture),
                sender.m_nCurrY.ToString(CultureInfo.InvariantCulture),
                sender.m_sCharName ?? string.Empty,
                itemName,
                unchecked((uint)item.MakeIndex).ToString(
                    CultureInfo.InvariantCulture),
                count.ToString(CultureInfo.InvariantCulture),
                targetName));

            sender.SysMsg(SuccessPrefix + itemName + SuccessSeparator
                          + targetName, MsgColor.Green, MsgType.Hint);
            sender.Dispose(item);
        }

        private static TUserItem FindFirstByMakeIndex(
            IList<TUserItem> items, int makeIndex)
        {
            if (items == null)
                return null;

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item != null && item.MakeIndex == makeIndex)
                    return item;
            }
            return null;
        }
    }
}
