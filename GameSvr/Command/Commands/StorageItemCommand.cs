using GameSvr.CommandSystem;
using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// GM command to access a player's storage warehouse.
    /// Usage: @StorageItem PlayerName
    /// Displays the target player's storage contents or opens storage dialog.
    /// Without a parameter, shows the GM's own storage.
    /// </summary>
    [GameCommand("StorageItem", "查看/操作玩家仓库", "人物名称(可选,不填则查看自己的)", 4)]
    public class StorageItemCommand : BaseCommond
    {
        [DefaultCommand]
        public void StorageItem(string[] @Params, TPlayObject PlayObject)
        {
            var sHumanName = @Params != null && @Params.Length > 0 ? @Params[0] : "";
            TPlayObject m_PlayObject;
            if (string.IsNullOrEmpty(sHumanName))
            {
                m_PlayObject = PlayObject;
            }
            else
            {
                m_PlayObject = M2Share.UserEngine.GetPlayObject(sHumanName);
                if (m_PlayObject == null)
                {
                    PlayObject.SysMsg(string.Format(M2Share.g_sNowNotOnLineOrOnOtherServer, sHumanName), MsgColor.Red, MsgType.Hint);
                    return;
                }
            }
            if (m_PlayObject.m_StorageItemList == null || m_PlayObject.m_StorageItemList.Count == 0)
            {
                PlayObject.SysMsg($"{m_PlayObject.m_sCharName} 的仓库为空。", MsgColor.Green, MsgType.Hint);
                return;
            }
            PlayObject.SysMsg($"===== {m_PlayObject.m_sCharName} 的仓库物品 (共 {m_PlayObject.m_StorageItemList.Count} 件) =====", MsgColor.Green, MsgType.Hint);
            for (var i = 0; i < m_PlayObject.m_StorageItemList.Count; i++)
            {
                var UserItem = m_PlayObject.m_StorageItemList[i];
                if (UserItem != null && UserItem.wIndex > 0)
                {
                    var StdItem = M2Share.UserEngine.GetStdItem(UserItem.wIndex);
                    var sItemName = StdItem != null ? StdItem.Name : $"未知物品(ID:{UserItem.wIndex})";
                    var nDura = UserItem.Dura;
                    var nDuraMax = UserItem.DuraMax;
                    var sDura = nDuraMax > 0 ? $" 持久 [{nDura}/{nDuraMax}]" : "";
                    PlayObject.SysMsg($"  [{i + 1}] {sItemName} (MakeIndex:{UserItem.MakeIndex}){sDura}", MsgColor.Green, MsgType.Hint);
                }
            }
            // Send the storage item list to the player so the client can render the warehouse dialog
            m_PlayObject.SendSaveItemList(m_PlayObject.ObjectId);
        }
    }
}
