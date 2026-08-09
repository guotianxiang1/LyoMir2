using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to give an item TO a specific player.
    /// Usage: @GiveUserItem PlayerName ItemName [Count]
    /// Reverse direction of GetUserItems (which takes FROM). Creates items and adds to target's bag.
    /// </summary>
    [GameCommand("GiveUserItem", "给指定玩家物品", "人物名称 物品名称 数量(默认1)", 4)]
    public class GiveUserItemCommand : BaseCommond
    {
        [DefaultCommand]
        public void GiveUserItem(string[] @Params, TPlayObject PlayObject)
        {
            if (@Params == null)
            {
                return;
            }
            var sHumanName = @Params.Length > 0 ? @Params[0] : "";
            var sItemName = @Params.Length > 1 ? @Params[1] : "";
            var nCount = @Params.Length > 2 ? HUtil32.Str_ToInt(@Params[2], 1) : 1;
            if (string.IsNullOrEmpty(sHumanName) || string.IsNullOrEmpty(sItemName) || nCount <= 0)
            {
                PlayObject.SysMsg(GameCommand.ShowHelp, MsgColor.Red, MsgType.Hint);
                return;
            }
            if (nCount > 50) nCount = 50; // safety limit
            var m_PlayObject = M2Share.UserEngine.GetPlayObject(sHumanName);
            if (m_PlayObject == null)
            {
                PlayObject.SysMsg(string.Format(M2Share.g_sNowNotOnLineOrOnOtherServer, sHumanName), MsgColor.Red, MsgType.Hint);
                return;
            }
            var nGiven = 0;
            for (var i = 0; i < nCount; i++)
            {
                if (m_PlayObject.m_ItemList.Count >= Grobal2.MAXBAGITEM)
                {
                    PlayObject.SysMsg($"{sHumanName} 的背包已满，已给予 {nGiven} 件。", MsgColor.Red, MsgType.Hint);
                    break;
                }
                TUserItem UserItem = null;
                if (M2Share.UserEngine.CopyToUserItemFromName(sItemName, ref UserItem))
                {
                    var StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                    if (StdItem != null)
                    {
                        if (M2Share.RandomNumber.Random(M2Share.g_Config.nMakeRandomAddValue) == 0)
                        {
                            StdItem.RandomUpgradeItem(UserItem);
                        }
                    }
                    m_PlayObject.m_ItemList.Add(UserItem);
                    m_PlayObject.SendAddItem(UserItem);
                    nGiven++;
                }
                else
                {
                    PlayObject.SysMsg(string.Format(M2Share.g_sGamecommandMakeItemNameNotFound, sItemName), MsgColor.Red, MsgType.Hint);
                    break;
                }
            }
            if (nGiven > 0)
            {
                PlayObject.SysMsg($"已给予 {sHumanName} {sItemName} x{nGiven}。", MsgColor.Green, MsgType.Hint);
                m_PlayObject.SysMsg($"GM 给予你 {sItemName} x{nGiven}。", MsgColor.Green, MsgType.Hint);
                M2Share.MainOutMessage($"[给予物品] {PlayObject.m_sCharName} 给予 {sHumanName} {sItemName} x{nGiven}");
            }
        }
    }
}
