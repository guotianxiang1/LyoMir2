using System.Globalization;
using GameSvr.CommandSystem;
using GameSvr.Services;
using SystemModule;

namespace GameSvr
{
    [GameCommand("GetUserItem", "取回指定玩家物品",
        "<PlayerName> <ItemID>", 4)]
    public class GetUserItemCommand : BaseCommond
    {
        internal const string NativeHelp =
            "@GetUserItem <PlayerName> <ItemID>";
        internal const string BagCapacityMessage =
            "请确认有足够的包裹空位。";

        [DefaultCommand]
        public void GetUserItem(string[] @Params, TPlayObject PlayObject)
        {
            var targetName = @Params != null && @Params.Length > 0
                ? @Params[0]
                : string.Empty;
            var itemIdText = @Params != null && @Params.Length > 1
                ? @Params[1]
                : string.Empty;
            if (string.IsNullOrEmpty(targetName)
                || string.IsNullOrEmpty(itemIdText))
            {
                PlayObject.SysMsg(NativeHelp, MsgColor.Green, MsgType.Hint);
                return;
            }

            if (!PlayObject.IsEnoughBag())
            {
                PlayObject.SysMsg(BagCapacityMessage,
                    MsgColor.Red, MsgType.Hint);
                return;
            }

            var makeIndex = ParseMakeIndex(itemIdText);
            var target = M2Share.UserEngine.GetPlayObject(targetName);
            TUserItem item = null;
            if (target != null)
            {
                NativeOnlineItemExtraction.TryExtract(target,
                    PlayObject.m_sCharName,
                    makeIndex, out item);
                if (item == null && target.m_HeroObject != null
                                 && !target.m_HeroObject.m_boGhost)
                {
                    NativeOnlineItemExtraction.TryExtract(target.m_HeroObject,
                        PlayObject.m_sCharName, makeIndex, out item);
                }
            }

            if (item == null)
            {
                NativeItemExtractionClient.SendRequest(PlayObject,
                    targetName, makeIndex);
                return;
            }

            if (PlayObject.AddItemToBag(item))
                PlayObject.SendAddItem(item);

            var itemName = ItmUnit.GetItemName(item);
            PlayObject.SysMsg("成功得到" + targetName + "的身上物品"
                              + itemName + "("
                              + unchecked((uint)item.MakeIndex).ToString(
                                  CultureInfo.InvariantCulture) + ")",
                MsgColor.Red, MsgType.Hint);
        }

        private static int ParseMakeIndex(string value)
        {
            return uint.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var makeIndex)
                ? unchecked((int)makeIndex)
                : HUtil32.Str_ToInt(value, 0);
        }

    }
}
